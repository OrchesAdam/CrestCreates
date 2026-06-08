using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Event.Abstractions;

namespace CrestCreates.Event;

public sealed class EventRegistry : IEventRegistry
{
    private readonly ConcurrentDictionary<string, EventDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<EventDescriptor>> _byName = new();
    private readonly ConcurrentDictionary<EventCategory, List<EventDescriptor>> _byCategory = new();
    private readonly ConcurrentDictionary<EventSemantic, List<EventDescriptor>> _bySemantic = new();
    private readonly ConcurrentDictionary<EventImportance, List<EventDescriptor>> _byImportance = new();

    public void Register(EventDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
        _byCategory.GetOrAdd(descriptor.Category, _ => new()).Add(descriptor);
        _bySemantic.GetOrAdd(descriptor.Semantic, _ => new()).Add(descriptor);
        _byImportance.GetOrAdd(descriptor.Importance, _ => new()).Add(descriptor);
    }

    public EventDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public EventDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public EventDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public EventDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public EventDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<EventDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<EventDescriptor>();

    public IReadOnlyList<EventDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<EventDescriptor>();

    public IReadOnlyList<EventDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();

    public IReadOnlyList<EventDescriptor> GetByCategory(EventCategory category) =>
        _byCategory.TryGetValue(category, out var list) ? list.AsReadOnly() : Array.Empty<EventDescriptor>();

    public IReadOnlyList<EventDescriptor> GetBySemantic(EventSemantic semantic) =>
        _bySemantic.TryGetValue(semantic, out var list) ? list.AsReadOnly() : Array.Empty<EventDescriptor>();

    public IReadOnlyList<EventDescriptor> GetByImportance(EventImportance importance) =>
        _byImportance.TryGetValue(importance, out var list) ? list.AsReadOnly() : Array.Empty<EventDescriptor>();
}
