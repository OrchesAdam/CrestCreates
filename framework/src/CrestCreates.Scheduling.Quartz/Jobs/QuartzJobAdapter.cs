using System.Text.Json;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using QuartzJob = Quartz.IJob;

namespace CrestCreates.Scheduling.Quartz.Jobs;

internal class QuartzJobAdapter<TJob, TArg> : QuartzJob
    where TJob : CrestCreates.Scheduling.Jobs.IJob<TArg>
    where TArg : IJobArgs
{
    private readonly IServiceProvider _serviceProvider;

    public QuartzJobAdapter(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    async Task QuartzJob.Execute(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<TJob>();
        var handler = scope.ServiceProvider.GetRequiredService<IJobExecutionHandler>();

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
        var jobId = new JobId(context.JobDetail.Key.Name, context.JobDetail.Key.Group, Guid.Parse(context.FireInstanceId));

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
            jobId.Uuid,
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
            await job.ExecuteAsync(jobContext, context.CancellationToken);

            var finishedAt = DateTimeOffset.UtcNow;
            await handler.OnJobSucceededAsync(new JobSucceededContext(
                jobId.Uuid,
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
            var failureContext = new JobFailureContext(
                jobId,
                typeof(TJob),
                typeof(TArg),
                ex,
                tenantId,
                organizationId,
                userId,
                DateTimeOffset.UtcNow,
                args,
                attemptNumber
            );

            await handler.HandleAsync(failureContext, context.CancellationToken);

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
