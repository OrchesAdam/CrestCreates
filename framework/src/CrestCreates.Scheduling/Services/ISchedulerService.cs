using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Scheduling.Services
{
    public interface ISchedulerService
    {
        /// <summary>
        /// 启动调度器
        /// </summary>
        /// <returns>任务</returns>
        Task StartAsync();

        /// <summary>
        /// 停止调度器
        /// </summary>
        /// <returns>任务</returns>
        Task StopAsync();

        /// <summary>
        /// 注册任务
        /// </summary>
        /// <typeparam name="TJob">任务类型</typeparam>
        /// <param name="metadata">任务元数据</param>
        /// <returns>任务</returns>
        Task RegisterJobAsync<TJob>(JobMetadata metadata) where TJob : IJob;

        /// <summary>
        /// 延迟调度任务
        /// </summary>
        /// <typeparam name="TJob">任务类型</typeparam>
        /// <param name="delay">延迟时间</param>
        /// <returns>任务ID</returns>
        Task<string> ScheduleJobAsync<TJob>(TimeSpan delay) where TJob : IJob;

        /// <summary>
        /// 指定时间调度任务
        /// </summary>
        /// <typeparam name="TJob">任务类型</typeparam>
        /// <param name="scheduledTime">调度时间</param>
        /// <returns>任务ID</returns>
        Task<string> ScheduleJobAsync<TJob>(DateTimeOffset scheduledTime) where TJob : IJob;

        /// <summary>
        /// Cron表达式调度任务
        /// </summary>
        /// <typeparam name="TJob">任务类型</typeparam>
        /// <param name="cronExpression">Cron表达式</param>
        /// <param name="group">任务组</param>
        /// <returns>任务ID</returns>
        Task<string> ScheduleJobAsync<TJob>(string cronExpression, string? group = null) where TJob : IJob;

        /// <summary>
        /// 立即执行任务
        /// </summary>
        /// <typeparam name="TJob">任务类型</typeparam>
        /// <returns>任务</returns>
        Task ExecuteJobAsync<TJob>() where TJob : IJob;

        /// <summary>
        /// 暂停任务
        /// </summary>
        /// <param name="jobName">任务名称</param>
        /// <param name="jobGroup">任务组</param>
        /// <returns>任务</returns>
        Task PauseJobAsync(string jobName, string jobGroup = "Default");

        /// <summary>
        /// 恢复任务
        /// </summary>
        /// <param name="jobName">任务名称</param>
        /// <param name="jobGroup">任务组</param>
        /// <returns>任务</returns>
        Task ResumeJobAsync(string jobName, string jobGroup = "Default");

        /// <summary>
        /// 删除任务
        /// </summary>
        /// <param name="jobName">任务名称</param>
        /// <param name="jobGroup">任务组</param>
        /// <returns>任务</returns>
        Task DeleteJobAsync(string jobName, string jobGroup = "Default");

        /// <summary>
        /// 取消任务
        /// </summary>
        /// <param name="jobId">任务ID</param>
        /// <returns>任务</returns>
        Task CancelJobAsync(string jobId);

        /// <summary>
        /// 暂停任务
        /// </summary>
        /// <param name="jobId">任务ID</param>
        /// <returns>任务</returns>
        Task PauseJobAsync(string jobId);

        /// <summary>
        /// 恢复任务
        /// </summary>
        /// <param name="jobId">任务ID</param>
        /// <returns>任务</returns>
        Task ResumeJobAsync(string jobId);

        /// <summary>
        /// 检查任务是否存在
        /// </summary>
        /// <param name="jobId">任务ID</param>
        /// <returns>是否存在</returns>
        Task<bool> JobExistsAsync(string jobId);

        /// <summary>
        /// 获取所有任务
        /// </summary>
        /// <returns>任务列表</returns>
        Task<IEnumerable<JobMetadata>> GetAllJobsAsync();
    }
}