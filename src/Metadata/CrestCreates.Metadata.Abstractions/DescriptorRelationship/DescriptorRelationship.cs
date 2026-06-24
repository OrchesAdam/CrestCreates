using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorRelationship;

public sealed record DescriptorRelationship(
    DescriptorRef From,
    DescriptorRef To,
    RelationshipKind Kind,
    string? Role = null,
    string? SourcePath = null,
    RelationshipStrength Strength = RelationshipStrength.Strong,
    bool IsRuntimeBinding = false);
