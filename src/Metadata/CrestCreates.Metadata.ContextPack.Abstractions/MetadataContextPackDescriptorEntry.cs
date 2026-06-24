using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackDescriptorEntry
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorState State { get; init; }
    public DescriptorStableHashes? Hashes { get; init; }
    public MetadataContextPackGovernanceEntry? Governance { get; init; }
    public bool IsFocus { get; init; }
}
