using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using Relationship = CrestCreates.Metadata.Abstractions.DescriptorRelationship.DescriptorRelationship;

namespace CrestCreates.Metadata.DescriptorRelationship;

public sealed class DefaultDescriptorRelationshipProvider : IDescriptorRelationshipProvider
{
    private readonly IReadOnlyList<IDescriptorRelationshipExtractor> _extractors;

    public DefaultDescriptorRelationshipProvider(
        IEnumerable<IDescriptorRelationshipExtractor> extractors)
    {
        _extractors = extractors.ToList();
    }

    public IReadOnlyList<Relationship> GetRelationships(IDescriptor descriptor)
    {
        foreach (var extractor in _extractors)
        {
            if (extractor.DescriptorType.IsInstanceOfType(descriptor))
                return extractor.Extract(descriptor);
        }
        return Array.Empty<Relationship>();
    }
}
