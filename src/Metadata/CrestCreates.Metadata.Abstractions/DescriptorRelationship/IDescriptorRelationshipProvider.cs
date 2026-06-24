namespace CrestCreates.Metadata.Abstractions.DescriptorRelationship;

public interface IDescriptorRelationshipProvider
{
    IReadOnlyList<DescriptorRelationship> GetRelationships(IDescriptor descriptor);
}
