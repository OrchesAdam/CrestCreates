using CrestCreates.Scheduling.Quartz.Jobs;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using SchedulingJobs = CrestCreates.Scheduling.Jobs;
using SchedulingServices = CrestCreates.Scheduling.Services;

namespace CrestCreates.Scheduling.Quartz.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQuartzScheduling(this IServiceCollection services)
    {
        services.AddSingleton<SchedulingServices.ISchedulerService, QuartzSchedulerService>();
        return services;
    }

    public static IServiceCollection AddQuartzJobs(this IServiceCollection services)
    {
        // This method is used by tests to register job types
        // Jobs should be registered with AddScoped<TJob>()
        return services;
    }
}
