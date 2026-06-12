namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorRelationshipProvider
{
    IReadOnlyList<DescriptorRelationship> GetRelationships(IDescriptor descriptor);
}
