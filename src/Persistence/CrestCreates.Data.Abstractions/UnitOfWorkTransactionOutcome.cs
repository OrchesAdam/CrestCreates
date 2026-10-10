namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 事务结果（owner 专用，不可被后续操作改写）。
    /// </summary>
    public enum UnitOfWorkTransactionOutcome
    {
        /// <summary>尚未完成数据库提交/回滚。</summary>
        NotStarted = 0,

        /// <summary>数据库 commit 已确认（确认时立即记录）。</summary>
        Committed = 1,

        /// <summary>确定回滚完成。</summary>
        RolledBack = 2,

        /// <summary>提交结果未知（提交派发期失败、响应丢失等）；回滚尝试不得改写。</summary>
        Unknown = 3
    }
}
