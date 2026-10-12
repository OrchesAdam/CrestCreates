namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 提交后通知结果（独立于事务结果）。
    /// </summary>
    public enum UnitOfWorkNotificationOutcome
    {
        /// <summary>未执行通知（回滚路径或尚未完成）。</summary>
        None = 0,

        /// <summary>通知执行成功。</summary>
        Succeeded = 1,

        /// <summary>通知执行失败；事务结果不变、不回滚、不自动重试。</summary>
        Failed = 2
    }
}
