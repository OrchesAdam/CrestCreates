using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using Quartz.Spi;

namespace CrestCreates.Scheduling.Quartz.Jobs;

public class DependencyInjectionJobFactory : IJobFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DependencyInjectionJobFactory>? _logger;

    public DependencyInjectionJobFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<DependencyInjectionJobFactory>>();
    }

    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var jobType = bundle.JobDetail.JobType;
        _logger?.LogDebug("Creating job of type {JobType}", jobType.FullName);

        // Create a scope for each job execution
        var scope = _serviceProvider.CreateScope();
        try
        {
            // For generic types like QuartzJobAdapter<TJob, TArg>, we need to get the service
            var job = scope.ServiceProvider.GetRequiredService(jobType) as IJob;
            if (job == null)
            {
                _logger?.LogError("Job type {JobType} does not implement IJob", jobType.FullName);
                scope.Dispose();
                throw new InvalidOperationException($"Job type {jobType.FullName} does not implement IJob");
            }
            _logger?.LogDebug("Successfully created job of type {JobType}", jobType.FullName);
            return job;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create job of type {JobType}", jobType.FullName);
            scope.Dispose();
            throw;
        }
    }

    public void ReturnJob(IJob job)
    {
        // Jobs are created from scoped services, disposal handled by scope
    }
}