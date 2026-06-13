using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorImpactPathSegment
{
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public required bool IsRuntimeBinding { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
}
