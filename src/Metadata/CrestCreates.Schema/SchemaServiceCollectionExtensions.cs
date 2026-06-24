using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Schema;

public static class SchemaServiceCollectionExtensions
{
    public static IServiceCollection AddSchemaKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<ISchemaRegistry, SchemaRegistry>();
        services.TryAddSingleton<IRegistryValidationEngine<SchemaDescriptor>,
            RegistryValidationEngine<SchemaDescriptor>>();
        return services;
    }
}
