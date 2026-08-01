using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorBinding;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.Metadata.Runtime;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.HumanTask;

public static class HumanTaskServiceCollectionExtensions
{
    public static IServiceCollection AddHumanTaskRuntime(this IServiceCollection services)
    {
        services.TryAddScoped<IHumanTaskRuntime, DefaultHumanTaskRuntime>();
        services.TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>();

        // HumanTask Registry (for binding status contributors)
        services.TryAddSingleton<IHumanTaskRegistry, HumanTaskRegistry>();
        services.TryAddSingleton<IRuntimeDescriptorPinResolver<HumanTaskDescriptor>>(sp =>
            new RuntimeDescriptorPinResolver<HumanTaskDescriptor>(
                sp.GetRequiredService<IHumanTaskRegistry>(),
                sp.GetRequiredService<IDescriptorStableHashBuilder>(),
                "humantask",
                DescriptorKind.HumanTask));
        services.TryAddSingleton<IRegistryValidationEngine<HumanTaskDescriptor>,
            RegistryValidationEngine<HumanTaskDescriptor>>();

        // Binding Status Contributor
        services.AddSingleton<IDescriptorBindingStatusContributor, HumanTaskBindingStatusContributor>();

        // Relationship Extractor
        services.AddSingleton<IDescriptorRelationshipExtractor, HumanTaskRelationshipExtractor>();

        return services;
    }
}
