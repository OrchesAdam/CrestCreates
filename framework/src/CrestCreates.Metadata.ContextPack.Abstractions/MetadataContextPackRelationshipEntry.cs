using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public sealed record MetadataContextPackRelationshipEntry
{
    public required DescriptorRef From { get; init; }
    public required DescriptorRef To { get; init; }
    public required RelationshipKind Kind { get; init; }
    public string? Role { get; init; }
    public string? SourcePath { get; init; }
    public required RelationshipStrength Strength { get; init; }
    public bool IsRuntimeBinding { get; init; }
}
