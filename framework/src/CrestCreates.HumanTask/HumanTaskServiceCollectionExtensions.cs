using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.HumanTask;

public static class HumanTaskServiceCollectionExtensions
{
    public static IServiceCollection AddHumanTaskRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IHumanTaskInstanceStore, InMemoryHumanTaskInstanceStore>();
        services.TryAddScoped<IHumanTaskRuntime, DefaultHumanTaskRuntime>();
        services.TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>();

        // HumanTask Registry (for binding status contributors)
        services.TryAddSingleton<IHumanTaskRegistry, HumanTaskRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<HumanTaskDescriptor>,
            RegistryValidationEngine<HumanTaskDescriptor>>();

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, HumanTaskBindingStatusContributor>();

        return services;
    }
}
