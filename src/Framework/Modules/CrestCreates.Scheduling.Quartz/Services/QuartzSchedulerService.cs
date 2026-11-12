using System.Collections.Specialized;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using SchedulingJobs = CrestCreates.Scheduling.Jobs;
using SchedulingServices = CrestCreates.Scheduling.Services;

namespace CrestCreates.Scheduling.Quartz.Services;

public class QuartzSchedulerService(IServiceProvider serviceProvider, ILogger<QuartzSchedulerService> logger)
    : ISchedulerService, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private global::Quartz.IScheduler? _scheduler;
    private bool _stopped;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            if (_stopped) throw new InvalidOperationException("The scheduler has been stopped.");
            if (_scheduler != null) return;
            var factory = new StdSchedulerFactory(new NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = $"CrestCreates-{Guid.NewGuid():N}",
                ["quartz.scheduler.instanceId"] = "AUTO",
                ["quartz.jobStore.type"] = "Quartz.Simpl.RAMJobStore, Quartz"
            });
            var scheduler = await factory.GetScheduler(ct);
            try
            {
                scheduler.JobFactory = new Jobs.DependencyInjectionJobFactory(serviceProvider);
                await scheduler.Start(ct);
                _scheduler = scheduler;
            }
            catch
            {
                await scheduler.Shutdown(false, CancellationToken.None);
                throw;
            }
            logger.LogInformation("Quartz scheduler started");
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        await _lifecycle.WaitAsync(ct);
        try
        {
            _stopped = true;
            if (_scheduler == null || _scheduler.IsShutdown) return;
            // Once shutdown begins it owns completion, even if the caller cancels.
            await _scheduler.Shutdown(true, CancellationToken.None);
            logger.LogInformation("Quartz scheduler stopped");
        }
        finally { _lifecycle.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    public Task<SchedulingJobs.JobId> RegisterAsync<TJob>(JobMetadata metadata) where TJob : SchedulingJobs.IJob
        => RegisterAsync<TJob, SchedulingJobs.NoArgs>(metadata, new());

    public Task<SchedulingJobs.JobId> RegisterAsync<TJob, TArg>(JobMetadata metadata)
        where TJob : SchedulingJobs.IJob<TArg> where TArg : SchedulingJobs.IJobArgs
        => typeof(TArg) == typeof(SchedulingJobs.NoArgs)
            ? RegisterAsync<TJob, TArg>(metadata, (TArg)(object)new SchedulingJobs.NoArgs())
            : throw new ArgumentException("Parameterized jobs require RegisterAsync(metadata, args).", nameof(metadata));

    public Task<SchedulingJobs.JobId> RegisterAsync<TJob, TArg>(JobMetadata metadata, TArg args)
        where TJob : SchedulingJobs.IJob<TArg> where TArg : SchedulingJobs.IJobArgs
        => ScheduleInternalAsync<TJob, TArg>(DateTimeOffset.UtcNow, args, null, null, null, metadata);

    public Task<SchedulingJobs.JobId> ScheduleAsync<TJob>(TimeSpan delay, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : SchedulingJobs.IJob
        => ScheduleAsync<TJob, SchedulingJobs.NoArgs>(delay, new(), tenantId, organizationId, userId);
    public Task<SchedulingJobs.JobId> ScheduleAsync<TJob, TArg>(TimeSpan delay, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null)
        where TJob : SchedulingJobs.IJob<TArg> where TArg : SchedulingJobs.IJobArgs
        => ScheduleInternalAsync<TJob, TArg>(DateTimeOffset.UtcNow.Add(delay), args, tenantId, organizationId, userId);
    public Task<SchedulingJobs.JobId> ScheduleAsync<TJob>(DateTimeOffset scheduledTime, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : SchedulingJobs.IJob
        => ScheduleAsync<TJob, SchedulingJobs.NoArgs>(scheduledTime, new(), tenantId, organizationId, userId);
    public Task<SchedulingJobs.JobId> ScheduleAsync<TJob, TArg>(DateTimeOffset scheduledTime, TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null)
        where TJob : SchedulingJobs.IJob<TArg> where TArg : SchedulingJobs.IJobArgs
        => ScheduleInternalAsync<TJob, TArg>(scheduledTime, args, tenantId, organizationId, userId);
    public Task<SchedulingJobs.JobId> ExecuteNowAsync<TJob>(Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null) where TJob : SchedulingJobs.IJob
        => ExecuteNowAsync<TJob, SchedulingJobs.NoArgs>(new(), tenantId, organizationId, userId);
    public Task<SchedulingJobs.JobId> ExecuteNowAsync<TJob, TArg>(TArg args, Guid? tenantId = null, Guid? organizationId = null, Guid? userId = null)
        where TJob : SchedulingJobs.IJob<TArg> where TArg : SchedulingJobs.IJobArgs
        => ScheduleInternalAsync<TJob, TArg>(DateTimeOffset.UtcNow, args, tenantId, organizationId, userId);

    private async Task<SchedulingJobs.JobId> ScheduleInternalAsync<TJob, TArg>(DateTimeOffset time, TArg args,
        Guid? tenantId, Guid? organizationId, Guid? userId, JobMetadata? metadata = null)
        where TJob : SchedulingJobs.IJob<TArg> where TArg : SchedulingJobs.IJobArgs
    {
        // Fail before creating scheduler state when the required static contract is absent.
        var json = JobArgsJson<TArg>.Encode(serviceProvider, args);
        if (metadata?.Timeout is { } timeout && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1))
            throw new ArgumentOutOfRangeException(nameof(metadata), "Timeout must be a positive supported cooperative deadline.");
        metadata?.Retry?.Validate();
        var uuid = Guid.NewGuid();
        var key = new JobKey(metadata?.Name ?? uuid.ToString(), metadata?.Group ?? SchedulerConstants.DefaultGroup);
        var id = new SchedulingJobs.JobId(key.Name, key.Group, uuid);
        var data = new JobDataMap
        {
            ["JobUuid"] = uuid.ToString(), ["Args"] = json,
            ["TenantId"] = tenantId?.ToString() ?? "", ["OrganizationId"] = organizationId?.ToString() ?? "",
            ["UserId"] = userId?.ToString() ?? "", ["BusinessJobType"] = typeof(TJob), ["ArgType"] = typeof(TArg)
        };
        if (metadata?.Timeout is { } deadline) data["Timeout"] = deadline;
        if (metadata?.Retry is { } retry) data["Retry"] = retry;
        var job = JobBuilder.Create<Jobs.QuartzJobAdapter<TJob, TArg>>().WithIdentity(key)
            .WithDescription(metadata?.Description).UsingJobData(data).StoreDurably().Build();
        var builder = TriggerBuilder.Create().WithIdentity("primary", uuid.ToString()).ForJob(key).StartAt(time);
        if (!string.IsNullOrWhiteSpace(metadata?.CronExpression))
            builder.WithCronSchedule(metadata.CronExpression, cron => cron.WithMisfireHandlingInstructionFireAndProceed());
        var trigger = builder.Build();
        await StartAsync();
        // Pausing the group before insertion prevents an immediate disabled job from racing execution.
        if (metadata?.Enabled == false)
            await _scheduler!.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(uuid.ToString()));
        await using var scope = serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetService<IJobExecutionHandler>();
        if (handler != null)
            await handler.OnJobScheduledAsync(new(uuid, typeof(TJob), typeof(TArg), tenantId, organizationId, userId, json, time));
        // Duplicate names fail explicitly; replacing an active job would orphan its retry/control identity.
        await _scheduler!.ScheduleJob(job, trigger);
        return id;
    }

    public async Task DeleteAsync(SchedulingJobs.JobId jobId) { await StartAsync(); await _scheduler!.DeleteJob(ToQuartzKey(jobId)); }
    public async Task CancelAsync(SchedulingJobs.JobId jobId) { await StartAsync(); await _scheduler!.Interrupt(ToQuartzKey(jobId)); }
    public async Task PauseAsync(SchedulingJobs.JobId jobId) { await StartAsync(); await _scheduler!.PauseJob(ToQuartzKey(jobId)); }
    public async Task ResumeAsync(SchedulingJobs.JobId jobId) { await StartAsync(); await _scheduler!.ResumeJob(ToQuartzKey(jobId)); }
    public async Task<bool> ExistsAsync(SchedulingJobs.JobId jobId) { await StartAsync(); return await _scheduler!.CheckExists(ToQuartzKey(jobId)); }

    public async Task<IEnumerable<JobInfo>> GetAllAsync(JobStatus status = JobStatus.All)
    {
        await StartAsync();
        var result = new List<JobInfo>();
        foreach (var key in await _scheduler!.GetJobKeys(GroupMatcher<JobKey>.AnyGroup()))
        {
            var detail = await _scheduler.GetJobDetail(key);
            if (detail == null) continue;
            var triggers = await _scheduler.GetTriggersOfJob(key);
            var primary = triggers.FirstOrDefault(t => t.Key.Name == "primary");
            var jobStatus = await ToJobStatusAsync(triggers, _scheduler);
            if (status != JobStatus.All && status != jobStatus) continue;
            result.Add(new(new(key.Name, key.Group, Guid.Parse(detail.JobDataMap.GetString("JobUuid")!)),
                (Type)detail.JobDataMap["BusinessJobType"], (Type)detail.JobDataMap["ArgType"],
                (primary as ICronTrigger)?.CronExpressionString, primary?.GetNextFireTimeUtc()?.LocalDateTime, jobStatus, null));
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

    private static JobKey ToQuartzKey(SchedulingJobs.JobId id)
        => id.Name != null ? new(id.Name, id.Group ?? SchedulerConstants.DefaultGroup) : new(id.Uuid.ToString());
}
