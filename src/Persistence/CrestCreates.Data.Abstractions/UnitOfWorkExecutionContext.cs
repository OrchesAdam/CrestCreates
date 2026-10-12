using System;
using System.Threading;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 当前受管执行 token 的读入口与数据准入守卫（链校验：只跟随自身或其后代的帧）。
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
            var current = ResolveVisibleFrame(readerNode)?.Source.Token ?? default;

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
            return linked;
        }

        /// <summary>
        /// 数据准入守卫（正式读方在返回资源前调用）：
        /// 1) 租户身份：顶层可见受管帧与读方均具备租户来源时按值（含 null=host）精确比较，不一致确定性拒绝；
        /// 2) 生命周期：顶层可见 owner 帧处于终态/已释放时拒绝新数据动作（结果仍可读，需新建 UoW）。
        /// </summary>
        public static void ValidateDataAccess(UnitOfWorkChainNode? readerNode, TenantKeySnapshot readerTenant)
        {
            var frame = ResolveVisibleFrame(readerNode);
            if (frame is null)
            {
                return; // 无受管窗口：无 UoW 的既有行为（含自动保存）不受影响
            }

            if (frame.Tenant.HasTenantSource &&
                readerTenant.HasTenantSource &&
                !frame.Tenant.Matches(readerTenant))
            {
                throw new InvalidOperationException(
                    $"The ambient unit-of-work resource belongs to tenant '{frame.Tenant.Value ?? "<host>"}' but the current " +
                    $"tenant is '{readerTenant.Value ?? "<host>"}'. Switching tenant (including switching to host) inside an " +
                    "active unit of work is not supported; start a new unit of work for the other tenant.");
            }

            if (frame.Source.IsOwner && (frame.Source.State != UnitOfWorkState.Active || frame.Source.IsReleased))
            {
                throw new InvalidOperationException(
                    $"The unit of work is '{frame.Source.State}' (released: {frame.Source.IsReleased}). Scope results remain " +
                    "readable, but new data operations require a new unit of work.");
            }
        }

        /// <summary>
        /// 解析读方可见的顶层受管 owner 状态（供 CAP lease 等受管窗口准入使用）。
        /// </summary>
        public static bool TryGetCurrentManagedState(
            UnitOfWorkChainNode? readerNode,
            out UnitOfWorkState state,
            out bool released)
        {
            for (var frame = CurrentSource.Value; frame is not null; frame = frame.Parent)
            {
                if (!IsVisible(frame, readerNode))
                {
                    continue;
                }

                if (!frame.Source.IsOwner)
                {
                    continue; // join 帧不承载受管窗口身份，继续向 owner 帧查找
                }

                state = frame.Source.State;
                released = frame.Source.IsReleased;
                return true;
            }

            state = UnitOfWorkState.Active;
            released = false;
            return false;
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
