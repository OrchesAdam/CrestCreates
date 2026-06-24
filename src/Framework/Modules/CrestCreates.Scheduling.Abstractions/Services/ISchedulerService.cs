using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Scheduling.Jobs;

namespace CrestCreates.Scheduling.Services;

public interface ISchedulerService
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    // Recurring jobs (Cron)
    Task<JobId> RegisterAsync<TJob>(JobMetadata metadata) where TJob : IJob;
    Task<JobId> RegisterAsync<TJob, TArg>(JobMetadata metadata) where TJob : IJob<TArg> where TArg : IJobArgs;

    // Delayed / scheduled jobs
    Task<JobId> ScheduleAsync<TJob>(TimeSpan delay, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob;
    Task<JobId> ScheduleAsync<TJob, TArg>(TimeSpan delay, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob<TArg> where TArg : IJobArgs;
    Task<JobId> ScheduleAsync<TJob>(DateTimeOffset scheduledTime, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob;
    Task<JobId> ScheduleAsync<TJob, TArg>(DateTimeOffset scheduledTime, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob<TArg> where TArg : IJobArgs;

    // Immediate execution
    Task<JobId> ExecuteNowAsync<TJob>(Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob;
    Task<JobId> ExecuteNowAsync<TJob, TArg>(TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : IJob<TArg> where TArg : IJobArgs;

    // Lifecycle
    Task DeleteAsync(JobId jobId);
    Task CancelAsync(JobId jobId);
    Task PauseAsync(JobId jobId);
    Task ResumeAsync(JobId jobId);

    // Query
    Task<bool> ExistsAsync(JobId jobId);
    Task<IEnumerable<JobInfo>> GetAllAsync(JobStatus status = JobStatus.All);
}
