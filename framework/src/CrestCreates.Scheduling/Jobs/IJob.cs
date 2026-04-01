using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Scheduling.Jobs
{
    /// <summary>
    /// 定时任务接口
    /// </summary>
    public interface IJob
    {
        /// <summary>
        /// 执行任务
        /// </summary>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>任务</returns>
        Task ExecuteAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 任务元数据
    /// </summary>
    public class JobMetadata
    {
        /// <summary>
        /// 任务名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 任务组
        /// </summary>
        public string Group { get; set; } = "Default";

        /// <summary>
        /// cron表达式
        /// </summary>
        public string CronExpression { get; set; }

        /// <summary>
        /// 任务描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}