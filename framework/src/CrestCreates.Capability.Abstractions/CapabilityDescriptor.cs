using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityDescriptor : IVersionedDescriptor
{
    public string Namespace => "capability";
    public DescriptorKind Kind => DescriptorKind.Capability;
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; } = DescriptorState.Active;
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }

    public int Version { get; init; }
    public CapabilityKind CapabilityKind { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor> InputSchema { get; init; }
    public VersionedDescriptorRef<SchemaDescriptor> OutputSchema { get; init; }
    public string Permission { get; init; } = string.Empty;
    public CapabilityRiskLevel RiskLevel { get; init; } = CapabilityRiskLevel.Medium;
    public IReadOnlyList<string> SemanticTags { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
}
