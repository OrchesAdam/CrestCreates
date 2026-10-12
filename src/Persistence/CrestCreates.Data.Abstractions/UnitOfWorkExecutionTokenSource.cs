using System.Threading;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 受管执行上下文的执行 token 载体（per-scope 对象；AsyncLocal 帧仅保存引用）。
    /// </summary>
    /// <remarks>
    /// 载体在调用方帧同步 Push；token 由内核在 StartAsync 通过变更载体字段发布，
    /// 使调用方帧与织入方法体内的正式仓储/Provider 都能组合到「传入 CT ⊕ 受管执行 token」。
    /// 载体随 scope 释放恢复，不跨 scope 复用；同时携带 owner 身份与生命周期状态，
    /// 读方据终态守卫拒绝新数据动作（结果仍可读）。
    /// </remarks>
    public sealed class UnitOfWorkExecutionTokenSource
    {
        private CancellationToken _token;

        /// <summary>受管执行 token（StartAsync 之前为 default）。</summary>
        public CancellationToken Token => _token;

        /// <summary>该载体是否为 owner（顶层/隔离）；join 为 false。</summary>
        public bool IsOwner { get; internal set; }

        /// <summary>参与状态（内核维护；读方据终态守卫数据准入）。</summary>
        public UnitOfWorkState State { get; private set; } = UnitOfWorkState.Active;

        /// <summary>是否已释放。</summary>
        public bool IsReleased { get; private set; }

        internal void Publish(CancellationToken token)
        {
            _token = token;
        }

        internal void TransitionTo(UnitOfWorkState state)
        {
            State = state;
        }

        internal void MarkReleased()
        {
            IsReleased = true;
        }
    }
}
