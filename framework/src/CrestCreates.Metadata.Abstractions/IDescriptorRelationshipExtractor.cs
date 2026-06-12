namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorRelationshipExtractor
{
    DescriptorKind SupportedKind { get; }
    Type DescriptorType { get; }
    IReadOnlyList<DescriptorRelationship> Extract(IDescriptor descriptor);
}
