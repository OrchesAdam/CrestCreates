using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Event;

public sealed class EventRegistry : RegistryBase<GeneratedEventDescriptor>,
    IEventRegistry, IEventMetadataProvider
{
    private FrozenDictionary<Type, GeneratedEventDescriptor>? _byPayloadType;

    protected override string RegistryNamespace => "event";

    public EventRegistry(IRegistryValidationEngine<GeneratedEventDescriptor> validationEngine)
        : base(validationEngine) { }

    // IEventRegistry (preserved API)
    public new GeneratedEventDescriptor? GetByName(string name)
    {
        var all = base.GetByName(name);
        return all.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version);
    }

    public GeneratedEventDescriptor? GetByPayloadType(Type t)
        => _byPayloadType?.TryGetValue(t, out var d) == true ? d : null;

    public GeneratedEventDescriptor? GetByNameAndVersion(string name, int version)
        => base.GetByName(name).FirstOrDefault(v => v.Version == version);

    // IEventMetadataProvider (preserved API)
    public IReadOnlyList<GeneratedEventDescriptor> GetAllVersions(string name)
        => base.GetByName(name);

    public GeneratedEventDescriptor? GetLatestVersion(string name)
        => base.GetByName(name).MaxBy(v => v.Version);

    protected override RegistrySnapshot<GeneratedEventDescriptor> BuildSnapshot(
        List<GeneratedEventDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        _byPayloadType = descriptors
            .Where(d => d.State == DescriptorState.Active)
            .GroupBy(d => d.PayloadType)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        return new RegistrySnapshot<GeneratedEventDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
