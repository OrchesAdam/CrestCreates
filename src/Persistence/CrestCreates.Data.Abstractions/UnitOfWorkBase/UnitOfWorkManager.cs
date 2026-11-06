using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Core.Abstractions.Threading;
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
        private readonly AsyncLocal<UnitOfWorkScope?> _currentExecutionScope = new();

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

        internal IUnitOfWorkFactory Factory => _factory;

        internal UnitOfWorkProviderBindingRegistry? Bindings => _bindings;

        internal IServiceScopeFactory? ScopeFactory => _scopeFactory;

        internal OrmProvider? ExplicitDefault => _explicitDefault;

        internal UnitOfWorkChainNode? ChainNode => _chainNode;

        internal IServiceProvider? ServiceProvider => _serviceProvider;

        internal bool OwnsResolvedUnitOfWorks => _ownsResolvedUnitOfWorks;

        internal AsyncLocal<AmbientFrame?> CurrentFrame => _currentFrame;

        internal AsyncLocal<UnitOfWorkScope?> CurrentExecutionScope => _currentExecutionScope;

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
            UnitOfWorkScopePreflight.ValidateOptions(options);

            var frame = _currentFrame.Value;
            var executionParent = _currentExecutionScope.Value;
            UnitOfWorkScope scope;
            if (frame is not null && options.Propagation == UnitOfWorkPropagation.Required)
            {
                scope = UnitOfWorkScope.CreateJoined(this, frame.Scope, executionParent, options);
            }
            else if (frame is not null)
            {
                scope = UnitOfWorkScopeBootstrapper.BeginIsolated(this, frame, executionParent, options);
            }
            else
            {
                scope = UnitOfWorkScopeBootstrapper.BeginRoot(this, frame, executionParent, options);
            }

            // Joins do not push an ambient owner frame, but they are real execution parents for
            // cancellation classification; record the innermost scope synchronously in this frame.
            _currentExecutionScope.Value = scope;
            return scope;
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
                AttachCancellationSnapshot(exception, scope);
                var rollbackTerminationFailure = await scope.TryRollbackOnFailureAsync().ConfigureAwait(false);
                if (rollbackTerminationFailure is not null)
                {
                    UnitOfWorkDiagnostics.Attach(primary, RollbackTerminationFailureDataKey, rollbackTerminationFailure);
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
                    UnitOfWorkDiagnostics.Attach(primary, CleanupExceptionDataKey, cleanupException);
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

        /// <summary>
        /// Preserves the caller/execution identities on the original cancellation exception before
        /// the ambient frame unwinds; transports classify the source from this typed snapshot.
        /// </summary>
        private static void AttachCancellationSnapshot(Exception exception, UnitOfWorkScope scope)
            => UnitOfWorkCancellationSnapshot.Attach(exception, scope.CaptureCancellationSnapshot());

        /// <summary>
        /// Restores the execution-parent pointer used for cancellation classification. The caller
        /// frame already holds the parent value; this only keeps the disposing frame consistent.
        /// </summary>
        internal void RestoreExecutionParent(UnitOfWorkScope scope)
        {
            if (ReferenceEquals(_currentExecutionScope.Value, scope))
            {
                _currentExecutionScope.Value = scope.ExecutionParent;
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
    }
}
