using System;
using System.Threading;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 当前受管执行 token 的读入口、数据准入守卫与受管事务准入权威
    /// （链校验：只跟随自身或其后代的帧；准入作用于最近的可见 owner 窗口）。
    /// </summary>
    public static class UnitOfWorkExecutionContext
    {
        private static readonly AsyncLocal<SourceFrame?> CurrentSource = new();

        /// <summary>当前受管执行 token；无受管 scope 时为 default。</summary>
        public static CancellationToken CurrentExecutionToken =>
            CurrentSource.Value?.Source.Token ?? default;

        /// <summary>
        /// 把传入 CT 与当前受管执行 token 组合为有效 token（读方未接入链时沿用顶层帧）。
        /// 返回的非 null Disposable 表示创建了联动 CTS，使用方必须释放。
        /// </summary>
        public static IDisposable? CombineWithCurrent(CancellationToken incoming, out CancellationToken effective)
            => CombineWithCurrent(incoming, readerNode: null, out effective);

        /// <summary>
        /// 把传入 CT 与「读方可见」的受管执行 token 组合为有效 token。
        /// </summary>
        /// <param name="incoming">调用方传入的 CT。</param>
        /// <param name="readerNode">读方自身的受管链节点；null 表示未接入链校验。</param>
        /// <param name="effective">组合后的有效 token。</param>
        public static IDisposable? CombineWithCurrent(
            CancellationToken incoming,
            UnitOfWorkChainNode? readerNode,
            out CancellationToken effective)
        {
            var frame = ResolveVisibleFrame(readerNode);
            var current = frame?.Source.Token ?? default;

            if (!current.CanBeCanceled)
            {
                effective = incoming;
                return null;
            }

            if (!incoming.CanBeCanceled || incoming == current)
            {
                effective = incoming.CanBeCanceled ? incoming : current;
                return null;
            }

            var linked = CancellationTokenSource.CreateLinkedTokenSource(incoming, current);
            effective = linked.Token;

            // The derived token is the actual token an EF operation cancels with; associate it with
            // the frame that produced it so cancellation classification can follow the source.
            frame!.Source.RecordDerivedToken(effective);
            return linked;
        }

        /// <summary>
        /// 数据准入守卫（正式读方在返回资源前调用；作用于最近的可见 owner 窗口）：
        /// 1) 租户身份：顶层可见受管帧与读方均具备租户来源时按值（含 null=host）精确比较，不一致确定性拒绝；
        /// 2) 生命周期：顶层可见 owner 帧处于终态/已释放时拒绝新数据动作（结果仍可读，需新建 UoW）；
        /// 3) 逻辑资源身份：owner 窗口开始后记录的声明种类/连接配置与读方自身不一致时确定性拒绝
        ///    （第二 Context 声明/连接配置是不同逻辑资源，根窗口与隔离窗口规则一致）。
        /// </summary>
        /// <param name="readerNode">读方所在 scope 的受管链节点；null 表示读方未接入链校验。</param>
        /// <param name="readerTenant">读方当前租户快照。</param>
        /// <param name="readerLogicalContextType">读方自身 context 的声明类型全名；null 表示不参与。</param>
        /// <param name="readerConnectionString">读方自身连接配置；null 表示不参与（非关系型）。</param>
        public static void ValidateDataAccess(
            UnitOfWorkChainNode? readerNode,
            TenantKeySnapshot readerTenant,
            string? readerLogicalContextType = null,
            string? readerConnectionString = null)
        {
            var frame = ResolveVisibleOwnerFrame(readerNode);
            if (frame is null)
            {
                return; // 无受管窗口：无 UoW 的既有行为（含自动保存）不受影响
            }

            var source = frame.Source;

            if (frame.Tenant.HasTenantSource &&
                readerTenant.HasTenantSource &&
                !frame.Tenant.Matches(readerTenant))
            {
                throw new InvalidOperationException(
                    $"The ambient unit-of-work resource belongs to tenant '{frame.Tenant.Value ?? "<host>"}' but the current " +
                    $"tenant is '{readerTenant.Value ?? "<host>"}'. Switching tenant (including switching to host) inside an " +
                    "active unit of work is not supported; start a new unit of work for the other tenant.");
            }

            if (source.State != UnitOfWorkState.Active || source.IsReleased)
            {
                throw new InvalidOperationException(
                    $"The unit of work is '{source.State}' (released: {source.IsReleased}). Scope results remain " +
                    "readable, but new data operations require a new unit of work.");
            }

            if (!string.IsNullOrEmpty(source.ManagedResourceKind) &&
                !string.IsNullOrEmpty(readerLogicalContextType) &&
                !string.Equals(source.ManagedResourceKind, readerLogicalContextType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The managed unit-of-work resource uses context declaration '{source.ManagedResourceKind}', " +
                    $"but this reader declares '{readerLogicalContextType}'. A second context declaration is a " +
                    "different logical resource; start a new unit of work for it instead of sharing the active one.");
            }

            if (!string.IsNullOrEmpty(source.ManagedResourceConnectionString) &&
                !string.IsNullOrEmpty(readerConnectionString) &&
                !string.Equals(source.ManagedResourceConnectionString, readerConnectionString, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The managed unit-of-work resource uses a different connection configuration than this reader. " +
                    "A second connection is a different logical resource; start a new unit of work for it.");
            }
        }

        /// <summary>
        /// 参与判定（公共权威，三值）：只跟随读方可见链上**最近的 owner 窗口**，
        /// 不满足即拒绝且不回退到更外层窗口（有载体 ≠ 本事务已受管）。
        /// 结果只报告决策/原因，**不暴露句柄**；原生资源经 Provider 借用边界获取。
        /// </summary>
        /// <param name="readerNode">读方所在 scope 的受管链节点；null 表示不参与链校验。</param>
        /// <param name="readerTenant">读方当前租户快照；两端均有租户来源时按值（含 null=host）精确比较。</param>
        public static UnitOfWorkParticipationResult ResolveParticipation(
            UnitOfWorkChainNode? readerNode,
            TenantKeySnapshot readerTenant = default)
        {
            var frame = ResolveVisibleOwnerFrame(readerNode);
            if (frame is null)
            {
                return UnitOfWorkParticipationResult.NoVisibleWindow();
            }

            var refusal = EvaluateParticipation(frame, readerTenant);
            return refusal is null
                ? UnitOfWorkParticipationResult.Granted()
                : UnitOfWorkParticipationResult.Refused(refusal.Value);
        }

        /// <summary>
        /// 受管事务准入（CAP lease 签发与原生借用共用的**唯一权威**）：
        /// 读方可见链上**最近的 owner 窗口**必须已开始、为事务型、Active、未释放且持有关联事务句柄；
        /// 不满足即确定性拒绝，不回退到更外层窗口（有载体 ≠ 本事务已受管）。
        /// </summary>
        /// <param name="readerNode">读方所在 scope 的受管链节点；null 表示不参与链校验。</param>
        /// <param name="transactionHandle">owner 开始事务时记录的实际事务句柄（仅用于身份比对）。</param>
        /// <param name="resourceContext">owner 开始事务时记录的原生资源上下文（仅用于身份比对）。</param>
        /// <param name="ownerSource">准入通过的 owner 载体（只读生命周期视图）。
        /// lease 等 live view 应绑定**签发时**的该载体判定有效性，不得重新解析「当前最近的 owner」。</param>
        public static bool TryGetCurrentManagedTransaction(
            UnitOfWorkChainNode? readerNode,
            out object? transactionHandle,
            out object? resourceContext,
            out UnitOfWorkExecutionTokenSource? ownerSource)
        {
            var frame = ResolveVisibleOwnerFrame(readerNode);
            if (frame is not null && EvaluateParticipation(frame, TenantKeySnapshot.NotParticipating) is null)
            {
                transactionHandle = frame.Source.ManagedTransactionHandle;
                resourceContext = frame.Source.ManagedResourceContext;
                ownerSource = frame.Source;
                return true;
            }

            transactionHandle = null;
            resourceContext = null;
            ownerSource = null;
            return false;
        }

        /// <summary>
        /// 统一判定：返回 null 表示获准；否则给出确定性拒绝原因
        /// （与 <see cref="ResolveParticipation"/> 共用同一判定，保证两处权威一致）。
        /// </summary>
        private static UnitOfWorkParticipationRefusalReason? EvaluateParticipation(
            SourceFrame frame,
            TenantKeySnapshot readerTenant)
        {
            if (frame.Tenant.HasTenantSource &&
                readerTenant.HasTenantSource &&
                !frame.Tenant.Matches(readerTenant))
            {
                return UnitOfWorkParticipationRefusalReason.ResourceMismatch;
            }

            var source = frame.Source;
            if (source.IsReleased)
            {
                return UnitOfWorkParticipationRefusalReason.Released;
            }

            if (source.State != UnitOfWorkState.Active)
            {
                return UnitOfWorkParticipationRefusalReason.Terminal;
            }

            if (!source.HasStarted)
            {
                return UnitOfWorkParticipationRefusalReason.NotStarted;
            }

            if (!source.IsTransactionalOwner)
            {
                return UnitOfWorkParticipationRefusalReason.NonTransactional;
            }

            if (source.ManagedTransactionHandle is null)
            {
                return UnitOfWorkParticipationRefusalReason.NoTransactionAssociation;
            }

            return null;
        }

        private static SourceFrame? ResolveVisibleOwnerFrame(UnitOfWorkChainNode? readerNode)
        {
            for (var frame = CurrentSource.Value; frame is not null; frame = frame.Parent)
            {
                if (IsVisible(frame, readerNode) && frame.Source.IsOwner)
                {
                    return frame;
                }
            }

            return null;
        }

        private static SourceFrame? ResolveVisibleFrame(UnitOfWorkChainNode? readerNode)
        {
            for (var frame = CurrentSource.Value; frame is not null; frame = frame.Parent)
            {
                if (IsVisible(frame, readerNode))
                {
                    return frame;
                }
            }

            return null;
        }

        private static bool IsVisible(SourceFrame frame, UnitOfWorkChainNode? readerNode)
            => readerNode is null ||
               frame.OwnerNode is null ||
               frame.OwnerNode.IsSelfOrDescendantOf(readerNode);

        internal static IDisposable Push(
            UnitOfWorkExecutionTokenSource source,
            UnitOfWorkChainNode? ownerNode,
            TenantKeySnapshot tenant)
        {
            var frame = new SourceFrame(source, ownerNode, tenant, CurrentSource.Value);
            CurrentSource.Value = frame;
            return new FrameRestorer(frame);
        }

        private sealed class SourceFrame
        {
            public SourceFrame(
                UnitOfWorkExecutionTokenSource source,
                UnitOfWorkChainNode? ownerNode,
                TenantKeySnapshot tenant,
                SourceFrame? parent)
            {
                Source = source;
                OwnerNode = ownerNode;
                Tenant = tenant;
                Parent = parent;
            }

            public UnitOfWorkExecutionTokenSource Source { get; }

            public UnitOfWorkChainNode? OwnerNode { get; }

            public TenantKeySnapshot Tenant { get; }

            public SourceFrame? Parent { get; }
        }

        private sealed class FrameRestorer : IDisposable
        {
            private readonly SourceFrame _frame;
            private bool _disposed;

            public FrameRestorer(SourceFrame frame)
            {
                _frame = frame;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (ReferenceEquals(CurrentSource.Value, _frame))
                {
                    CurrentSource.Value = _frame.Parent;
                }
            }
        }
    }
}
