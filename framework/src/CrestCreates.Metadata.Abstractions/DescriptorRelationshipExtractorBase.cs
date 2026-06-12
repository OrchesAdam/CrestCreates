namespace CrestCreates.Metadata.Abstractions;

public abstract class DescriptorRelationshipExtractorBase<TDescriptor>
    : IDescriptorRelationshipExtractor
    where TDescriptor : class, IDescriptor
{
    public abstract DescriptorKind SupportedKind { get; }
    public Type DescriptorType => typeof(TDescriptor);

    public IReadOnlyList<DescriptorRelationship> Extract(IDescriptor descriptor)
    {
        if (descriptor is TDescriptor typed)
            return Extract(typed);
        return Array.Empty<DescriptorRelationship>();
    }

    protected abstract IReadOnlyList<DescriptorRelationship> Extract(TDescriptor descriptor);
}
