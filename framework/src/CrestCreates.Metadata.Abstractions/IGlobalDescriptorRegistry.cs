namespace CrestCreates.Metadata.Abstractions;

public interface IGlobalDescriptorRegistry
{
    IDescriptor? GetById(string id);
    IReadOnlyList<IDescriptor> GetAll();
    IReadOnlyList<IDescriptor> GetByKind(DescriptorKind kind);
    IReadOnlyList<IDescriptor> GetByPackage(string packageId);
    void Register(IDescriptor descriptor);
}
