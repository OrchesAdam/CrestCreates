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

            await using var scope = BeginScope(options);
            try
            {
                await scope.StartAsync(cancellationToken).ConfigureAwait(false);
                var result = await action(scope.ExecutionToken).ConfigureAwait(false);
                await scope.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
                return result;
            }
            catch
            {
                await ((UnitOfWorkScope)scope).TryRollbackOnFailureAsync().ConfigureAwait(false);
                throw;
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

        private UnitOfWorkScope BeginRootScope(AmbientFrame? frame, UnitOfWorkOptions options)
        {
            var provider = ResolveProvider(options.Provider);
            ValidateIsolationCapability(provider, options);
            var unitOfWork = _factory.Create(provider);
            var node = new UnitOfWorkChainNode();
            node.AttachTo(frame?.Scope.Node ?? _chainNode);
            return UnitOfWorkScope.CreateOwner(
                this,
                unitOfWork,
                provider,
                options,
                node,
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
                    parentFrame: parentFrame,
                    ownedScope: null,
                    ambientToken: null,
                    ownsUnitOfWorkDirectly: _ownsResolvedUnitOfWorks);
            }

            var childScope = _scopeFactory.CreateScope();
            IDisposable? ambientToken = null;
            try
            {
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
                    var tenantKey = binding.TenantKeyFactory?.Invoke(childScope.ServiceProvider);
                    ambientToken = UnitOfWorkAmbientContext.Push(ambientContext, childNode, tenantKey);
                }

                var childFactory = childScope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
                var unitOfWork = childFactory.Create(provider);

                return UnitOfWorkScope.CreateOwner(
                    this,
                    unitOfWork,
                    provider,
                    options,
                    childScope.ServiceProvider.GetRequiredService<UnitOfWorkChainNode>(),
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
                _isOwner = isOwner;
                _isTransactional = isTransactional;
                _requestedIsolation = options.IsolationLevel;
                _requestedTimeout = options.Timeout;
                _parentFrame = parentFrame;
                _ownerScope = ownerScope;
                _ownedScope = ownedScope;
                _ambientToken = ambientToken;
                _ownsUnitOfWorkDirectly = ownsUnitOfWorkDirectly;
                _tokenRestorer = UnitOfWorkExecutionContext.Push(_tokenSource, node);
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
                _executionCts = CreateExecutionTokenSource(cancellationToken, deadline);
                _tokenSource.Publish(_executionCts?.Token ?? cancellationToken);

                if (!_isOwner || !_isTransactional)
                {
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
                    // join：记录参与者成功；不 flush、不 commit、不释放外层。
                    _state = UnitOfWorkState.Completed;
                    return;
                }

                if (_rollbackOnly)
                {
                    await RollbackOwnerCoreAsync().ConfigureAwait(false);
                    throw new UnitOfWorkRollbackOnlyException(
                        "The unit of work was marked rollback-only because an inner participant failed or exited " +
                        "without completing. The transaction was rolled back instead of committing a partial failure.");
                }

                ThrowIfExecutionInterrupted(cancellationToken);

                // 阶段 1：flush（提交前；rollback-only 校验已在此之前完成）。
                try
                {
                    await _unitOfWork.SaveChangesAsync(_tokenSource.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    var aborted = await TryAbortOwnerAsync().ConfigureAwait(false);
                    _state = aborted ? UnitOfWorkState.RolledBack : UnitOfWorkState.Failed;
                    if (aborted)
                    {
                        _transactionOutcome = UnitOfWorkTransactionOutcome.RolledBack;
                    }
                    throw;
                }

                // 阶段 2：数据库 commit（确认后立即记录 Committed）。
                if (_isTransactional)
                {
                    try
                    {
                        await _unitOfWork.CommitTransactionAsync(_tokenSource.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // 提交派发期失败/响应丢失：结果未知；回滚尝试不得改写 Unknown。
                        _transactionOutcome = UnitOfWorkTransactionOutcome.Unknown;
                        await TryAbortOwnerAsync().ConfigureAwait(false);
                        _state = UnitOfWorkState.Failed;
                        throw;
                    }
                }

                _transactionOutcome = UnitOfWorkTransactionOutcome.Committed;
                _state = UnitOfWorkState.Completed;

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

            /// <summary>异常路径使用：不抛次级异常，保留原始业务异常。</summary>
            internal async Task TryRollbackOnFailureAsync()
            {
                if (_released)
                {
                    return;
                }

                try
                {
                    if (!_isOwner)
                    {
                        if (_state != UnitOfWorkState.Completed)
                        {
                            _ownerScope?.MarkRollbackOnly();
                        }
                        return;
                    }

                    if (_state is UnitOfWorkState.Completed or UnitOfWorkState.RolledBack or UnitOfWorkState.Failed)
                    {
                        return;
                    }

                    await RollbackOwnerCoreAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 清理失败不替换原始业务异常。
                }
            }

            private async Task RollbackOwnerCoreAsync()
            {
                if (!_started)
                {
                    // 未开始（无事务句柄）：无需回滚。
                    _state = UnitOfWorkState.RolledBack;
                    return;
                }

                var aborted = await TryRollbackAsync().ConfigureAwait(false);
                if (_transactionOutcome != UnitOfWorkTransactionOutcome.Unknown)
                {
                    _transactionOutcome = UnitOfWorkTransactionOutcome.RolledBack;
                }
                _state = aborted ? UnitOfWorkState.RolledBack : UnitOfWorkState.Failed;
            }

            private async Task<bool> TryRollbackAsync()
            {
                try
                {
                    await _unitOfWork.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                    return true;
                }
                catch (Exception)
                {
                    // 清理失败仅作结果记录（可检查：状态 Failed）。
                    return false;
                }
            }

            private async Task<bool> TryAbortOwnerAsync()
            {
                try
                {
                    await _unitOfWork.RollbackTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                    return true;
                }
                catch (Exception)
                {
                    return false;
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

            private CancellationTokenSource? CreateExecutionTokenSource(CancellationToken callerToken, DateTimeOffset? deadline)
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

                if (sources.Count == 1 && deadline is null)
                {
                    return null; // 直接使用调用方 token
                }

                var cts = sources.Count switch
                {
                    0 => new CancellationTokenSource(),
                    1 => CancellationTokenSource.CreateLinkedTokenSource(sources[0]),
                    _ => CancellationTokenSource.CreateLinkedTokenSource(sources[0], sources[1])
                };

                if (deadline is { } deadlineValue)
                {
                    var remaining = deadlineValue - DateTimeOffset.UtcNow;
                    cts.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                }

                return cts;
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

            private void DisposeCore()
            {
                if (_released)
                {
                    return;
                }

                // owner 未完成退出：回滚 + 释放（不等请求 DI scope 结束）。
                if (_isOwner && _state == UnitOfWorkState.Active)
                {
                    TryAbandonOwner();
                }

                Exception? restoreFailure = null;
                try
                {
                    _manager.RestoreCallerEnvironment(this);
                }
                catch (Exception ex)
                {
                    restoreFailure = ex;
                }

                _executionCts?.Dispose();

                if (_ownsUnitOfWorkDirectly)
                {
                    _unitOfWork.Dispose();
                }

                _ownedScope?.Dispose();
                _released = true;

                if (restoreFailure is not null)
                {
                    throw restoreFailure;
                }
            }

            private void TryAbandonOwner()
            {
                try
                {
                    if (_unitOfWork is IUnitOfWorkAbandonable abandonable)
                    {
                        // DI 路径：容器负责对象释放，内核终结未完成事务并丢弃未 flush 跟踪写入。
                        abandonable.AbandonPendingWork();
                    }

                    // 无终结能力的 Provider：依赖其容器释放语义（见 Provider 矩阵）。
                    _transactionOutcome = _isTransactional
                        ? UnitOfWorkTransactionOutcome.RolledBack
                        : UnitOfWorkTransactionOutcome.NotStarted;
                    _state = UnitOfWorkState.RolledBack;
                }
                catch (Exception)
                {
                    _state = UnitOfWorkState.Failed;
                }
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
