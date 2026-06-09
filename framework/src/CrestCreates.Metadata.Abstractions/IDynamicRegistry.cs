namespace CrestCreates.Metadata.Abstractions;

public interface IDynamicRegistry<TDescriptor>
    where TDescriptor : IDescriptor
{
    bool TryRegister(TDescriptor descriptor);
    bool TryUnregister(string id);
    TDescriptor? GetById(string id);
}
