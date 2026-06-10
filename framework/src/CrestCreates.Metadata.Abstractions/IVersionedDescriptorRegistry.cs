namespace CrestCreates.Metadata.Abstractions;

public interface IVersionedDescriptorRegistry<TDescriptor>
    : IDescriptorRegistry<TDescriptor>
    where TDescriptor : IVersionedDescriptor
{
    TDescriptor? GetByNameAndVersion(string name, int version);
    TDescriptor? GetByVersion(string id, int version);
    IReadOnlyList<TDescriptor> GetAllByName(string name);
    TDescriptor? GetActiveVersion(string name);
    TDescriptor? GetLatestVersion(string name);
    IReadOnlyList<TDescriptor> GetDeprecatedVersions(string name);
}
