using System;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// owner 完成时发现被标记 rollback-only（有参与者失败/未完成退出）时抛出。
    /// 抛出前已完成回滚，不得静默提交部分失败。
    /// </summary>
    public sealed class UnitOfWorkRollbackOnlyException : InvalidOperationException
    {
        public UnitOfWorkRollbackOnlyException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// 数据库提交已确认，但提交后通知（域事件发布等）失败。
    /// 事务已提交的事实保留：不回滚、不自动重试业务；失败可检查。
    /// </summary>
    public sealed class UnitOfWorkPostCommitNotificationException : Exception
    {
        public UnitOfWorkPostCommitNotificationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
