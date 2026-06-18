using System.Text.Json;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using QuartzJob = Quartz.IJob;

namespace CrestCreates.Scheduling.Quartz.Jobs;

internal class QuartzJobAdapter<TJob, TArg> : QuartzJob
    where TJob : CrestCreates.Scheduling.Jobs.IJob<TArg>
    where TArg : IJobArgs
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QuartzJobAdapter<TJob, TArg>>? _logger;

    public QuartzJobAdapter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<QuartzJobAdapter<TJob, TArg>>>();
    }

    async Task QuartzJob.Execute(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<TJob>();
        var handler = scope.ServiceProvider.GetService<IJobExecutionHandler>();

        // If no handler is registered, just execute the job without tracking
        if (handler == null)
        {
            var fireInstanceIdNoHandler = Guid.TryParse(context.FireInstanceId, out var parsedId)
                ? parsedId
                : Guid.NewGuid();
            await job.ExecuteAsync(new JobExecutionContext<TArg>(
                Args: default!,
                JobId: new JobId(context.JobDetail.Key.Name, context.JobDetail.Key.Group, fireInstanceIdNoHandler),
                TenantId: null,
                OrganizationId: null,
                UserId: null,
                ScheduledAt: context.ScheduledFireTimeUtc ?? DateTimeOffset.UtcNow,
                CancellationToken: context.CancellationToken
            ), context.CancellationToken);
            return;
        }

        // Prepare job context
        var scheduledAt = context.ScheduledFireTimeUtc ?? DateTimeOffset.UtcNow;
        var startedAt = DateTimeOffset.UtcNow;
        var argsJson = context.MergedJobDataMap.GetString("Args");
        var args = argsJson != null ? JsonSerializer.Deserialize<TArg>(argsJson) : default;
        var tenantIdStr = context.MergedJobDataMap.GetString("TenantId");
        var organizationIdStr = context.MergedJobDataMap.GetString("OrganizationId");
        var userIdStr = context.MergedJobDataMap.GetString("UserId");
        var attemptNumber = context.MergedJobDataMap.ContainsKey("AttemptNumber")
            ? context.MergedJobDataMap.GetIntValue("AttemptNumber")
            : 1;

        var tenantId = string.IsNullOrEmpty(tenantIdStr) ? (Guid?)null : Guid.Parse(tenantIdStr);
        var organizationId = string.IsNullOrEmpty(organizationIdStr) ? (Guid?)null : Guid.Parse(organizationIdStr);
        var userId = string.IsNullOrEmpty(userIdStr) ? (Guid?)null : Guid.Parse(userIdStr);

        // Use JobKey.Name as JobUuid (matches the UUID from ExecuteNowAsync/ScheduleAsync)
        var jobUuid = Guid.Parse(context.JobDetail.Key.Name);
        var fireInstanceId = Guid.TryParse(context.FireInstanceId, out var parsedFireInstanceId)
            ? parsedFireInstanceId
            : Guid.NewGuid();
        var jobId = new JobId(context.JobDetail.Key.Name, context.JobDetail.Key.Group, fireInstanceId);

        var jobContext = new JobExecutionContext<TArg>(
            Args: args!,
            JobId: jobId,
            TenantId: tenantId,
            OrganizationId: organizationId,
            UserId: userId,
            ScheduledAt: scheduledAt,
            CancellationToken: context.CancellationToken
        );

        // Record job started
        await handler.OnJobStartedAsync(new JobStartedContext(
            jobUuid,
            typeof(TJob),
            typeof(TArg),
            tenantId,
            organizationId,
            userId,
            argsJson,
            attemptNumber,
            startedAt
        ), context.CancellationToken);

        try
        {
            // Execute job
            await job.ExecuteAsync(jobContext, context.CancellationToken);

            var finishedAt = DateTimeOffset.UtcNow;
            await handler.OnJobSucceededAsync(new JobSucceededContext(
                jobUuid,
                typeof(TJob),
                typeof(TArg),
                tenantId,
                organizationId,
                userId,
                argsJson,
                attemptNumber,
                startedAt,
                finishedAt,
                finishedAt - startedAt
            ), context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Job {JobType} execution failed", typeof(TJob).Name);

            // Create a JobId with the correct jobUuid for failure tracking
            var failureJobId = new JobId(context.JobDetail.Key.Name, context.JobDetail.Key.Group, jobUuid);

            // Record failure
            await handler.HandleAsync(new JobFailureContext(
                failureJobId,
                typeof(TJob),
                typeof(TArg),
                ex,
                tenantId,
                organizationId,
                userId,
                DateTimeOffset.UtcNow,
                args,
                attemptNumber
            ), context.CancellationToken);

            var failureContext = new JobFailureContext(
                failureJobId,
                typeof(TJob),
                typeof(TArg),
                ex,
                tenantId,
                organizationId,
                userId,
                DateTimeOffset.UtcNow,
                args,
                attemptNumber);

            if (handler.ShouldRetry(failureContext))
            {
                var delay = handler.GetNextRetryDelay(failureContext, attemptNumber);
                context.MergedJobDataMap.Put("AttemptNumber", attemptNumber + 1);

                var trigger = TriggerBuilder.Create()
                    .StartAt(DateTimeOffset.UtcNow.Add(delay ?? TimeSpan.Zero))
                    .Build();

                await context.Scheduler.RescheduleJob(context.Trigger.Key, trigger);
            }

            throw;
        }
    }
}
