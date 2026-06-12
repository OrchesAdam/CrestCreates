using CrestCreates.Metadata.Abstractions;
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
}
