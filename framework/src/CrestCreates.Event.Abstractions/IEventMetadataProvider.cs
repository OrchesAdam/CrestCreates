namespace CrestCreates.Event.Abstractions;

public interface IEventMetadataProvider
{
    CrestCreates.Metadata.Abstractions.RegistryState State { get; }
    IReadOnlyList<GeneratedEventDescriptor> GetAllVersions(string name);
    GeneratedEventDescriptor? GetLatestVersion(string name);
    IReadOnlyList<GeneratedEventDescriptor> GetAll();
}
