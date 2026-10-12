using System;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 受管窗口的参与判定三值（CAP 原子发布等集成消费的公共权威结果）。
    /// </summary>
    public enum UnitOfWorkParticipationDecision
    {
        /// <summary>读方链上确无可见受管 owner 窗口（含独立 DI 根不可见他链）。</summary>
        NoVisibleWindow,

        /// <summary>参与获准（可进入 Provider 借用流程；原生句柄不经本结果暴露）。</summary>
        Granted,

        /// <summary>存在可见 owner 窗口但禁止参与（原因见 <see cref="UnitOfWorkParticipationResult.RefusalReason"/>）。</summary>
        Refused
    }

    /// <summary>参与被拒的确定性原因。</summary>
    public enum UnitOfWorkParticipationRefusalReason
    {
        /// <summary>owner 载体存在但未执行 StartAsync。</summary>
        NotStarted,

        /// <summary>owner 为非事务模式（没有可参与的事务）。</summary>
        NonTransactional,

        /// <summary>owner 处于终态（Completed/RolledBack/Failed）。</summary>
        Terminal,

        /// <summary>owner 已释放。</summary>
        Released,

        /// <summary>事务型 owner 已开始，但 Provider 未曝光受管事务关联（如未实现资源曝光接口）。</summary>
        NoTransactionAssociation,

        /// <summary>租户/资源身份与读方不匹配。</summary>
        ResourceMismatch
    }

    /// <summary>
    /// 参与判定结果：只报告决策与拒绝原因，**不暴露任何句柄/上下文对象**
    /// （原生 Context/Transaction 一律经 Provider 借用边界获取，见 #137 交接契约）。
    /// </summary>
    public readonly struct UnitOfWorkParticipationResult
    {
        private UnitOfWorkParticipationResult(
            UnitOfWorkParticipationDecision decision,
            UnitOfWorkParticipationRefusalReason? refusalReason)
        {
            Decision = decision;
            RefusalReason = refusalReason;
        }

        /// <summary>判定决策。</summary>
        public UnitOfWorkParticipationDecision Decision { get; }

        /// <summary>拒绝原因（仅 Refused 时非 null）。</summary>
        public UnitOfWorkParticipationRefusalReason? RefusalReason { get; }

        public static UnitOfWorkParticipationResult NoVisibleWindow()
            => new(UnitOfWorkParticipationDecision.NoVisibleWindow, null);

        public static UnitOfWorkParticipationResult Granted()
            => new(UnitOfWorkParticipationDecision.Granted, null);

        public static UnitOfWorkParticipationResult Refused(UnitOfWorkParticipationRefusalReason reason)
            => new(UnitOfWorkParticipationDecision.Refused, reason);
    }
}
