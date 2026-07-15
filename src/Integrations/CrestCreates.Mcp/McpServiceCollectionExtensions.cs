using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema;
using CrestCreates.Schema.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Mcp;

public static class McpServiceCollectionExtensions
{
    public static IServiceCollection AddCrestMcpToolProjection(
        this IServiceCollection services,
        Action<McpJsonOptions>? configureJson = null)
    {
        var json = new McpJsonOptions();
        configureJson?.Invoke(json);
        services.TryAddSingleton(json);
        services.TryAddSingleton<IRegistryValidator<CrestCreates.Metadata.Mcp.McpToolDescriptor>, McpToolDescriptorValidator>();
        services.TryAddSingleton<IRegistryValidationEngine<CrestCreates.Metadata.Mcp.McpToolDescriptor>,
            RegistryValidationEngine<CrestCreates.Metadata.Mcp.McpToolDescriptor>>();
        services.TryAddSingleton<McpToolRegistry>();
        services.TryAddSingleton<IMcpToolRegistry>(provider => provider.GetRequiredService<McpToolRegistry>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDescriptorRelationshipExtractor, McpToolRelationshipExtractor>());
        services.TryAddSingleton<IMcpJsonSchemaProjector, McpJsonSchemaProjector>();
        services.TryAddSingleton<McpToolSchemaParityValidator>();
        services.TryAddSingleton<McpToolRuntimeSnapshotBuilder>();
        services.TryAddSingleton(provider => provider.GetRequiredService<McpToolRuntimeSnapshotBuilder>().Build());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, McpToolProjectionStartupValidator>());
        services.TryAddSingleton<IMcpToolExposurePolicy, DefaultMcpToolExposurePolicy>();
        services.TryAddSingleton<IMcpIdempotencyKeyBuilder, DefaultMcpIdempotencyKeyBuilder>();
        services.TryAddSingleton<ISchemaValidator, SchemaValidator>();
        services.TryAddSingleton<McpToolResultMapper>();
        services.TryAddSingleton<IMcpToolDiscoveryService, McpToolDiscoveryService>();
        services.TryAddSingleton<IMcpToolInvoker, McpToolInvoker>();
        return services;
    }
}
