using CrestCreates.HumanTask.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.HumanTask;

public static class HumanTaskServiceCollectionExtensions
{
    public static IServiceCollection AddHumanTaskRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IHumanTaskInstanceStore, InMemoryHumanTaskInstanceStore>();
        services.TryAddScoped<IHumanTaskRuntime, DefaultHumanTaskRuntime>();
        return services;
    }
}
