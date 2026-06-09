namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Descriptors that self-describe their relationships.
/// Topology Engine can consume these directly without a separate provider.
/// </summary>
public interface IRelationshipAwareDescriptor
{
    IEnumerable<DescriptorRelationship> GetRelationships();
}
