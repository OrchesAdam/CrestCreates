namespace CrestCreates.Metadata.Abstractions;

public interface IRegistryIndexBuilder<TDescriptor, TIndex>
    where TDescriptor : IDescriptor
    where TIndex : IRegistryIndex
{
    TIndex BuildIndex(IReadOnlyList<TDescriptor> descriptors);
}
