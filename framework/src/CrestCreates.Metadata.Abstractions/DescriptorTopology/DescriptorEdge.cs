using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed record DescriptorEdge
{
    public required int Index { get; init; }
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public required bool IsRuntimeBinding { get; init; }
}
