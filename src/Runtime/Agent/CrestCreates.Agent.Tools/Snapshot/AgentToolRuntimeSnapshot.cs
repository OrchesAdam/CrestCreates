using System.Collections.Frozen;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Agent.Tools;

public sealed record AgentToolRuntimeBinding(
    AgentToolBindingContract Contract,
    JsonTypeInfo? InputTypeInfo,
    JsonTypeInfo? OutputTypeInfo);

public sealed record AgentToolRuntimeEntry(
    AgentCapabilityToolDescriptor Descriptor,
    CapabilityDescriptor Capability,
    SchemaDescriptor? InputSchema,
    SchemaDescriptor? OutputSchema,
    AgentToolRuntimeBinding Binding,
    AgentToolDiscoveryContract DiscoveryContract,
    FrozenSet<string> AllowedAgentRoles,
    CapabilityRiskLevel EffectiveRisk,
    AgentToolSideEffectKind EffectiveSideEffectKind,
    AgentToolEffectiveGovernance Governance,
    string ToolContractHash,
    string CapabilityContractHash,
    string? InputSchemaContractHash,
    string? OutputSchemaContractHash);

public sealed class AgentToolRuntimeSnapshot
{
    public AgentToolRuntimeSnapshot(FrozenDictionary<string, AgentToolRuntimeEntry> entries)
        => Entries = entries ?? throw new ArgumentNullException(nameof(entries));

    public FrozenDictionary<string, AgentToolRuntimeEntry> Entries { get; }

    public AgentToolRuntimeEntry? Find(string toolName)
        => Entries.TryGetValue(toolName, out var entry) ? entry : null;
}
