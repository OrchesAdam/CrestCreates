using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Form.Abstractions;

namespace CrestCreates.Form;

public sealed class FormRegistry : IFormRegistry
{
    private readonly ConcurrentDictionary<string, FormDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<FormDescriptor>> _byName = new();

    public void Register(FormDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
    }

    public FormDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public FormDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public FormDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public FormDescriptor? GetByVersion(string id, int version)
    {
        var byId = GetById(id);
        if (byId != null && byId.Version == version)
            return byId;
        return GetAll().FirstOrDefault(d => d.Id == id && d.Version == version);
    }

    public FormDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active).MaxBy(v => v.Version)
            : null;

    public FormDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<FormDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<FormDescriptor>();

    public IReadOnlyList<FormDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<FormDescriptor>();

    public IReadOnlyList<FormDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();
}
