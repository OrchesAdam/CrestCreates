using CrestCreates.Metadata.ContextPack.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Metadata.ContextPack;

public static class MetadataContextPackServiceCollectionExtensions
{
    public static IServiceCollection AddMetadataContextPack(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IMetadataContextPackBuilder, DefaultMetadataContextPackBuilder>();
        return services;
    }
}
