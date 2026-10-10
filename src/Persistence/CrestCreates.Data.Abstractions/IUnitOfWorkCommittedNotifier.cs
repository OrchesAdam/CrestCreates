using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Data.Abstractions
{
    /// <summary>
    /// Provider 侧提交后通知（域事件发布等）。由内核在「数据库 commit 已确认并记录」之后调用；
    /// 通知失败不改变事务结果、触发回滚或自动重试业务。
    /// </summary>
    public interface IUnitOfWorkCommittedNotifier
    {
        /// <summary>
        /// 发布已提交后的通知（收集待发布域事件并在成功后清除）。
        /// </summary>
        Task PublishCommittedNotificationsAsync(CancellationToken cancellationToken = default);
    }
}
