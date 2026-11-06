using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Core.Abstractions.Threading;
using CrestCreates.Domain.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 状态化 scope：承载参与者完成、事务结果、通知结果与释放标志（设计 §3.2）。
    /// 创建/启动留在调用方帧（同步激活）；完成/回滚/终结由 <see cref="UnitOfWorkScopeSettlement"/>，
    /// 提交后通知由 <see cref="UnitOfWorkScopeNotification"/>，释放由 <see cref="UnitOfWorkScopeRelease"/> 实现。
    /// </summary>
    internal sealed class UnitOfWorkScope : IUnitOfWorkScope, IUnitOfWorkCancellationSource
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
        private readonly UnitOfWorkScope? _executionParent;
        private readonly UnitOfWorkScope? _ownerScope;
        private readonly IServiceScope? _ownedScope;
        private readonly IDisposable? _ambientToken;
        private readonly bool _ownsUnitOfWorkDirectly;
        private readonly UnitOfWorkExecutionTokenSource _tokenSource = new();
        private CancellationToken _callerToken;
        private IDisposable? _tokenRestorer;
        private bool _framePushed;
        private bool _started;
        private bool _released;
        private bool _rollbackOnly;
        private DateTimeOffset? _deadlineUtc;
        private CancellationTokenSource? _executionCts;
        private UnitOfWorkState _state = UnitOfWorkState.Active;

        private UnitOfWorkScope(
            UnitOfWorkManager manager,
            IUnitOfWork unitOfWork,
            OrmProvider provider,
            UnitOfWorkChainNode? node,
            TenantKeySnapshot tenantSnapshot,
            UnitOfWorkOptions options,
            bool isOwner,
            bool isTransactional,
            UnitOfWorkManager.AmbientFrame? parentFrame,
            UnitOfWorkScope? executionParent,
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
            _executionParent = executionParent;
            _ownerScope = ownerScope;
            _ownedScope = ownedScope;
            _ambientToken = ambientToken;
            _ownsUnitOfWorkDirectly = ownsUnitOfWorkDirectly;
            _tokenSource.IsOwner = isOwner;
            _tokenRestorer = UnitOfWorkExecutionContext.Push(_tokenSource, node, tenantSnapshot);
            if (isOwner)
            {
                manager.CurrentFrame.Value = new UnitOfWorkManager.AmbientFrame(this, parentFrame);
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
            UnitOfWorkManager.AmbientFrame? parentFrame,
            UnitOfWorkScope? executionParent,
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
                executionParent,
                ownerScope: null,
                ownedScope,
                ambientToken,
                ownsUnitOfWorkDirectly);
        }

        /// <summary>join scope：复用外层 owner；调用方帧内完成校验。</summary>
        internal static UnitOfWorkScope CreateJoined(
            UnitOfWorkManager manager,
            UnitOfWorkScope owner,
            UnitOfWorkScope? executionParent,
            UnitOfWorkOptions options)
        {
            UnitOfWorkScopePreflight.ValidateJoin(owner, options);

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
                executionParent,
                ownerScope: owner,
                ownedScope: null,
                ambientToken: null,
                ownsUnitOfWorkDirectly: false);
        }

        /// <summary>受管链节点（诊断与读方链校验）。</summary>
        internal UnitOfWorkChainNode? Node => _node;

        internal UnitOfWorkScope? ExecutionParent => _executionParent;

        internal UnitOfWorkScope? OwnerScope => _ownerScope;

        internal UnitOfWorkManager Manager => _manager;

        internal OrmProvider Provider => _provider;

        internal IsolationLevel? RequestedIsolation => _requestedIsolation;

        internal DateTimeOffset? DeadlineUtc => _deadlineUtc;

        internal bool HasStarted => _started;

        internal bool RollbackOnly => _rollbackOnly;

        internal CancellationTokenSource? ExecutionCts => _executionCts;

        internal IServiceScope? OwnedScope => _ownedScope;

        internal bool OwnsUnitOfWorkDirectly => _ownsUnitOfWorkDirectly;

        public IUnitOfWork UnitOfWork => _unitOfWork;

        public bool IsOwner => _isOwner;

        public bool IsTransactional => _isTransactional;

        public UnitOfWorkState State => _state;

        public UnitOfWorkTransactionOutcome TransactionOutcome { get; internal set; } = UnitOfWorkTransactionOutcome.NotStarted;

        public UnitOfWorkNotificationOutcome NotificationOutcome { get; internal set; } = UnitOfWorkNotificationOutcome.None;

        public bool IsReleased => _released;

        public Exception? RollbackTerminationFailure { get; internal set; }

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
            _callerToken = cancellationToken;

            var deadline = UnitOfWorkExecutionTokenPolicy.ComputeDeadline(
                _requestedTimeout,
                _isOwner,
                _ownerScope?.DeadlineUtc);
            _deadlineUtc = deadline;
            var (executionCts, executionToken) = UnitOfWorkExecutionTokenPolicy.CreateExecutionToken(
                cancellationToken,
                _ownerScope?.ExecutionToken ?? default,
                _isOwner,
                deadline);
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

        public UnitOfWorkCancellationSnapshot? CaptureCancellationSnapshot()
        {
            if (!_started)
            {
                return null;
            }

            // Walk the ownership chain (join -> owner, isolated -> parent frame) so classification
            // can return to the root transport caller and the real deadline source instead of
            // mistaking an outer execution token for the caller.
            var scopes = new System.Collections.Generic.List<UnitOfWorkScope>();
            for (var scope = this; scope is not null; scope = scope._executionParent)
            {
                if (scope._started)
                {
                    scopes.Add(scope);
                }
            }

            if (scopes.Count == 0)
            {
                return null;
            }

            var frames = new System.Collections.Generic.List<UnitOfWorkCancellationFrame>(scopes.Count);
            for (var index = 0; index < scopes.Count; index++)
            {
                var scope = scopes[index];
                var callerCancelled = scope._callerToken.CanBeCanceled
                    && scope._callerToken.IsCancellationRequested;
                var deadlineExpired = scope._deadlineUtc is { } deadline
                    && (DateTimeOffset.UtcNow >= deadline
                        || (!callerCancelled && scope._tokenSource.Token.IsCancellationRequested));
                frames.Add(new UnitOfWorkCancellationFrame(
                    scope._callerToken,
                    scope._tokenSource.Token,
                    callerCancelled,
                    deadlineExpired,
                    scope._tokenSource.SnapshotDerivedTokens(),
                    DepthFromRoot: scopes.Count - 1 - index));
            }

            return new UnitOfWorkCancellationSnapshot(frames);
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

        public Task CompleteAsync(CancellationToken cancellationToken = default)
            => UnitOfWorkScopeSettlement.CompleteAsync(this, cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
            => UnitOfWorkScopeSettlement.RollbackAsync(this, cancellationToken);

        internal Task<Exception?> TryRollbackOnFailureAsync()
            => UnitOfWorkScopeSettlement.TryRollbackOnFailureAsync(this);

        internal void MarkRollbackOnly()
        {
            if (_state == UnitOfWorkState.Active)
            {
                _rollbackOnly = true;
            }
        }

        internal void ThrowIfReleased()
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
            UnitOfWorkScopeRelease.Dispose(this);
        }

        public ValueTask DisposeAsync()
        {
            UnitOfWorkScopeRelease.Dispose(this);
            return default;
        }

        internal void TransitionTo(UnitOfWorkState state)
        {
            _state = state;
            _tokenSource.TransitionTo(state);
        }

        internal void MarkReleased()
        {
            _released = true;
            _tokenSource.MarkReleased();
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
