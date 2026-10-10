namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 工作单元 scope 的参与状态。
    /// </summary>
    /// <remarks>
    /// 释放标志 <see cref="IUnitOfWorkScope.IsReleased"/> 独立存储，不覆盖本状态。
    /// </remarks>
    public enum UnitOfWorkState
    {
        /// <summary>已开始，未完成。</summary>
        Active = 0,

        /// <summary>成功完成已记录（owner = 数据库 commit 已确认 / 非事务 = flush 成功；join = 参与者成功）。</summary>
        Completed = 1,

        /// <summary>确定回滚完成（仅来自未提交状态）。</summary>
        RolledBack = 2,

        /// <summary>终态结果不确定（如提交结果未知、回滚自身失败）。</summary>
        Failed = 3
    }
}
