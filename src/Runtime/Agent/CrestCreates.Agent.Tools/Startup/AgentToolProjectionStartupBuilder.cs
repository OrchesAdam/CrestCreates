using CrestCreates.Metadata;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolProjectionStartupBuilder
{
    private readonly object _buildLock = new();
    private readonly ISchemaRegistry _schemas;
    private readonly ICapabilityRegistry _capabilities;
    private readonly AgentToolRegistry _tools;
    private readonly AgentToolRuntimeSnapshotBuilder _snapshotBuilder;
    private readonly AgentToolRuntimeSnapshotProvider _snapshotProvider;

    public AgentToolProjectionStartupBuilder(
        ISchemaRegistry schemas,
        ICapabilityRegistry capabilities,
        AgentToolRegistry tools,
        AgentToolRuntimeSnapshotBuilder snapshotBuilder,
        AgentToolRuntimeSnapshotProvider snapshotProvider)
    {
        _schemas = schemas;
        _capabilities = capabilities;
        _tools = tools;
        _snapshotBuilder = snapshotBuilder;
        _snapshotProvider = snapshotProvider;
    }

    public AgentToolRuntimeSnapshot BuildAndPublish()
    {
        lock (_buildLock)
        {
            if (_snapshotProvider.IsPublished || _snapshotProvider.IsFailed)
                return _snapshotProvider.GetRequired();

            try
            {
                _schemas.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
                _capabilities.Build(DescriptorProviderRegistry.GetProviders<CapabilityDescriptor>());
                _tools.Build(DescriptorProviderRegistry.GetProviders<AgentCapabilityToolDescriptor>());
                var snapshot = _snapshotBuilder.Build();
                _snapshotProvider.Publish(snapshot);
                return snapshot;
            }
            catch (Exception exception)
            {
                _snapshotProvider.MarkFailed(exception);
                throw;
            }
        }
    }
}
