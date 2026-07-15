using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

internal sealed class McpToolProjectionStartupValidator : IHostedService
{
    private readonly ISchemaRegistry _schemaRegistry;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly McpToolRegistry _registry;
    private readonly McpToolRuntimeSnapshotBuilder _snapshotBuilder;
    private readonly McpToolRuntimeSnapshotProvider _snapshotProvider;

    public McpToolProjectionStartupValidator(
        ISchemaRegistry schemaRegistry,
        ICapabilityRegistry capabilityRegistry,
        McpToolRegistry registry,
        McpToolRuntimeSnapshotBuilder snapshotBuilder,
        McpToolRuntimeSnapshotProvider snapshotProvider)
    {
        _schemaRegistry = schemaRegistry;
        _capabilityRegistry = capabilityRegistry;
        _registry = registry;
        _snapshotBuilder = snapshotBuilder;
        _snapshotProvider = snapshotProvider;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // MCP discovery must capture resolved, immutable contracts. Build the
        // authoritative dependency registries before publishing the MCP snapshot;
        // RegistryBase.Build is idempotent when an earlier bootstrap already ran.
        _schemaRegistry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        _capabilityRegistry.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());
        _registry.Build(DescriptorProviderRegistry.GetProviders<McpToolDescriptor>());
        _snapshotProvider.Publish(_snapshotBuilder.Build());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
