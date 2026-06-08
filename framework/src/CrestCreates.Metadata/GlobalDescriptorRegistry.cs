using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class GlobalDescriptorRegistry : IGlobalDescriptorRegistry
{
    private readonly ConcurrentDictionary<string, IDescriptor> _byId = new();
    private readonly ConcurrentDictionary<DescriptorKind, List<IDescriptor>> _byKind = new();
    private readonly ConcurrentDictionary<string, List<IDescriptor>> _byPackage = new();

    public void Register(IDescriptor descriptor)
    {
        _byId[descriptor.Id] = descriptor;
        _byKind.GetOrAdd(descriptor.Kind, _ => new()).Add(descriptor);
    }

    public IDescriptor? GetById(string id) =>
        _byId.TryGetValue(id, out var d) ? d : null;

    public IReadOnlyList<IDescriptor> GetAll() =>
        _byId.Values.ToList().AsReadOnly();

    public IReadOnlyList<IDescriptor> GetByKind(DescriptorKind kind) =>
        _byKind.TryGetValue(kind, out var list) ? list.AsReadOnly() : Array.Empty<IDescriptor>();

    public IReadOnlyList<IDescriptor> GetByPackage(string packageId) =>
        _byPackage.TryGetValue(packageId, out var list) ? list.AsReadOnly() : Array.Empty<IDescriptor>();

    public void RegisterPackage(string packageId, IReadOnlyList<IDescriptor> descriptors)
    {
        foreach (var d in descriptors)
        {
            Register(d);
        }
        _byPackage[packageId] = descriptors.ToList();
    }
}