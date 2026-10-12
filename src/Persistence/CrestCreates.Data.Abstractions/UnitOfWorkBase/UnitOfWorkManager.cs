using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元管理器：唯一执行内核。
    /// </summary>
    /// <remarks>
    /// <para>开始协议（调用方帧激活，见设计 §4.1）：<see cref="BeginScope"/> 为同步方法，在调用方帧内完成
    /// 链节点/受管载体登记与环境激活（不得依赖 async 方法体内的 AsyncLocal 写入）；异步开始经
    /// <see cref="IUnitOfWorkScope.StartAsync"/>。委托式执行优先 <see cref="ExecuteAsync{TResult}"/>。</para>
    /// <para>传播：Required 复用（join 记录参与者成功、不得提交/释放外层）；RequiresNew 受管子 scope 隔离
    /// （先 Push 内层 ambient 再构造 UoW；声明的 Provider 不支持时确定性诊断）。参数与外层冲突（事务开关/
    /// 显式 Provider/显式隔离级别/超时延长）在执行前确定性失败，不静默降级。</para>
    /// <para>完成顺序：rollback-only 校验 → flush → commit（确认即记录 Committed）→ 提交后通知 → 清理。
    /// 提交结果未知（Unknown）不被回滚尝试改写；通知失败保留已提交事实，不回滚、不自动重试业务。</para>
    /// <para>释放协议：恢复段在调用方帧同步完成（Dispose/DisposeAsync 的同步段），资源与事务句柄恰好释放一次；
    /// 未完成退出的 owner scope 立即回滚并释放，join 未完成退出标记 owner rollback-only。</para>
    /// </remarks>
    public class UnitOfWorkManager : IUnitOfWorkManager
    {
        /// <summary>释放阶段清理异常附加到业务主异常 <see cref="Exception.Data"/> 的键。</summary>
        internal const string CleanupExceptionDataKey = "CrestCreates.UnitOfWork.CleanupException";

        /// <summary>回滚终结失败附加到业务主异常 <see cref="Exception.Data"/> 的键。</summary>
        internal const string RollbackTerminationFailureDataKey = "CrestCreates.UnitOfWork.RollbackTerminationFailure";

        private readonly IUnitOfWorkFactory _factory;
        private readonly UnitOfWorkProviderBindingRegistry? _bindings;
        private readonly IServiceScopeFactory? _scopeFactory;
        private readonly OrmProvider? _explicitDefault;
        private readonly UnitOfWorkChainNode? _chainNode;
        private readonly IServiceProvider? _serviceProvider;
        private readonly bool _ownsResolvedUnitOfWorks;
        private readonly AsyncLocal<AmbientFrame?> _currentFrame = new();

        /// <summary>
        /// DI 主链构造函数
        /// </summary>
        public UnitOfWorkManager(
            IUnitOfWorkFactory factory,
            UnitOfWorkProviderBindingRegistry bindings,
            IServiceScopeFactory scopeFactory,
            OrmProvider? explicitDefault = null,
            UnitOfWorkChainNode? chainNode = null,
            IServiceProvider? serviceProvider = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _explicitDefault = explicitDefault;
            _chainNode = chainNode;
            _serviceProvider = serviceProvider;
            _ownsResolvedUnitOfWorks = false;
        }

        /// <summary>
        /// 手动构造（自定义工厂 / 单元测试）：不使用绑定索引与子作用域，
        /// requiresNew 语义取决于自定义工厂是否每次返回独立实例。
        /// </summary>
        public UnitOfWorkManager(IUnitOfWorkFactory factory, OrmProvider defaultProvider = OrmProvider.EfCore)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _explicitDefault = defaultProvider;
            _ownsResolvedUnitOfWorks = true;
        }

        /// <inheritdoc />
        public IUnitOfWork? CurrentOrNull => _currentFrame.Value?.Scope.UnitOfWork;

        /// <inheritdoc />
        public IUnitOfWork Current
        {
            get
            {
                var current = CurrentOrNull;
                if (current == null)
                {
                    throw new InvalidOperationException("No active unit of work. Call BeginScope first.");
                }
                return current;
            }
        }

        /// <inheritdoc />
        public IUnitOfWorkScope BeginScope(UnitOfWorkOptions? options = null)
        {
            options ??= new UnitOfWorkOptions();
            ValidateOptions(options);

            var frame = _currentFrame.Value;
            if (frame is not null && options.Propagation == UnitOfWorkPropagation.Required)
            {
                return UnitOfWorkScope.CreateJoined(this, frame.Scope, options);
            }

            if (frame is not null)
            {
                return BeginIsolatedScope(frame, options);
            }

            return BeginRootScope(frame, options);
        }

        /// <inheritdoc />
        public async Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> action,
            UnitOfWorkOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);

            var scope = (UnitOfWorkScope)BeginScope(options);
            Exception? primary = null;
            try
            {
                await scope.StartAsync(cancellationToken).ConfigureAwait(false);
                var result = await action(scope.ExecutionToken).ConfigureAwait(false);
                await scope.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception)
            {
                primary = exception;
                var rollbackTerminationFailure = await scope.TryRollbackOnFailureAsync().ConfigureAwait(false);
                if (rollbackTerminationFailure is not null)
                {
                    AttachCleanupDiagnostic(primary, RollbackTerminationFailureDataKey, rollbackTerminationFailure);
                }
                throw;
            }
            finally
            {
                try
                {
                    scope.Dispose();
                }
                catch (UnitOfWorkCleanupException cleanupException) when (primary is not null)
                {
                    // 释放阶段的次级失败以结构化诊断附加到业务主异常（Data），
                    // 根因（类型/栈）保留；无主异常时 Dispose 照常向外抛出 UnitOfWorkCleanupException。
                    AttachCleanupDiagnostic(primary, CleanupExceptionDataKey, cleanupException);
                }
            }
        }

        /// <inheritdoc />
        public TResult Execute<TResult>(
            Func<TResult> action,
            UnitOfWorkOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);
            return ExecuteAsync(_ => Task.FromResult(action()), options, cancellationToken)
                .GetAwaiter()
                .GetResult();
        }

        private static void ValidateOptions(UnitOfWorkOptions options)
        {
            if (options.Propagation is not (UnitOfWorkPropagation.Required or UnitOfWorkPropagation.RequiresNew))
            {
                throw new InvalidOperationException(
                    $"Unsupported unit-of-work propagation '{options.Propagation}'. Supported values: Required, RequiresNew.");
            }

            if (options.Timeout is { } timeout && timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    timeout,
                    "Unit-of-work Timeout must be a positive duration.");
            }
        }

        /// <summary>
        /// 把次级清理/终结诊断附加到业务主异常的可检查通道（<see cref="Exception.Data"/>），
        /// 不改写主异常的类型/栈/消息。
        /// </summary>
        private static void AttachCleanupDiagnostic(Exception primary, string key, Exception diagnostic)
        {
            primary.Data[key] = diagnostic;
        }

        private UnitOfWorkScope BeginRootScope(AmbientFrame? frame, UnitOfWorkOptions options)
        {
            var provider = ResolveProvider(options.Provider);
            ValidateIsolationCapability(provider, options);
            var unitOfWork = _factory.Create(provider);
            var tenantSnapshot = ResolveTenantSnapshot(provider, _serviceProvider);
            var node = new UnitOfWorkChainNode();
            node.AttachTo(frame?.Scope.Node ?? _chainNode);
            return UnitOfWorkScope.CreateOwner(
                this,
                unitOfWork,
                provider,
                options,
                node,
                tenantSnapshot,
                parentFrame: frame,
                ownedScope: null,
                ambientToken: null,
                ownsUnitOfWorkDirectly: _ownsResolvedUnitOfWorks);
        }

        private UnitOfWorkScope BeginIsolatedScope(AmbientFrame parentFrame, UnitOfWorkOptions options)
        {
            var provider = ResolveProvider(options.Provider);
            ValidateIsolationCapability(provider, options);
            var binding = _bindings?.GetRequired(provider);

            if (binding is not null && !binding.SupportsRequiresNew)
            {
                throw new NotSupportedException(
                    $"The registered '{provider}' provider does not support requiresNew isolation: " +
                    "it cannot bind already-injected business dependencies to an independent transaction context. " +
                    "Do not request requiresNew for this provider.");
            }

            if (_scopeFactory is null)
            {
                // Manual construction without DI scope support (unit tests / custom hosts).
                // The custom factory is expected to return an independent instance per call.
                var fallback = _factory.Create(provider);
                var manualNode = new UnitOfWorkChainNode();
                manualNode.AttachTo(parentFrame.Scope.Node);
                return UnitOfWorkScope.CreateOwner(
                    this,
                    fallback,
                    provider,
                    options,
                    manualNode,
                    ResolveTenantSnapshot(provider, _serviceProvider),
                    parentFrame: parentFrame,
                    ownedScope: null,
                    ambientToken: null,
                    ownsUnitOfWorkDirectly: _ownsResolvedUnitOfWorks);
            }

            var childScope = _scopeFactory.CreateScope();
            IDisposable? ambientToken = null;
            try
            {
                var tenantSnapshot = ResolveTenantSnapshot(provider, childScope.ServiceProvider);

                // Push the isolated ambient context BEFORE constructing the unit of
                // work: construction must capture the child scope's own resources,
                // never be redirected to a parent context via ambient routing.
                if (binding!.AmbientContextFactory is not null)
                {
                    var ambientContext = binding.AmbientContextFactory(childScope.ServiceProvider)
                        ?? throw new InvalidOperationException(
                            $"The '{provider}' ambient context factory returned null; requiresNew isolation " +
                            "requires a resolvable resource context.");
                    var childNode = childScope.ServiceProvider.GetRequiredService<UnitOfWorkChainNode>();
                    childNode.AttachTo(parentFrame.Scope.Node);
                    ambientToken = UnitOfWorkAmbientContext.Push(ambientContext, childNode, tenantSnapshot);
                }

                var childFactory = childScope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
                var unitOfWork = childFactory.Create(provider);

                return UnitOfWorkScope.CreateOwner(
                    this,
                    unitOfWork,
                    provider,
                    options,
                    childScope.ServiceProvider.GetRequiredService<UnitOfWorkChainNode>(),
                    tenantSnapshot,
                    parentFrame: parentFrame,
                    ownedScope: childScope,
                    ambientToken: ambientToken,
                    ownsUnitOfWorkDirectly: false);
            }
            catch
            {
                ambientToken?.Dispose();
                childScope.Dispose();
                throw;
            }
        }

        private OrmProvider ResolveProvider(OrmProvider? requested)
        {
            if (_bindings is not null)
            {
                return _bindings.ResolveProvider(requested, _explicitDefault);
            }

            return requested ?? _explicitDefault ?? OrmProvider.EfCore;
        }

        /// <summary>
        /// 解析受管资源的租户身份快照（host/null 是有效身份值，不能被当作关闭校验的开关）。
        /// </summary>
        private TenantKeySnapshot ResolveTenantSnapshot(OrmProvider provider, IServiceProvider? serviceProvider)
        {
            var factory = _bindings?.GetRequired(provider).TenantKeyFactory;
            if (factory is null || serviceProvider is null)
            {
                return TenantKeySnapshot.NotParticipating;
            }

            return new TenantKeySnapshot(factory(serviceProvider));
        }

        /// <summary>
        /// 执行前能力校验：显式隔离级别必须在 Provider 声明集合内（未声明即不支持，fail closed）。
        /// </summary>
        private void ValidateIsolationCapability(OrmProvider provider, UnitOfWorkOptions options)
        {
            if (options.IsolationLevel is not { } level)
            {
                return;
            }

            var binding = _bindings?.GetRequired(provider);
            if (binding is null)
            {
                // 自定义工厂路径：Provider 语义由自定义工厂自行解释（不经过能力索引）。
                return;
            }

            var supported = _serviceProvider is not null
                ? binding.Capabilities.ResolveSupportedIsolationLevels(_serviceProvider)
                : binding.Capabilities.SupportedIsolationLevels;

            if (!supported.Contains(level))
            {
                var declared = supported.Count == 0
                    ? "<none declared>"
                    : string.Join(", ", supported);
                throw new NotSupportedException(
                    $"The '{provider}' provider does not declare support for isolation level '{level}'. " +
                    $"Declared levels: {declared}. The request was rejected before executing any business code; " +
                    "do not request isolation levels the provider does not declare.");
            }
        }

        /// <summary>
        /// 恢复调用方环境（同步段；必须由调用方帧触发）。仅当本 scope 的帧仍为栈顶时恢复。
        /// </summary>
        internal void RestoreCallerEnvironment(UnitOfWorkScope scope)
        {
            if (scope.FramePushed)
            {
                var frame = _currentFrame.Value;
                if (!ReferenceEquals(frame?.Scope, scope))
                {
                    throw new InvalidOperationException("The unit of work scope was disposed out of order.");
                }

                _currentFrame.Value = frame!.Parent;
            }

            scope.RestoreTokenCarrier();
            scope.RestoreAmbient();
        }

        internal sealed class AmbientFrame
        {
            public AmbientFrame(UnitOfWorkScope scope, AmbientFrame? parent)
            {
                Scope = scope;
                Parent = parent;
            }

            public UnitOfWorkScope Scope { get; }

            public AmbientFrame? Parent { get; }
        }
        /// <summary>
        /// 状态化 scope：承载参与者完成、事务结果、通知结果与释放标志（设计 §3.2）。
        /// </summary>
        internal sealed class UnitOfWorkScope : IUnitOfWorkScope
        {
            private readonly UnitOfWorkManager _manager;
            private readonly IUnitOfWork _unitOfWork;
            private readonly OrmProvider _provider;
            private readonly UnitOfWorkChainNode? _node;
            private readonly TenantKeySnapshot _tenantSnapshot;
            private readonly bool _isOwner;
            private readonly bool _isTransactional;
            private readonly IsolationLevel? _requestedIsolation;
            private readonly TimeSpan? _requestedTimeout;
            private readonly AmbientFrame? _parentFrame;
            private readonly UnitOfWorkScope? _ownerScope;
            private readonly IServiceScope? _ownedScope;
            private readonly IDisposable? _ambientToken;
            private readonly bool _ownsUnitOfWorkDirectly;
            private readonly UnitOfWorkExecutionTokenSource _tokenSource = new();
            private IDisposable? _tokenRestorer;
            private bool _framePushed;
            private bool _started;
            private bool _released;
            private bool _rollbackOnly;
            private Exception? _rollbackTerminationFailure;
            private DateTimeOffset? _deadlineUtc;
            private CancellationTokenSource? _executionCts;
            private UnitOfWorkState _state = UnitOfWorkState.Active;
            private UnitOfWorkTransactionOutcome _transactionOutcome = UnitOfWorkTransactionOutcome.NotStarted;
            private UnitOfWorkNotificationOutcome _notificationOutcome = UnitOfWorkNotificationOutcome.None;

            private UnitOfWorkScope(
                UnitOfWorkManager manager,
                IUnitOfWork unitOfWork,
                OrmProvider provider,
                UnitOfWorkChainNode? node,
                TenantKeySnapshot tenantSnapshot,
                UnitOfWorkOptions options,
                bool isOwner,
                bool isTransactional,
                AmbientFrame? parentFrame,
                UnitOfWorkScope? ownerScope,
                IServiceScope? ownedScope,
                IDisposable? ambientToken,
                bool ownsUnitOfWorkDirectly)
            {
                _manager = manager;
                _unitOfWork = unitOfWork;
                _provider = provider;
                _node = node;
                _tenantSnapshot = tenantSnapshot;
                _isOwner = isOwner;
                _isTransactional = isTransactional;
                _requestedIsolation = options.IsolationLevel;
                _requestedTimeout = options.Timeout;
                _parentFrame = parentFrame;
                _ownerScope = ownerScope;
                _ownedScope = ownedScope;
                _ambientToken = ambientToken;
                _ownsUnitOfWorkDirectly = ownsUnitOfWorkDirectly;
                _tokenSource.IsOwner = isOwner;
                _tokenRestorer = UnitOfWorkExecutionContext.Push(_tokenSource, node, tenantSnapshot);
                if (isOwner)
                {
                    manager._currentFrame.Value = new AmbientFrame(this, parentFrame);
                    _framePushed = true;
                }
            }

            /// <summary>新建 owner scope（顶层或隔离）；调用方帧内完成激活。</summary>
            internal static UnitOfWorkScope CreateOwner(
                UnitOfWorkManager manager,
                IUnitOfWork unitOfWork,
                OrmProvider provider,
                UnitOfWorkOptions options,
                UnitOfWorkChainNode? node,
                TenantKeySnapshot tenantSnapshot,
                AmbientFrame? parentFrame,
                IServiceScope? ownedScope,
                IDisposable? ambientToken,
                bool ownsUnitOfWorkDirectly)
            {
                return new UnitOfWorkScope(
                    manager,
                    unitOfWork ?? throw new InvalidOperationException("The unit-of-work factory returned null."),
                    provider,
                    node,
                    tenantSnapshot,
                    options,
                    isOwner: true,
                    isTransactional: options.IsTransactional,
                    parentFrame,
                    ownerScope: null,
                    ownedScope,
                    ambientToken,
                    ownsUnitOfWorkDirectly);
            }

            /// <summary>join scope：复用外层 owner；调用方帧内完成校验。</summary>
            internal static UnitOfWorkScope CreateJoined(
                UnitOfWorkManager manager,
                UnitOfWorkScope owner,
                UnitOfWorkOptions options)
            {
                if (owner._state != UnitOfWorkState.Active)
                {
                    throw new InvalidOperationException(
                        $"Cannot join a unit of work in state '{owner._state}'. Scope results remain readable, " +
                        "but new participation requires a new unit of work.");
                }

                if (options.IsTransactional != owner.IsTransactional)
                {
                    throw new InvalidOperationException(
                        $"The requested transactional mode (IsTransactional={options.IsTransactional}) conflicts with " +
                        $"the active unit of work (IsTransactional={owner.IsTransactional}). Joining requires a matching value.");
                }

                if (options.Provider is { } requestedProvider && requestedProvider != owner._provider)
                {
                    throw new InvalidOperationException(
                        $"The requested provider '{requestedProvider}' conflicts with the active unit of work " +
                        $"provider '{owner._provider}'. Joining requires a matching provider or null to inherit.");
                }

                if (options.IsolationLevel is { } requestedIsolation && requestedIsolation != owner._requestedIsolation)
                {
                    throw new InvalidOperationException(
                        $"The requested isolation level '{requestedIsolation}' cannot be validated against the active " +
                        $"unit of work (explicit isolation: '{owner._requestedIsolation?.ToString() ?? "<unspecified>"}'). " +
                        "Joining requires a matching explicit isolation level on both scopes.");
                }

                if (options.Timeout is { } requestedTimeout &&
                    owner._deadlineUtc is { } ownerDeadline &&
                    DateTimeOffset.UtcNow + requestedTimeout > ownerDeadline)
                {
                    throw new InvalidOperationException(
                        "The requested timeout would extend beyond the active unit of work's remaining deadline. " +
                        "A joined scope may only tighten the deadline, never extend it.");
                }

                return new UnitOfWorkScope(
                    manager,
                    owner._unitOfWork,
                    owner._provider,
                    owner._node,
                    owner._tenantSnapshot,
                    options,
                    isOwner: false,
                    isTransactional: owner._isTransactional,
                    parentFrame: null,
                    ownerScope: owner,
                    ownedScope: null,
                    ambientToken: null,
                    ownsUnitOfWorkDirectly: false);
            }

            /// <summary>受管链节点（诊断与读方链校验）。</summary>
            internal UnitOfWorkChainNode? Node => _node;

            public IUnitOfWork UnitOfWork => _unitOfWork;

            public bool IsOwner => _isOwner;

            public bool IsTransactional => _isTransactional;

            public UnitOfWorkState State => _state;

            public UnitOfWorkTransactionOutcome TransactionOutcome => _transactionOutcome;

            public UnitOfWorkNotificationOutcome NotificationOutcome => _notificationOutcome;

            public bool IsReleased => _released;

            public Exception? RollbackTerminationFailure => _rollbackTerminationFailure;

            public CancellationToken ExecutionToken => _tokenSource.Token;

            internal bool FramePushed => _framePushed;

            public Task StartAsync(CancellationToken cancellationToken = default)
            {
                ThrowIfReleased();
                if (_started)
                {
                    throw new InvalidOperationException("The unit of work has already been started.");
                }

                if (_state != UnitOfWorkState.Active)
                {
                    throw new InvalidOperationException($"Cannot start a unit-of-work scope in state '{_state}'.");
                }

                _started = true;

                var deadline = ComputeDeadline();
                _deadlineUtc = deadline;
                var (executionCts, executionToken) = CreateExecutionToken(cancellationToken, deadline);
                _executionCts = executionCts;
                _tokenSource.Publish(executionToken);
                _tokenSource.MarkStarted();

                if (!_isOwner)
                {
                    return Task.CompletedTask;
                }

                if (!_isTransactional)
                {
                    // 非事务 owner：记录逻辑资源身份（无事务句柄），逻辑资源准入仍然生效。
                    PublishManagedResource(transactionHandle: null);
                    return Task.CompletedTask;
                }

                return BeginProviderTransactionAsync();
            }

            private async Task BeginProviderTransactionAsync()
            {
                var options = new UnitOfWorkBeginOptions
                {
                    IsolationLevel = _requestedIsolation,
                    Timeout = _deadlineUtc is { } deadline
                        ? (deadline - DateTimeOffset.UtcNow) switch
                        {
                            var remaining when remaining > TimeSpan.Zero => remaining,
                            _ => TimeSpan.Zero
                        }
                        : null
                };

                await _unitOfWork.BeginTransactionAsync(options, _tokenSource.Token).ConfigureAwait(false);

                // 事务开始成功后记录实际资源/事务关联：CAP 准入据此核对同一窗口、同一事务。
                PublishManagedResource((_unitOfWork as IUnitOfWorkManagedResource)?.ManagedTransactionHandle);
            }

            /// <summary>
            /// 记录 owner 窗口选定资源的逻辑键与实际资源关联（无 Provider 曝光时为 null，不参与比对）。
            /// </summary>
            private void PublishManagedResource(object? transactionHandle)
            {
                var managed = _unitOfWork as IUnitOfWorkManagedResource;
                _tokenSource.PublishManagedResource(
                    _isTransactional,
                    managed?.ManagedResourceKind,
                    managed?.ManagedResourceConnectionString,
                    managed?.ManagedResourceContext,
                    transactionHandle);
            }

            public async Task CompleteAsync(CancellationToken cancellationToken = default)
            {
                ThrowIfReleased();
                if (_state == UnitOfWorkState.Completed)
                {
                    return; // 重复成功完成 = 幂等 no-op
                }

                if (_state != UnitOfWorkState.Active)
                {
                    throw new InvalidOperationException($"Cannot complete a unit-of-work scope in state '{_state}'.");
                }

                if (!_started)
                {
                    throw new InvalidOperationException("The unit of work has not been started. Call StartAsync before completing.");
                }

                if (!_isOwner)
                {
                    // join：成功记录前执行同等的截止时间/取消检查（联动 token 生成不等于完成阶段已观察取消）。
                    ThrowIfExecutionInterrupted(cancellationToken);

                    // join：记录参与者成功；不 flush、不 commit、不释放外层。
                    TransitionTo(UnitOfWorkState.Completed);
                    return;
                }

                if (_rollbackOnly)
                {
                    var terminationFailure = await RollbackOwnerCoreAsync().ConfigureAwait(false);
                    var rollbackOnly = new UnitOfWorkRollbackOnlyException(
                        "The unit of work was marked rollback-only because an inner participant failed or exited " +
                        "without completing. The transaction was rolled back instead of committing a partial failure.");
                    if (terminationFailure is not null)
                    {
                        AttachCleanupDiagnostic(rollbackOnly, RollbackTerminationFailureDataKey, terminationFailure);
                    }
                    throw rollbackOnly;
                }

                ThrowIfExecutionInterrupted(cancellationToken);

                // 阶段 1：flush（提交前；rollback-only 校验已在此之前完成）。
                try
                {
                    await _unitOfWork.SaveChangesAsync(_tokenSource.Token).ConfigureAwait(false);
                }
                catch (Exception flushFailure)
                {
                    var terminationFailure = await TryTerminateUncommittedAsync().ConfigureAwait(false);
                    if (terminationFailure is not null)
                    {
                        AttachCleanupDiagnostic(flushFailure, RollbackTerminationFailureDataKey, terminationFailure);
                    }

                    // 统一分类：已确认回滚（含仅清理失败）保持 RolledBack，无法确认才 Unknown。
                    ClassifyTerminationResult(terminationFailure);
                    throw;
                }

                // 阶段 2：数据库 commit（确认后立即记录 Committed）。
                if (_isTransactional)
                {
                    try
                    {
                        await _unitOfWork.CommitTransactionAsync(_tokenSource.Token).ConfigureAwait(false);
                    }
                    catch (UnitOfWorkCommitCleanupFailureException)
                    {
                        // 提交已确认，句柄清理失败：固定 Committed，记录清理未完成，不尝试回滚。
                        _transactionOutcome = UnitOfWorkTransactionOutcome.Committed;
                        TransitionTo(UnitOfWorkState.Failed);
                        throw;
                    }
                    catch (Exception commitFailure)
                    {
                        // 提交派发期失败/响应丢失：结果未知；回滚尝试不得改写 Unknown。
                        _transactionOutcome = UnitOfWorkTransactionOutcome.Unknown;
                        var terminationFailure = await TryTerminateUncommittedAsync().ConfigureAwait(false);
                        if (terminationFailure is not null)
                        {
                            AttachCleanupDiagnostic(commitFailure, RollbackTerminationFailureDataKey, terminationFailure);
                        }
                        TransitionTo(UnitOfWorkState.Failed);
                        throw;
                    }
                }

                _transactionOutcome = UnitOfWorkTransactionOutcome.Committed;
                TransitionTo(UnitOfWorkState.Completed);

                // 阶段 3：提交后通知（失败不回滚、不谎称、不自动重试）。
                if (_unitOfWork is IUnitOfWorkCommittedNotifier notifier)
                {
                    try
                    {
                        await notifier.PublishCommittedNotificationsAsync(_tokenSource.Token).ConfigureAwait(false);
                        _notificationOutcome = UnitOfWorkNotificationOutcome.Succeeded;
                    }
                    catch (Exception notificationException)
                    {
                        _notificationOutcome = UnitOfWorkNotificationOutcome.Failed;
                        throw new UnitOfWorkPostCommitNotificationException(
                            "The unit of work committed successfully, but post-commit notifications failed. " +
                            "The committed fact is preserved: the database transaction was NOT rolled back and " +
                            "the business operation will not be retried automatically.",
                            notificationException);
                    }
                }
            }

            public async Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                ThrowIfReleased();

                if (!_isOwner)
                {
                    if (_state != UnitOfWorkState.Completed)
                    {
                        _ownerScope?.MarkRollbackOnly();
                    }
                    return;
                }

                if (_state == UnitOfWorkState.Completed)
                {
                    throw new InvalidOperationException(
                        "Cannot roll back after commit. The unit of work is already committed; the committed fact is preserved.");
                }

                if (_state is UnitOfWorkState.RolledBack or UnitOfWorkState.Failed)
                {
                    return; // 幂等
                }

                await RollbackOwnerCoreAsync().ConfigureAwait(false);
            }

            /// <summary>异常路径使用：不抛次级异常，保留原始业务异常；返回原始终结故障（无失败为 null）。</summary>
            internal async Task<Exception?> TryRollbackOnFailureAsync()
            {
                if (_released)
                {
                    return null;
                }

                try
                {
                    if (!_isOwner)
                    {
                        if (_state != UnitOfWorkState.Completed)
                        {
                            _ownerScope?.MarkRollbackOnly();
                        }
                        return null;
                    }

                    if (_state is UnitOfWorkState.Completed or UnitOfWorkState.RolledBack or UnitOfWorkState.Failed)
                    {
                        return null;
                    }

                    return await RollbackOwnerCoreAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    // 防御：次级失败不替换原始业务异常，但保留为可检查结果。
                    _rollbackTerminationFailure ??= exception;
                    return exception;
                }
            }

            /// <summary>
            /// owner 回滚：终结未提交工作并按统一分类迁移结果。返回原始终结故障（无法确认回滚时非 null）。
            /// 终结失败不得宣称 RolledBack：无法确认转 Unknown + Failed，回滚已确认但释放/丢弃失败保持
            /// RolledBack + Failed（详见 <see cref="ClassifyTerminationResult"/>）。
            /// </summary>
            private async Task<Exception?> RollbackOwnerCoreAsync()
            {
                if (!_started)
                {
                    // 未开始（无事务句柄）：无需回滚。
                    TransitionTo(UnitOfWorkState.RolledBack);
                    return null;
                }

                var terminationFailure = await TryTerminateUncommittedAsync().ConfigureAwait(false);
                ClassifyTerminationResult(terminationFailure);
                return terminationFailure;
            }

            /// <summary>
            /// 终结结果统一分类（所有退出路径共用；同一数据库事实不得因入口不同而变化）：
            /// 1) 成功回滚：保持/恢复 RolledBack（已有 Unknown 提交结果不得被改写）；
            /// 2) 回滚**已确认**但后续句柄释放/未 flush 丢弃失败（<see cref="UnitOfWorkAbandonCleanupFailureException"/>）：
            ///    事务事实保持 RolledBack，状态 Failed（清理未完成）；
            /// 3) 回滚**无法确认**（其它终结故障）：Unknown + Failed。
            /// 原始终结故障经 scope 的 <c>RollbackTerminationFailure</c> 保留；异常路径由调用方附加到主异常。
            /// </summary>
            private void ClassifyTerminationResult(Exception? terminationFailure)
            {
                if (terminationFailure is null)
                {
                    if (_transactionOutcome != UnitOfWorkTransactionOutcome.Unknown)
                    {
                        _transactionOutcome = UnitOfWorkTransactionOutcome.RolledBack;
                    }

                    TransitionTo(UnitOfWorkState.RolledBack);
                    return;
                }

                _rollbackTerminationFailure ??= terminationFailure;

                if (terminationFailure is UnitOfWorkAbandonCleanupFailureException)
                {
                    if (_transactionOutcome != UnitOfWorkTransactionOutcome.Unknown)
                    {
                        _transactionOutcome = UnitOfWorkTransactionOutcome.RolledBack;
                    }
                }
                else if (_isTransactional && _transactionOutcome != UnitOfWorkTransactionOutcome.Unknown)
                {
                    _transactionOutcome = UnitOfWorkTransactionOutcome.Unknown;
                }

                TransitionTo(UnitOfWorkState.Failed);
            }

            /// <summary>
            /// 未提交退出的统一终结：优先 <see cref="IUnitOfWorkAbandonable"/>（终结事务 + 丢弃未 flush 跟踪写入），
            /// 否则回滚事务。所有回滚路径（显式回滚/业务异常/flush 失败/提交失败清理）共用，
            /// 避免已回滚的跟踪写入被下一个 UoW 重新持久化。
            /// 返回终结失败（无法确认回滚或释放失败）的原始异常；成功为 null —— Provider 不得吞掉终结故障。
            /// </summary>
            private async Task<Exception?> TryTerminateUncommittedAsync()
            {
                try
                {
                    if (_unitOfWork is IUnitOfWorkAbandonable abandonable)
                    {
                        abandonable.AbandonPendingWork();
                        return null;
                    }

                    await _unitOfWork.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                    return null;
                }
                catch (Exception exception)
                {
                    // 终结失败结果交回调用方（状态/结果迁移 + 故障保留），不静默吞掉。
                    return exception;
                }
            }

            private void MarkRollbackOnly()
            {
                if (_state == UnitOfWorkState.Active)
                {
                    _rollbackOnly = true;
                }
            }

            private void ThrowIfExecutionInterrupted(CancellationToken cancellationToken)
            {
                if (_deadlineUtc is { } deadline && DateTimeOffset.UtcNow >= deadline)
                {
                    throw new OperationCanceledException(
                        "The unit of work deadline expired before completion; the transaction must not be reported as success.");
                }

                if (_tokenSource.Token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(_tokenSource.Token);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            private DateTimeOffset? ComputeDeadline()
            {
                DateTimeOffset? own = _requestedTimeout is { } timeout ? DateTimeOffset.UtcNow + timeout : null;

                if (_isOwner)
                {
                    return own;
                }

                // join：继承外层剩余期限，只可收紧。
                return _ownerScope?._deadlineUtc is { } ownerDeadline
                    ? (own is { } value && value > ownerDeadline ? ownerDeadline : own ?? ownerDeadline)
                    : own;
            }

            private (CancellationTokenSource? Cts, CancellationToken Token) CreateExecutionToken(
                CancellationToken callerToken,
                DateTimeOffset? deadline)
            {
                var sources = new System.Collections.Generic.List<CancellationToken>(2);
                if (callerToken.CanBeCanceled)
                {
                    sources.Add(callerToken);
                }

                if (!_isOwner && _ownerScope is { } owner && owner._tokenSource.Token.CanBeCanceled)
                {
                    sources.Add(owner._tokenSource.Token);
                }

                if (sources.Count == 0)
                {
                    if (deadline is null)
                    {
                        return (null, callerToken);
                    }

                    var standalone = new CancellationTokenSource();
                    standalone.CancelAfter(deadline.Value - DateTimeOffset.UtcNow);
                    return (standalone, standalone.Token);
                }

                if (sources.Count == 1 && deadline is null)
                {
                    // 唯一来源直接复用：无论是 caller token 还是 owner token（join 继承）都必须生效。
                    return (null, sources[0]);
                }

                var cts = sources.Count switch
                {
                    1 => CancellationTokenSource.CreateLinkedTokenSource(sources[0]),
                    _ => CancellationTokenSource.CreateLinkedTokenSource(sources[0], sources[1])
                };

                if (deadline is { } deadlineValue)
                {
                    var remaining = deadlineValue - DateTimeOffset.UtcNow;
                    cts.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                }

                return (cts, cts.Token);
            }

            private void ThrowIfReleased()
            {
                if (_released)
                {
                    throw new ObjectDisposedException(
                        nameof(IUnitOfWorkScope),
                        "The unit-of-work scope has been released. Results (State/TransactionOutcome/NotificationOutcome) remain readable.");
                }
            }

            public void Dispose()
            {
                DisposeCore();
            }

            public ValueTask DisposeAsync()
            {
                DisposeCore();
                return default;
            }

            private void TransitionTo(UnitOfWorkState state)
            {
                _state = state;
                _tokenSource.TransitionTo(state);
            }

            private void DisposeCore()
            {
                if (_released)
                {
                    return;
                }

                var failures = new System.Collections.Generic.List<UnitOfWorkCleanupFailure>();

                // owner 未完成退出：回滚 + 丢弃 + 释放（不等请求 DI scope 结束）。
                if (_isOwner && _state == UnitOfWorkState.Active)
                {
                    TryAbandonOwner(failures);
                }
                else if (!_isOwner && _state == UnitOfWorkState.Active)
                {
                    // 未成功完成的 join 退出：标记实际 owner rollback-only；
                    // 成功完成的 join 仅恢复环境（不污染外层）。
                    _ownerScope?.MarkRollbackOnly();
                }

                try
                {
                    _manager.RestoreCallerEnvironment(this);
                }
                catch (Exception exception)
                {
                    failures.Add(new UnitOfWorkCleanupFailure("restore", exception));
                }

                try
                {
                    _executionCts?.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(new UnitOfWorkCleanupFailure("token-dispose", exception));
                }

                if (_ownsUnitOfWorkDirectly)
                {
                    try
                    {
                        _unitOfWork.Dispose();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(new UnitOfWorkCleanupFailure("unit-of-work-dispose", exception));
                    }
                }

                try
                {
                    _ownedScope?.Dispose();
                }
                catch (Exception exception)
                {
                    failures.Add(new UnitOfWorkCleanupFailure("owned-scope-dispose", exception));
                }

                _released = true;
                _tokenSource.MarkReleased();

                if (failures.Count > 0)
                {
                    throw new UnitOfWorkCleanupException(failures);
                }
            }

            private void TryAbandonOwner(System.Collections.Generic.List<UnitOfWorkCleanupFailure> failures)
            {
                if (_unitOfWork is IUnitOfWorkAbandonable abandonable)
                {
                    try
                    {
                        // DI 路径：容器负责对象释放，内核终结未完成事务并丢弃未 flush 跟踪写入。
                        abandonable.AbandonPendingWork();
                    }
                    catch (Exception exception)
                    {
                        failures.Add(new UnitOfWorkCleanupFailure("abandon", exception));

                        // 与其它退出路径共用同一分类：已确认回滚（含仅清理失败）保持 RolledBack，
                        // 无法确认才 Unknown；原始终结故障同时保留在 scope 诊断上。
                        ClassifyTerminationResult(exception);
                        return;
                    }
                }

                // 无终结能力的 Provider：依赖其容器释放语义（见 Provider 矩阵）。
                _transactionOutcome = _isTransactional
                    ? UnitOfWorkTransactionOutcome.RolledBack
                    : UnitOfWorkTransactionOutcome.NotStarted;
                TransitionTo(UnitOfWorkState.RolledBack);
            }

            internal void RestoreTokenCarrier()
            {
                _tokenRestorer?.Dispose();
                _tokenRestorer = null;
            }

            internal void RestoreAmbient()
            {
                _ambientToken?.Dispose();
            }
        }
    }
}
