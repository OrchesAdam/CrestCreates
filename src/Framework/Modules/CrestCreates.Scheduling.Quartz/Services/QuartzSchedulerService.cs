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
    : ISchedulerService, IAsyncDisposable, IDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _registration = new(1, 1);
    private global::Quartz.IScheduler? _scheduler;
    private bool _stopped;
    private Task? _shutdownTask;

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
        TaskCompletionSource? completion = null;
        global::Quartz.IScheduler? scheduler = null;
        Task shutdown;
        // Context-free continuations keep a blocking Dispose safe under a single-threaded
        // SynchronizationContext: shutdown completion must not need the blocked caller thread.
        await _lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _stopped = true;
            if (_shutdownTask == null)
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _shutdownTask = completion.Task;
                scheduler = _scheduler;
            }
            shutdown = _shutdownTask;
        }
        finally { _lifecycle.Release(); }

        // Shutdown can synchronously call listeners that reenter this service.
        if (completion != null)
        {
            try
            {
                if (scheduler != null)
                    await scheduler.Shutdown(true, CancellationToken.None).ConfigureAwait(false);
                logger.LogInformation("Quartz scheduler stopped");
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }
        // All Stop/Dispose callers share full cleanup, not Quartz's early IsShutdown flag.
        await shutdown.ConfigureAwait(false);
    }

    /// <summary>Blocks until shutdown and running job cleanup complete. Prefer DisposeAsync.</summary>
    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

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
        // Serialize check → history → insert so a concurrent duplicate cannot write a ghost record
        // and history remains ordered before an immediate fire.
        await _registration.WaitAsync();
        var paused = false;
        try
        {
            if (metadata?.Name != null && await _scheduler!.CheckExists(key))
                throw new InvalidOperationException($"Job '{key.Name}' in group '{key.Group}' is already registered.");
            // Pausing the group before insertion prevents an immediate disabled job from racing execution.
            if (metadata?.Enabled == false)
            {
                await _scheduler!.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals(uuid.ToString()));
                paused = true;
            }
            await using var scope = serviceProvider.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetService<IJobExecutionHandler>();
            if (handler != null)
                await handler.OnJobScheduledAsync(new(uuid, typeof(TJob), typeof(TArg), tenantId, organizationId, userId, json, time));
            await _scheduler!.ScheduleJob(job, trigger);
        }
        catch
        {
            if (paused)
            {
                try { await _scheduler!.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals(uuid.ToString())); }
                catch (Exception cleanup) { logger.LogWarning(cleanup, "Failed to release the pre-insertion pause for job {JobKey}.", key); }
            }
            throw;
        }
        finally { _registration.Release(); }
        return id;
    }

    public async Task DeleteAsync(SchedulingJobs.JobId jobId) { await StartAsync(); await _scheduler!.DeleteJob(ToQuartzKey(jobId)); }
    public async Task CancelAsync(SchedulingJobs.JobId jobId) { await StartAsync(); await _scheduler!.Interrupt(ToQuartzKey(jobId)); }
    public async Task PauseAsync(SchedulingJobs.JobId jobId) { await StartAsync(); await _scheduler!.PauseJob(ToQuartzKey(jobId)); }
    public async Task ResumeAsync(SchedulingJobs.JobId jobId)
    {
        await StartAsync();
        var key = ToQuartzKey(jobId);
        var detail = await _scheduler!.GetJobDetail(key);
        if (detail == null) return;
        // Named control tokens may lack the UUID that owns the trigger-group pause.
        var group = detail.JobDataMap.GetString("JobUuid")!;
        await _scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals(group));
        await _scheduler.ResumeJob(key);
    }
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
