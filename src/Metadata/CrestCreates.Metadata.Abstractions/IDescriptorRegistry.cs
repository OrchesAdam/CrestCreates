using CrestCreates.Metadata.Abstractions.Registry;

namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorRegistry<TDescriptor> where TDescriptor : IDescriptor
{
    RegistryState State { get; }
    TDescriptor? GetById(string id);
    TDescriptor? GetByName(string name);
    IReadOnlyList<TDescriptor> GetAll();
    void Build(IEnumerable<IDescriptorProvider<TDescriptor>> providers);
}
