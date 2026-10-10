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
    /// 当前受管执行 token 的读入口（切片 2 升级为受管链校验访问器）。
    /// </summary>
    public static class UnitOfWorkExecutionContext
    {
        private static readonly AsyncLocal<SourceFrame?> CurrentSource = new();

        /// <summary>当前受管执行 token；无受管 scope 时为 default。</summary>
        public static CancellationToken CurrentExecutionToken =>
            CurrentSource.Value?.Source.Token ?? default;

        /// <summary>
        /// 把传入 CT 与当前受管执行 token 组合为有效 token。
        /// 返回的非 null Disposable 表示创建了联动 CTS，使用方必须释放。
        /// </summary>
        public static IDisposable? CombineWithCurrent(CancellationToken incoming, out CancellationToken effective)
        {
            var current = CurrentExecutionToken;

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

        internal static IDisposable Push(UnitOfWorkExecutionTokenSource source)
        {
            var frame = new SourceFrame(source, CurrentSource.Value);
            CurrentSource.Value = frame;
            return new FrameRestorer(frame);
        }

        private sealed class SourceFrame
        {
            public SourceFrame(UnitOfWorkExecutionTokenSource source, SourceFrame? parent)
            {
                Source = source;
                Parent = parent;
            }

            public UnitOfWorkExecutionTokenSource Source { get; }

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
