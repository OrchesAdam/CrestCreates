namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorProvider<TDescriptor>
    where TDescriptor : IDescriptor
{
    IReadOnlyList<TDescriptor> GetDescriptors();
}
