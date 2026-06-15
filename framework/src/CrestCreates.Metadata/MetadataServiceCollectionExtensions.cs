using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.DescriptorCompatibility;
using CrestCreates.Metadata.DescriptorLifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Metadata;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddBindingStatusKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorRuntimeBindingStatusProvider,
            DefaultDescriptorRuntimeBindingStatusProvider>();
        return services;
    }

    public static IServiceCollection AddRelationshipKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorRelationshipProvider,
            DefaultDescriptorRelationshipProvider>();

        services.AddSingleton<IDescriptorRelationshipExtractor, SchemaRelationshipExtractor>();

        return services;
    }

    public static IServiceCollection AddTopologyKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorTopologyBuilder, DescriptorTopologyBuilder>();
        return services;
    }

    public static IServiceCollection AddDescriptorImpactAnalysis(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorImpactAnalyzer, DescriptorImpactAnalyzer>();
        services.TryAddSingleton<IDescriptorChangeSetBuilder, DescriptorChangeSetBuilder>();
        return services;
    }

    public static IServiceCollection AddDescriptorCompatibilityAnalysis(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorCompatibilityAnalyzer, DescriptorCompatibilityAnalyzer>();
        return services;
    }

    public static IServiceCollection AddDescriptorLifecycleGovernance(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IDescriptorLifecycleGovernanceService,
            DefaultDescriptorLifecycleGovernanceService>();
        return services;
    }
}
