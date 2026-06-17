using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackSummary
{
    public required int TotalDescriptorCount { get; init; }
    public required IReadOnlyDictionary<DescriptorKind, int> DescriptorCountsByKind { get; init; }
    public required int TotalRelationshipCount { get; init; }
    public required IReadOnlyDictionary<RelationshipKind, int> RelationshipCountsByKind { get; init; }
    public required IReadOnlyList<DescriptorRef> FocusRefs { get; init; }
    public required bool WasTruncated { get; init; }
    public required int? TruncatedAtCount { get; init; }
    public required int TraversalDepthReached { get; init; }
}
