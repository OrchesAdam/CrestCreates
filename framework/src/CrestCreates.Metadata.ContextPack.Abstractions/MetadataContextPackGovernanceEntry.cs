using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackGovernanceEntry
{
    public required DescriptorState State { get; init; }
    public bool RequiresReview { get; init; }
}
