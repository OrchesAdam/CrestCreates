using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;

namespace CrestCreates.Event.Abstractions;

public interface IEventRegistry
{
    RegistryState State { get; }
    void Build(IEnumerable<IDescriptorProvider<GeneratedEventDescriptor>> providers);
    GeneratedEventDescriptor? GetByName(string name);
    GeneratedEventDescriptor? GetByPayloadType(Type payloadType);
    GeneratedEventDescriptor? GetByNameAndVersion(string name, int version);
}
