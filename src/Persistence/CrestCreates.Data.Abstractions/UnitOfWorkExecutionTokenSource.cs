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
    /// owner 窗口在 StartAsync 后还记录选定资源的逻辑键（声明种类 + 连接配置）与
    /// 实际资源/事务关联（<see cref="IUnitOfWorkManagedResource"/>），供逻辑资源准入与
    /// CAP 参与准入（lease/borrower）核对同一受管窗口、同一实际事务。
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

        /// <summary>是否已执行 StartAsync（未开始的载体不代表受管事务窗口）。</summary>
        internal bool HasStarted { get; private set; }

        /// <summary>该 owner 是否为事务型（非事务 owner 不参与 CAP 事务准入）。</summary>
        internal bool IsTransactionalOwner { get; private set; }

        /// <summary>选定资源的声明种类（逻辑键；StartAsync 成功后记录）。</summary>
        internal string? ManagedResourceKind { get; private set; }

        /// <summary>选定资源的连接配置（逻辑键；非关系型为 null）。</summary>
        internal string? ManagedResourceConnectionString { get; private set; }

        /// <summary>选定资源的原生上下文句柄（身份关联；不提供操作）。</summary>
        internal object? ManagedResourceContext { get; private set; }

        /// <summary>当前受管事务句柄（事务开始成功后记录；无事务为 null）。</summary>
        internal object? ManagedTransactionHandle { get; private set; }

        internal void Publish(CancellationToken token)
        {
            _token = token;
        }

        internal void MarkStarted()
        {
            HasStarted = true;
        }

        internal void PublishManagedResource(
            bool isTransactionalOwner,
            string? resourceKind,
            string? connectionString,
            object? resourceContext,
            object? transactionHandle)
        {
            IsTransactionalOwner = isTransactionalOwner;
            ManagedResourceKind = resourceKind;
            ManagedResourceConnectionString = connectionString;
            ManagedResourceContext = resourceContext;
            ManagedTransactionHandle = transactionHandle;
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
