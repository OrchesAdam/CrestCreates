using System;
using System.Data;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 不可操作的资源标识（公开面）：提供身份与稳定性，不提供任何生命周期或原生化操作。
    /// </summary>
    public readonly struct UnitOfWorkResourceIdentity
    {
        internal UnitOfWorkResourceIdentity(OrmProvider provider, string resourceKind, Guid instanceId)
        {
            Provider = provider;
            ResourceKind = resourceKind;
            InstanceId = instanceId;
        }

        /// <summary>ORM 提供者。</summary>
        public OrmProvider Provider { get; }

        /// <summary>资源声明种类（如 Context 声明类型全名；诊断/关联用）。</summary>
        public string ResourceKind { get; }

        /// <summary>本次有效资源实例的稳定标识（同一实例多次读取一致；不可用于取得实例）。</summary>
        public Guid InstanceId { get; }
    }

    /// <summary>
    /// 原生事务的**非操作性借用标记**：仅携带身份关联信息，不是句柄本体，
    /// 不可强转为 Provider 的原生事务类型；原生句柄借用由 Provider 专属 borrower 边界提供
    /// （如 EF 包的 <c>IEfCoreTransactionBorrower</c>），不在此公开视图中。
    /// </summary>
    public sealed class UnitOfWorkNativeTransactionMarker
    {
        internal UnitOfWorkNativeTransactionMarker(Guid transactionId)
        {
            TransactionId = transactionId;
        }

        /// <summary>事务标识（与 <c>IUnitOfWorkTransactionIdentity.TransactionId</c> 一致）。</summary>
        public Guid TransactionId { get; }
    }

    /// <summary>
    /// CAP 最小参与入口：受限借用 lease 的 Provider 所有签发者（绑定/注册声明）。
    /// </summary>
    public interface IUnitOfWorkTransactionLeaseProvider
    {
        /// <summary>
        /// 尝试为「当前受管窗口的有效资源」签发 lease；
        /// 未开始、终态、已释放、独立 scope/宿主，或无活动事务时返回 false。
        /// </summary>
        bool TryAcquireCurrentLease(out UnitOfWorkTransactionLease? lease);
    }

    /// <summary>
    /// 只读事务身份/状态视图：生命周期操作由内核唯一掌握，本视图不暴露 Commit/Rollback/Dispose，
    /// 也不返回原生事务/上下文句柄本体。
    /// </summary>
    public interface IUnitOfWorkTransactionIdentity
    {
        /// <summary>事务标识（与数据库侧可核对）。</summary>
        Guid TransactionId { get; }

        /// <summary>事务是否已完结/失效（owner 终态或已释放，或事务关联终结后为 true）。</summary>
        bool IsCompleted { get; }

        /// <summary>隔离级别；Provider 无法读取时为 null。</summary>
        IsolationLevel? IsolationLevel { get; }

        /// <summary>
        /// 原生事务借用标记：**非操作性对象**（不是句柄本体，不可强转为原生事务类型）。
        /// 需要原生句柄的 SDK 适配（如 CAP）必须通过 Provider 专属 borrower 边界借用，
        /// 并遵守「不得 Dispose / Commit / Rollback」的借用规则。
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
            UnitOfWorkResourceIdentity resource,
            IUnitOfWorkTransactionIdentity transaction)
        {
            Resource = resource;
            Transaction = transaction;
        }

        /// <summary>不可操作的资源标识。</summary>
        public UnitOfWorkResourceIdentity Resource { get; }

        /// <summary>只读事务身份/状态视图。</summary>
        public IUnitOfWorkTransactionIdentity Transaction { get; }

        /// <summary>live view 有效性：事务已完结/句柄释放后为 false。</summary>
        public bool IsValid => !Transaction.IsCompleted;
    }
}
