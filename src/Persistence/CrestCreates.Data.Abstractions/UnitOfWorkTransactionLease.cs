using System;
using System.Data;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// CAP 最小参与入口：受限借用 lease 的 Provider 所有签发者（绑定/注册声明）。
    /// </summary>
    public interface IUnitOfWorkTransactionLeaseProvider
    {
        /// <summary>
        /// 尝试为「当前受管窗口的有效资源」签发 lease；无活动事务（未开始/非事务/已终结）时返回 false。
        /// </summary>
        bool TryAcquireCurrentLease(out UnitOfWorkTransactionLease? lease);
    }

    /// <summary>
    /// 只读事务身份/状态视图：生命周期操作由内核唯一掌握，本视图不暴露 Commit/Rollback/Dispose。
    /// </summary>
    public interface IUnitOfWorkTransactionIdentity
    {
        /// <summary>事务标识（与数据库侧可核对）。</summary>
        Guid TransactionId { get; }

        /// <summary>事务是否已完结（提交/回滚/句柄释放后为 true）。</summary>
        bool IsCompleted { get; }

        /// <summary>隔离级别；Provider 无法读取时为 null。</summary>
        IsolationLevel? IsolationLevel { get; }

        /// <summary>
        /// 原生事务对象：**仅限 Provider 适配边界借用**（如 CAP SDK 的 transaction 适配）。
        /// 借用规则：不得 Dispose / Commit / Rollback，仅可读取身份并在同一事务内附加命令。
        /// </summary>
        object NativeTransaction { get; }
    }

    /// <summary>
    /// 受限借用视图（live view；不是万能扩展点）。
    /// </summary>
    /// <remarks>
    /// 由 Provider 在受管链当前窗口内签发；仅在所属 scope `Active` 且未释放时有效；
    /// 终态/释放后 <see cref="IsValid"/> 为 false，继续参与按确定性拒绝处理。
    /// 不允许持有到 scope 之外；每个参与动作前应重新获取。
    /// </remarks>
    public sealed class UnitOfWorkTransactionLease
    {
        internal UnitOfWorkTransactionLease(
            OrmProvider provider,
            object resourceIdentity,
            IUnitOfWorkTransactionIdentity transaction)
        {
            Provider = provider;
            ResourceIdentity = resourceIdentity;
            Transaction = transaction;
        }

        /// <summary>ORM 提供者。</summary>
        public OrmProvider Provider { get; }

        /// <summary>物理实例身份（当前资源 Context/连接实例标记；不得用逻辑键替代）。</summary>
        public object ResourceIdentity { get; }

        /// <summary>只读事务身份/状态视图。</summary>
        public IUnitOfWorkTransactionIdentity Transaction { get; }

        /// <summary>live view 有效性：事务已完结/句柄释放后为 false。</summary>
        public bool IsValid => !Transaction.IsCompleted;
    }
}
