using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Scheduling.Jobs;
using CrestCreates.Scheduling.Quartz.Jobs;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Scheduling.Quartz.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddQuartzScheduling(this IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddSingleton<ISchedulerService, QuartzSchedulerService>();
        services.TryAddScoped<IJobFailureHandler, DefaultJobFailureHandler>();
        return services.AddQuartzJobs();
    }

    public static IServiceCollection AddQuartzJobs(this IServiceCollection services)
    {
        services.TryAddTransient(typeof(QuartzJobAdapter<,>));
        return services;
    }

    public static IServiceCollection AddQuartzJobArgs<TArg>(this IServiceCollection services, JsonTypeInfo<TArg> typeInfo)
        where TArg : IJobArgs
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        services.AddSingleton(typeInfo);
        return services;
    }
}

internal static class JobArgsJson<TArg> where TArg : IJobArgs
{
    public static string Encode(IServiceProvider services, TArg args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return typeof(TArg) == typeof(NoArgs) ? "{}" : JsonSerializer.Serialize(args, services.GetRequiredService<JsonTypeInfo<TArg>>());
    }

    public static TArg Decode(IServiceProvider services, string json)
        => typeof(TArg) == typeof(NoArgs) ? (TArg)(object)new NoArgs()
            : JsonSerializer.Deserialize(json, services.GetRequiredService<JsonTypeInfo<TArg>>())
                ?? throw new JsonException("Job args must not be null.");
}
