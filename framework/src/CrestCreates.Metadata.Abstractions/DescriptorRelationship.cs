namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Non-generic descriptor reference with namespace and id.
/// Used in relationship declarations. The generic DescriptorRef{TDescriptor} remains for typed refs.
/// </summary>
public readonly record struct DescriptorRef(string Namespace, string Id);

public sealed record DescriptorRelationship(
    DescriptorRef From,
    DescriptorRef To,
    RelationshipKind Kind);

public enum RelationshipKind
{
    Produces,
    Consumes,
    DependsOn,
    References
}
