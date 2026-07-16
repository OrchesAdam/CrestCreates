using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolSchemaResolver
{
    private readonly ISchemaRegistry _schemas;

    public AgentToolSchemaResolver(ISchemaRegistry schemas)
        => _schemas = schemas;

    public SchemaDescriptor? Resolve(VersionedDescriptorRef<SchemaDescriptor>? reference)
    {
        if (reference is null)
            return null;

        if (reference.Value.SelectionMode != VersionSelectionMode.Exact
            || reference.Value.Version <= 0)
        {
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.SchemaReferenceNotExact,
                "Capability Schema reference must select an exact positive version.");
        }

        return _schemas.GetByVersion(reference.Value.Id, reference.Value.Version)
            ?? throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.SchemaReferenceNotExact,
                "Capability Schema reference could not be resolved exactly.");
    }
}
