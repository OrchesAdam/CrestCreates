using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using CrestCreates.Scheduling.Quartz.Services;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace CrestCreates.Scheduling.Quartz.Jobs;

[DisallowConcurrentExecution]
internal class QuartzJobAdapter<TJob, TArg>(IServiceProvider services) : global::Quartz.IJob
    where TJob : CrestCreates.Scheduling.Jobs.IJob<TArg>
    where TArg : IJobArgs
{
    public async Task Execute(IJobExecutionContext context)
    {
        var map = context.JobDetail.JobDataMap;
        var json = map.GetString("Args") ?? throw new InvalidOperationException("Missing stored job args.");
        var args = JobArgsJson<TArg>.Decode(services, json);
        var id = new JobId(context.JobDetail.Key.Name, context.JobDetail.Key.Group, Guid.Parse(map.GetString("JobUuid")!));
        var tenantId = ReadId(map, "TenantId");
        var orgId = ReadId(map, "OrganizationId");
        var userId = ReadId(map, "UserId");
        // The primary cron trigger always starts at 1. Only a retry trigger carries the next attempt.
        var attempt = context.Trigger.JobDataMap.ContainsKey("AttemptNumber") ? context.Trigger.JobDataMap.GetInt("AttemptNumber") : 1;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        if (map.ContainsKey("Timeout")) deadline.CancelAfter((TimeSpan)map["Timeout"]);
        var ct = deadline.Token;
        var scheduledAt = context.Trigger.JobDataMap.ContainsKey("OriginalScheduledAt")
            ? (DateTimeOffset)context.Trigger.JobDataMap["OriginalScheduledAt"]
            : context.ScheduledFireTimeUtc ?? DateTimeOffset.UtcNow;
        var currentTenant = services.GetService<ICurrentTenant>();
        ITenantInfo? tenant = null;
        if (tenantId.HasValue)
        {
            if (currentTenant == null)
                throw new InvalidOperationException("Tenant-targeted jobs require ICurrentTenant.");
            tenant = await services.GetRequiredService<ITenantProvider>().GetTenantAsync(tenantId.Value.ToString(), ct);
            if (tenant == null || !Guid.TryParse(tenant.Id, out var resolved) || resolved != tenantId.Value)
                throw new InvalidOperationException("Scheduled tenant could not be resolved to its authoritative Id.");
        }
        using var tenantFrame = currentTenant?.Change(tenant);
        var handler = services.GetService<IJobExecutionHandler>();
        var failureHandler = (IJobFailureHandler?)handler ?? services.GetService<IJobFailureHandler>();
        var job = services.GetRequiredService<TJob>();
        var started = DateTimeOffset.UtcNow;
        if (handler != null)
            await handler.OnJobStartedAsync(new(id.Uuid, typeof(TJob), typeof(TArg), tenantId, orgId, userId, json, attempt, started), ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            await job.ExecuteAsync(new(args, id, tenantId, orgId, userId, scheduledAt, ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (handler != null)
                await handler.OnJobCancelledAsync(new(id.Uuid, typeof(TJob), typeof(TArg), tenantId, orgId, userId, json, attempt, DateTimeOffset.UtcNow), CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            var failure = new JobFailureContext(id, typeof(TJob), typeof(TArg), ex, tenantId, orgId, userId,
                DateTimeOffset.UtcNow, args, attempt) { ArgsJson = json };
            if (failureHandler != null) await failureHandler.HandleAsync(failure, CancellationToken.None);
            // Cancellation and cooperative deadlines never initiate retry, including jobs throwing another exception on cancellation.
            if (ct.IsCancellationRequested || ex is OperationCanceledException) throw;
            var options = map.ContainsKey("Retry") ? (JobRetryOptions)map["Retry"] : null;
            var retry = options != null ? attempt <= options.MaxRetries : failureHandler?.ShouldRetry(failure) == true;
            if (retry)
            {
                var delay = options?.GetDelay(attempt) ?? failureHandler?.GetNextRetryDelay(failure, attempt) ?? TimeSpan.FromSeconds(1);
                if (delay <= TimeSpan.Zero) delay = TimeSpan.FromMilliseconds(100);
                if (delay > TimeSpan.FromDays(1)) delay = TimeSpan.FromDays(1);
                var trigger = TriggerBuilder.Create().WithIdentity(Guid.NewGuid().ToString(), context.Trigger.Key.Group)
                    .ForJob(context.JobDetail.Key).UsingJobData(new JobDataMap
                    {
                        ["AttemptNumber"] = checked(attempt + 1), ["OriginalScheduledAt"] = scheduledAt
                    })
                    .StartAt(DateTimeOffset.UtcNow.Add(delay)).Build();
                await context.Scheduler.ScheduleJob(trigger, ct);
            }
            throw;
        }
        // Completion notification is outside the business catch: history failure cannot re-execute successful work.
        if (handler != null)
        {
            var finished = DateTimeOffset.UtcNow;
            await handler.OnJobSucceededAsync(new(id.Uuid, typeof(TJob), typeof(TArg), tenantId, orgId, userId,
                json, attempt, started, finished, finished - started), CancellationToken.None);
        }
    }

    private static Guid? ReadId(JobDataMap map, string key)
        => string.IsNullOrEmpty(map.GetString(key)) ? null : Guid.Parse(map.GetString(key)!);
}
