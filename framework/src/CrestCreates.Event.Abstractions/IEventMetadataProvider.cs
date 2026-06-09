namespace CrestCreates.Event.Abstractions;

public interface IEventMetadataProvider
{
    RegistryState State { get; }
    IReadOnlyList<GeneratedEventDescriptor> GetAllVersions(string name);
    GeneratedEventDescriptor? GetLatestVersion(string name);
    IReadOnlyList<GeneratedEventDescriptor> GetAll();
}
