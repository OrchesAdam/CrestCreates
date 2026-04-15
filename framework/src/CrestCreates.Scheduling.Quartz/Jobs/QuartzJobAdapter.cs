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
    private readonly IJobFailureHandler _failureHandler;

    public QuartzJobAdapter(IServiceProvider serviceProvider, IJobFailureHandler failureHandler)
    {
        _serviceProvider = serviceProvider;
        _failureHandler = failureHandler;
    }

    async Task QuartzJob.Execute(IJobExecutionContext context)
    {
        using var scope = _serviceProvider.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<TJob>();

        var scheduledAt = context.ScheduledFireTimeUtc ?? DateTimeOffset.UtcNow;
        var argsJson = context.MergedJobDataMap.GetString("Args");
        var args = argsJson != null ? JsonSerializer.Deserialize<TArg>(argsJson) : default;
        var tenantId = context.MergedJobDataMap.GetString("TenantId");
        var organizationId = context.MergedJobDataMap.GetString("OrganizationId");
        var userId = context.MergedJobDataMap.GetString("UserId");
        var attemptNumber = context.MergedJobDataMap.ContainsKey("AttemptNumber")
            ? context.MergedJobDataMap.GetIntValue("AttemptNumber")
            : 1;

        var jobId = new JobId(context.JobDetail.Key.Name, context.JobDetail.Key.Group, Guid.Parse(context.FireInstanceId));
        var jobContext = new JobExecutionContext<TArg>(
            Args: args!,
            JobId: jobId,
            TenantId: string.IsNullOrEmpty(tenantId) ? null : Guid.Parse(tenantId),
            OrganizationId: string.IsNullOrEmpty(organizationId) ? null : Guid.Parse(organizationId),
            UserId: string.IsNullOrEmpty(userId) ? null : Guid.Parse(userId),
            ScheduledAt: scheduledAt,
            CancellationToken: context.CancellationToken
        );

        try
        {
            await job.ExecuteAsync(jobContext, context.CancellationToken);
        }
        catch (Exception ex)
        {
            var failureContext = new JobFailureContext(
                jobId,
                typeof(TJob),
                typeof(TArg),
                ex,
                jobContext.TenantId,
                jobContext.OrganizationId,
                jobContext.UserId,
                DateTimeOffset.UtcNow,
                args,
                attemptNumber
            );

            await _failureHandler.HandleAsync(failureContext, context.CancellationToken);

            if (_failureHandler.ShouldRetry(failureContext))
            {
                var delay = _failureHandler.GetNextRetryDelay(failureContext, attemptNumber);
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
