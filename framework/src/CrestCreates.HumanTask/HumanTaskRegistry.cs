using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class HumanTaskRegistry : IHumanTaskRegistry
{
    private readonly ConcurrentDictionary<string, HumanTaskDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<HumanTaskDescriptor>> _byName = new();

    public void Register(HumanTaskDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
    }

    public HumanTaskDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public HumanTaskDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public HumanTaskDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public HumanTaskDescriptor? GetByVersion(string id, int version)
    {
        var byId = GetById(id);
        if (byId != null && byId.Version == version)
            return byId;
        return GetAll().FirstOrDefault(d => d.Id == id && d.Version == version);
    }

    public HumanTaskDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public HumanTaskDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<HumanTaskDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<HumanTaskDescriptor>();

    public IReadOnlyList<HumanTaskDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<HumanTaskDescriptor>();

    public IReadOnlyList<HumanTaskDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();
}
