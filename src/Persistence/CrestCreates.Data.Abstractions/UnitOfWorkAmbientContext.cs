using System;
using System.Threading;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元环境上下文：requiresNew 隔离期间，平台把内层作用域的上下文对象
    /// （由 Provider 绑定声明，例如 EF 的 IDataBaseContext）推入环境，
    /// 使已注入的业务依赖在操作时解析到当前 UoW 的上下文，而不是父作用域实例。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="UnitOfWorkManager"/> 在受管 requiresNew 子作用域开始时 Push、
    /// 结束时恢复；Provider 侧的上下文实现（如 EF DbContextAdapter）读取
    /// <see cref="Current"/> 完成切换。嵌套 requiresNew 通过帧链支持。
    /// </remarks>
    public static class UnitOfWorkAmbientContext
    {
        private static readonly AsyncLocal<AmbientContextFrame?> CurrentFrame = new();

        /// <summary>
        /// 当前环境上下文对象（无 requiresNew 隔离时为 null）
        /// </summary>
        public static object? Current => CurrentFrame.Value?.Context;

        /// <summary>
        /// 推入环境上下文，返回恢复令牌（Dispose 后恢复此前的环境）
        /// </summary>
        public static IDisposable Push(object context)
        {
            ArgumentNullException.ThrowIfNull(context);

            var frame = new AmbientContextFrame(context, CurrentFrame.Value);
            CurrentFrame.Value = frame;
            return new FrameRestorer(frame);
        }

        private sealed class AmbientContextFrame
        {
            public AmbientContextFrame(object context, AmbientContextFrame? parent)
            {
                Context = context;
                Parent = parent;
            }

            public object Context { get; }

            public AmbientContextFrame? Parent { get; }
        }

        private sealed class FrameRestorer : IDisposable
        {
            private readonly AmbientContextFrame _frame;
            private bool _disposed;

            public FrameRestorer(AmbientContextFrame frame)
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

                // 仅当本帧仍是栈顶时恢复；乱序释放不允许回退别人的环境。
                if (ReferenceEquals(CurrentFrame.Value, _frame))
                {
                    CurrentFrame.Value = _frame.Parent;
                }
            }
        }
    }
}
