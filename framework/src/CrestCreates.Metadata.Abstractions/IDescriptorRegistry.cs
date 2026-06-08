namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorRegistry<TDescriptor> where TDescriptor : IDescriptor
{
    TDescriptor? GetById(string id);
    TDescriptor? GetByName(string name);
    IReadOnlyList<TDescriptor> GetAll();
}
