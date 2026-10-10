using System;
using System.Threading;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 受管执行上下文的执行 token 载体（per-scope 对象；AsyncLocal 帧仅保存引用）。
    /// </summary>
    /// <remarks>
    /// 载体在调用方帧同步 Push（见设计 §4.1 激活协议）；token 由内核在 StartAsync
    /// 通过变更载体字段发布，使调用方帧与织入方法体内的正式仓储/Provider 都能组合到
    /// 「传入 CT ⊕ 受管执行 token」。载体随 scope 释放恢复，不跨 scope 复用。
    /// 帧携带 owner 链节点：读方（正式仓储，可注入自身节点）只跟随自身或其后代的帧。
    /// </remarks>
    public sealed class UnitOfWorkExecutionTokenSource
    {
        private CancellationToken _token;

        /// <summary>受管执行 token（StartAsync 之前为 default）。</summary>
        public CancellationToken Token => _token;

        internal void Publish(CancellationToken token)
        {
            _token = token;
        }
    }

    /// <summary>
    /// 当前受管执行 token 的读入口（链校验：只跟随自身或其后代的帧）。
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
            var current = ResolveVisibleToken(readerNode);

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

        private static CancellationToken ResolveVisibleToken(UnitOfWorkChainNode? readerNode)
        {
            for (var frame = CurrentSource.Value; frame is not null; frame = frame.Parent)
            {
                if (readerNode is not null &&
                    frame.OwnerNode is not null &&
                    !frame.OwnerNode.IsSelfOrDescendantOf(readerNode))
                {
                    continue; // 帧不在读方链上，视为不可见（独立 scope / 其他宿主）
                }

                return frame.Source.Token;
            }

            return default;
        }

        internal static IDisposable Push(UnitOfWorkExecutionTokenSource source, UnitOfWorkChainNode? ownerNode)
        {
            var frame = new SourceFrame(source, ownerNode, CurrentSource.Value);
            CurrentSource.Value = frame;
            return new FrameRestorer(frame);
        }

        private sealed class SourceFrame
        {
            public SourceFrame(UnitOfWorkExecutionTokenSource source, UnitOfWorkChainNode? ownerNode, SourceFrame? parent)
            {
                Source = source;
                OwnerNode = ownerNode;
                Parent = parent;
            }

            public UnitOfWorkExecutionTokenSource Source { get; }

            public UnitOfWorkChainNode? OwnerNode { get; }

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
