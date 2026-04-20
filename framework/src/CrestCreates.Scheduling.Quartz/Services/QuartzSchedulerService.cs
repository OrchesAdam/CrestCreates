using System.Text.Json;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using IScheduler = Quartz.IScheduler;
using SchedulingJobs = CrestCreates.Scheduling.Jobs;
using SchedulingServices = CrestCreates.Scheduling.Services;

namespace CrestCreates.Scheduling.Quartz.Services;

public class QuartzSchedulerService : ISchedulerService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QuartzSchedulerService> _logger;
    private IScheduler? _scheduler;

    public QuartzSchedulerService(
        IServiceProvider serviceProvider,
        ILogger<QuartzSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_scheduler != null) return;

        var factory = new StdSchedulerFactory();
        _scheduler = await factory.GetScheduler(ct);
        await _scheduler.Start(ct);
        _logger.LogInformation("Quartz scheduler started");
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_scheduler != null)
        {
            await _scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken: ct);
            _logger.LogInformation("Quartz scheduler stopped");
        }
    }

    public async Task<SchedulingJobs.JobId> RegisterAsync<TJob>(SchedulingServices.JobMetadata metadata) where TJob : SchedulingJobs.IJob
        => await RegisterAsync<TJob, SchedulingJobs.NoArgs>(metadata);

    public async Task<SchedulingJobs.JobId> RegisterAsync<TJob, TArg>(SchedulingServices.JobMetadata metadata)
        where TJob : SchedulingJobs.IJob<TArg>
        where TArg : SchedulingJobs.IJobArgs
    {
        await EnsureStarted();

        var jobId = SchedulingJobs.JobId.Create(metadata.Name, metadata.Group);
        var jobKey = new JobKey(metadata.Name, metadata.Group);

        if (await _scheduler!.CheckExists(jobKey))
        {
            await _scheduler.DeleteJob(jobKey);
        }

        var jobData = new JobDataMap
        {
            { "Args", JsonSerializer.Serialize(default(TArg)) }
        };

        var job = JobBuilder.Create<CrestCreates.Scheduling.Quartz.Jobs.QuartzJobAdapter<TJob, TArg>>()
            .WithIdentity(jobKey)
            .WithDescription(metadata.Description)
            .UsingJobData(jobData)
            .StoreDurably()
            .Build();

        ITrigger trigger;

        if (!string.IsNullOrEmpty(metadata.CronExpression))
        {
            trigger = TriggerBuilder.Create()
                .WithIdentity($"{metadata.Name}Trigger", metadata.Group)
                .WithCronSchedule(metadata.CronExpression)
                .ForJob(jobKey)
                .Build();
        }
        else
        {
            trigger = TriggerBuilder.Create()
                .WithIdentity($"{metadata.Name}Trigger", metadata.Group)
                .StartNow()
                .ForJob(jobKey)
                .Build();
        }

        await _scheduler.ScheduleJob(job, trigger);
        _logger.LogInformation("Registered job {JobName} in group {Group}", metadata.Name, metadata.Group);

        return jobId;
    }

    public async Task<SchedulingJobs.JobId> ScheduleAsync<TJob>(TimeSpan delay, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : SchedulingJobs.IJob
        => await ScheduleAsync<TJob, SchedulingJobs.NoArgs>(delay, new SchedulingJobs.NoArgs(), tenantId, organizationId, userId);

    public async Task<SchedulingJobs.JobId> ScheduleAsync<TJob, TArg>(TimeSpan delay, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null)
        where TJob : SchedulingJobs.IJob<TArg>
        where TArg : SchedulingJobs.IJobArgs
        => await ScheduleInternalAsync<TJob, TArg>(DateTimeOffset.UtcNow.Add(delay), args, tenantId, organizationId, userId);

    public async Task<SchedulingJobs.JobId> ScheduleAsync<TJob>(DateTimeOffset scheduledTime, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : SchedulingJobs.IJob
        => await ScheduleAsync<TJob, SchedulingJobs.NoArgs>(scheduledTime, new SchedulingJobs.NoArgs(), tenantId, organizationId, userId);

    public async Task<SchedulingJobs.JobId> ScheduleAsync<TJob, TArg>(DateTimeOffset scheduledTime, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null)
        where TJob : SchedulingJobs.IJob<TArg>
        where TArg : SchedulingJobs.IJobArgs
        => await ScheduleInternalAsync<TJob, TArg>(scheduledTime, args, tenantId, organizationId, userId);

    private async Task<SchedulingJobs.JobId> ScheduleInternalAsync<TJob, TArg>(DateTimeOffset scheduledTime, TArg args, Guid? tenantId, Guid? organizationId, Guid? userId)
        where TJob : SchedulingJobs.IJob<TArg>
        where TArg : SchedulingJobs.IJobArgs
    {
        await EnsureStarted();

        var jobId = SchedulingJobs.JobId.New();
        var jobKey = new JobKey(jobId.Uuid.ToString());
        var triggerKey = new TriggerKey($"{jobId.Uuid}Trigger");

        var argsJson = JsonSerializer.Serialize(args);
        var jobData = new JobDataMap
        {
            { "Args", argsJson },
            { "TenantId", tenantId?.ToString() ?? string.Empty },
            { "OrganizationId", organizationId?.ToString() ?? string.Empty },
            { "UserId", userId?.ToString() ?? string.Empty },
            { "AttemptNumber", 1 }
        };

        var job = JobBuilder.Create<CrestCreates.Scheduling.Quartz.Jobs.QuartzJobAdapter<TJob, TArg>>()
            .WithIdentity(jobKey)
            .UsingJobData(jobData)
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .StartAt(scheduledTime)
            .ForJob(jobKey)
            .Build();

        // Call OnJobScheduledAsync if IJobExecutionHandler is registered
        var executionHandler = _serviceProvider.GetService<IJobExecutionHandler>();
        if (executionHandler != null)
        {
            await executionHandler.OnJobScheduledAsync(new JobScheduledContext(
                jobId.Uuid,
                typeof(TJob),
                typeof(TArg),
                tenantId,
                organizationId,
                userId,
                argsJson,
                scheduledTime
            ));
        }

        await _scheduler!.ScheduleJob(job, trigger);
        _logger.LogInformation("Scheduled job {JobId} for {ScheduledTime}", jobId, scheduledTime);

        return jobId;
    }

    public async Task ExecuteNowAsync<TJob>(Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : SchedulingJobs.IJob
        => await ExecuteNowAsync<TJob, SchedulingJobs.NoArgs>(new SchedulingJobs.NoArgs(), tenantId, organizationId, userId);

    public async Task ExecuteNowAsync<TJob, TArg>(TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null)
        where TJob : SchedulingJobs.IJob<TArg>
        where TArg : SchedulingJobs.IJobArgs
    {
        await EnsureStarted();

        var jobId = SchedulingJobs.JobId.New();
        var jobKey = new JobKey(jobId.Uuid.ToString());

        var argsJson = JsonSerializer.Serialize(args);
        var jobData = new JobDataMap
        {
            { "Args", argsJson },
            { "TenantId", tenantId?.ToString() ?? string.Empty },
            { "OrganizationId", organizationId?.ToString() ?? string.Empty },
            { "UserId", userId?.ToString() ?? string.Empty },
            { "AttemptNumber", 1 }
        };

        var job = JobBuilder.Create<CrestCreates.Scheduling.Quartz.Jobs.QuartzJobAdapter<TJob, TArg>>()
            .WithIdentity(jobKey)
            .UsingJobData(jobData)
            .StoreDurably()
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobId.Uuid}Trigger")
            .StartNow()
            .ForJob(jobKey)
            .Build();

        // Call OnJobScheduledAsync if IJobExecutionHandler is registered
        var executionHandler = _serviceProvider.GetService<IJobExecutionHandler>();
        if (executionHandler != null)
        {
            await executionHandler.OnJobScheduledAsync(new JobScheduledContext(
                jobId.Uuid,
                typeof(TJob),
                typeof(TArg),
                tenantId,
                organizationId,
                userId,
                argsJson,
                DateTimeOffset.UtcNow
            ));
        }

        await _scheduler!.ScheduleJob(job, trigger);
    }

    public async Task DeleteAsync(SchedulingJobs.JobId jobId)
    {
        await EnsureStarted();
        var key = ToQuartzKey(jobId);
        await _scheduler!.DeleteJob(key);
        _logger.LogInformation("Deleted job {JobId}", jobId);
    }

    public async Task CancelAsync(SchedulingJobs.JobId jobId)
    {
        await EnsureStarted();
        var key = new JobKey(jobId.Uuid.ToString());
        if (await _scheduler!.CheckExists(key))
        {
            await _scheduler.Interrupt(key);
        }
    }

    public async Task PauseAsync(SchedulingJobs.JobId jobId)
    {
        await EnsureStarted();
        var key = ToQuartzKey(jobId);
        await _scheduler!.PauseJob(key);
    }

    public async Task ResumeAsync(SchedulingJobs.JobId jobId)
    {
        await EnsureStarted();
        var key = ToQuartzKey(jobId);
        await _scheduler!.ResumeJob(key);
    }

    public async Task<bool> ExistsAsync(SchedulingJobs.JobId jobId)
    {
        await EnsureStarted();
        var key = ToQuartzKey(jobId);
        return await _scheduler!.CheckExists(key);
    }

    public async Task<IEnumerable<SchedulingServices.JobInfo>> GetAllAsync(JobStatus status = JobStatus.All)
    {
        await EnsureStarted();
        var jobGroups = await _scheduler!.GetJobGroupNames();
        var result = new List<SchedulingServices.JobInfo>();

        foreach (var group in jobGroups)
        {
            var jobKeys = await _scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group));
            foreach (var key in jobKeys)
            {
                var detail = await _scheduler.GetJobDetail(key);
                var triggers = await _scheduler.GetTriggersOfJob(key);
                var trigger = triggers.FirstOrDefault();

                var jobStatus = await ToJobStatusAsync(triggers, _scheduler);

                if (status != JobStatus.All && jobStatus != status)
                    continue;

                result.Add(new SchedulingServices.JobInfo(
                    Id: new SchedulingJobs.JobId(key.Name, key.Group, Guid.Empty),
                    JobType: detail!.JobType,
                    ArgType: detail!.JobType.IsGenericType ? detail.JobType.GetGenericArguments()[0] : null,
                    CronExpression: trigger is ICronTrigger cronTrigger ? cronTrigger.CronExpressionString : null,
                    NextFireTime: trigger?.GetNextFireTimeUtc()?.LocalDateTime,
                    Status: jobStatus,
                    ExecutionCount: null
                ));
            }
        }

        return result;
    }

    private async Task<SchedulingServices.JobStatus> ToJobStatusAsync(IEnumerable<ITrigger> triggers, IScheduler scheduler)
    {
        var triggerList = triggers.ToList();
        if (triggerList.Count == 0)
            return SchedulingServices.JobStatus.Completed;

        // Error state has highest priority - job execution failed
        foreach (var trigger in triggerList)
        {
            if (await scheduler.GetTriggerState(trigger.Key) == TriggerState.Error)
                return SchedulingServices.JobStatus.Failed;
        }

        // Blocked means job is executing
        foreach (var trigger in triggerList)
        {
            if (await scheduler.GetTriggerState(trigger.Key) == TriggerState.Blocked)
                return SchedulingServices.JobStatus.Running;
        }

        // All triggers paused = job paused
        var allPaused = true;
        foreach (var trigger in triggerList)
        {
            if (await scheduler.GetTriggerState(trigger.Key) != TriggerState.Paused)
            {
                allPaused = false;
                break;
            }
        }
        if (allPaused)
            return SchedulingServices.JobStatus.Paused;

        // Check if any trigger is scheduled with a future fire time
        foreach (var trigger in triggerList)
        {
            if (await scheduler.GetTriggerState(trigger.Key) == TriggerState.Normal && trigger.GetNextFireTimeUtc().HasValue)
                return SchedulingServices.JobStatus.Scheduled;
        }

        // Complete state or no future fire time = completed
        return SchedulingServices.JobStatus.Completed;
    }

    private JobKey ToQuartzKey(SchedulingJobs.JobId jobId)
    {
        return jobId.Uuid != Guid.Empty
            ? new JobKey(jobId.Uuid.ToString())
            : new JobKey(jobId.Name!, jobId.Group!);
    }

    private async Task EnsureStarted()
    {
        if (_scheduler == null)
        {
            await StartAsync();
        }
    }
}
