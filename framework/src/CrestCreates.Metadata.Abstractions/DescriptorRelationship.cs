namespace CrestCreates.Metadata.Abstractions;

public readonly record struct DescriptorRef(
    string Namespace,
    string Id,
    int? Version = null) : IDescriptorRef
{
    public string FullId => $"{Namespace}.{Id}";
}

public sealed record DescriptorRelationship(
    DescriptorRef From,
    DescriptorRef To,
    RelationshipKind Kind,
    string? Role = null,
    string? SourcePath = null,
    RelationshipStrength Strength = RelationshipStrength.Strong,
    bool IsRuntimeBinding = false);

public enum RelationshipKind
{
    Produces,
    Consumes,
    DependsOn,
    References,
    Uses,
    Triggers
}
