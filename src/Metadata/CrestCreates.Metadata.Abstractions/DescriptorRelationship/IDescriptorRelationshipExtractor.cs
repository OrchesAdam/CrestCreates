namespace CrestCreates.Metadata.Abstractions.DescriptorRelationship;

public interface IDescriptorRelationshipExtractor
{
    DescriptorKind SupportedKind { get; }
    Type DescriptorType { get; }
    IReadOnlyList<DescriptorRelationship> Extract(IDescriptor descriptor);
}
