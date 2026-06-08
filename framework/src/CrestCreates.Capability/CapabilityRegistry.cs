using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly ConcurrentDictionary<string, CapabilityDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<CapabilityDescriptor>> _byName = new();
    private readonly ConcurrentDictionary<CapabilityKind, List<CapabilityDescriptor>> _byKind = new();
    private readonly ConcurrentDictionary<string, List<CapabilityDescriptor>> _byTag = new();

    public void Register(CapabilityDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
        _byKind.GetOrAdd(descriptor.CapabilityKind, _ => new()).Add(descriptor);
        foreach (var tag in descriptor.SemanticTags)
        {
            _byTag.GetOrAdd(tag, _ => new()).Add(descriptor);
        }
    }

    public CapabilityDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public CapabilityDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public CapabilityDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public CapabilityDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public CapabilityDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<CapabilityDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<CapabilityDescriptor>();

    public IReadOnlyList<CapabilityDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<CapabilityDescriptor>();

    public IReadOnlyList<CapabilityDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();

    public IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind) =>
        _byKind.TryGetValue(kind, out var list) ? list.AsReadOnly() : Array.Empty<CapabilityDescriptor>();

    public IReadOnlyList<CapabilityDescriptor> GetByTag(string tag) =>
        _byTag.TryGetValue(tag, out var list) ? list.AsReadOnly() : Array.Empty<CapabilityDescriptor>();
}
