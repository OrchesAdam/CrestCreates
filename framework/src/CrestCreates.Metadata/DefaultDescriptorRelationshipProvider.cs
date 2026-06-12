using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DefaultDescriptorRelationshipProvider : IDescriptorRelationshipProvider
{
    private readonly IReadOnlyList<IDescriptorRelationshipExtractor> _extractors;

    public DefaultDescriptorRelationshipProvider(
        IEnumerable<IDescriptorRelationshipExtractor> extractors)
    {
        _extractors = extractors.ToList();
    }

    public IReadOnlyList<DescriptorRelationship> GetRelationships(IDescriptor descriptor)
    {
        foreach (var extractor in _extractors)
        {
            if (extractor.DescriptorType.IsInstanceOfType(descriptor))
                return extractor.Extract(descriptor);
        }
        return Array.Empty<DescriptorRelationship>();
    }
}
