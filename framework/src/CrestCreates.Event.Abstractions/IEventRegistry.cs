namespace CrestCreates.Event.Abstractions;

public interface IEventRegistry
{
    CrestCreates.Metadata.Abstractions.RegistryState State { get; }
    void Build(IEnumerable<IEventDescriptorProvider> providers);
    GeneratedEventDescriptor? GetByName(string name);
    GeneratedEventDescriptor? GetByPayloadType(Type payloadType);
    GeneratedEventDescriptor? GetByNameAndVersion(string name, int version);
}
