using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Schema;

public sealed class SchemaRegistry : ISchemaRegistry
{
    private readonly ConcurrentDictionary<string, SchemaDescriptor> _byId = new();
    private readonly ConcurrentDictionary<string, List<SchemaDescriptor>> _byName = new();

    public void Register(SchemaDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byName.GetOrAdd(descriptor.Name, _ => new()).Add(descriptor);
    }

    public SchemaDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public SchemaDescriptor? GetByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.State == DescriptorState.Active)
            : null;

    public SchemaDescriptor? GetByNameAndVersion(string name, int version) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.FirstOrDefault(v => v.Version == version)
            : null;

    public SchemaDescriptor? GetActiveVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Active)
                      .MaxBy(v => v.Version)
            : null;

    public SchemaDescriptor? GetLatestVersion(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.MaxBy(v => v.Version)
            : null;

    public IReadOnlyList<SchemaDescriptor> GetAllByName(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.AsReadOnly()
            : Array.Empty<SchemaDescriptor>();

    public IReadOnlyList<SchemaDescriptor> GetDeprecatedVersions(string name) =>
        _byName.TryGetValue(name, out var versions)
            ? versions.Where(v => v.State == DescriptorState.Deprecated).ToList().AsReadOnly()
            : Array.Empty<SchemaDescriptor>();

    public IReadOnlyList<SchemaDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();
}
