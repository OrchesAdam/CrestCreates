using System;
using System.Threading.Tasks;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// 通知阶段：事务已确认提交后发布提交事件。失败不回滚、不谎称、不自动重试业务；
    /// 已提交事实保留在 <see cref="UnitOfWorkScope.TransactionOutcome"/>。
    /// </summary>
    internal static class UnitOfWorkScopeNotification
    {
        internal static async Task PublishAsync(UnitOfWorkScope scope)
        {
            if (scope.UnitOfWork is not IUnitOfWorkCommittedNotifier notifier)
            {
                return;
            }

            try
            {
                await notifier.PublishCommittedNotificationsAsync(scope.ExecutionToken).ConfigureAwait(false);
                scope.NotificationOutcome = UnitOfWorkNotificationOutcome.Succeeded;
            }
            catch (Exception notificationException)
            {
                scope.NotificationOutcome = UnitOfWorkNotificationOutcome.Failed;
                throw new UnitOfWorkPostCommitNotificationException(
                    "The unit of work committed successfully, but post-commit notifications failed. " +
                    "The committed fact is preserved: the database transaction was NOT rolled back and " +
                    "the business operation will not be retried automatically.",
                    notificationException);
            }
        }
    }
}
