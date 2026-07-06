using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;

namespace CrestCreates.DynamicApi;

public sealed class CapabilityEndpointRegistry
    : RegistryBase<CapabilityEndpointDescriptor>, ICapabilityEndpointRegistry
{
    protected override string RegistryNamespace => "dynamic-api-endpoint";

    public CapabilityEndpointRegistry(
        IRegistryValidationEngine<CapabilityEndpointDescriptor> validationEngine)
        : base(validationEngine)
    {
    }

    CapabilityEndpointDescriptor? IDescriptorRegistry<CapabilityEndpointDescriptor>.GetByName(string name)
        => GetByName(name).FirstOrDefault(d => d.State == DescriptorState.Active)
           ?? GetByName(name).FirstOrDefault();

    public CapabilityEndpointDescriptor? GetByNameAndVersion(string name, int version)
        => GetByName(name).FirstOrDefault(d => d.Version == version);

    public IReadOnlyList<CapabilityEndpointDescriptor> GetAllByName(string name)
        => GetByName(name);

    public CapabilityEndpointDescriptor? GetActiveVersion(string name)
        => GetByName(name).Where(d => d.State == DescriptorState.Active).MaxBy(d => d.Version);

    public CapabilityEndpointDescriptor? GetLatestVersion(string name)
        => GetByName(name).MaxBy(d => d.Version);

    public IReadOnlyList<CapabilityEndpointDescriptor> GetDeprecatedVersions(string name)
        => GetByName(name).Where(d => d.State == DescriptorState.Deprecated).ToList();

    public IReadOnlyList<CapabilityEndpointDescriptor> GetByCapability(
        string capabilityId,
        int? capabilityVersion = null)
    {
        if (_snapshot?.CustomIndexes.TryGetValue(typeof(ByCapabilityIdIndex), out var index) == true
            && index is ByCapabilityIdIndex capIndex
            && capIndex.Map.TryGetValue(capabilityId, out var matches))
        {
            return capabilityVersion.HasValue
                ? matches.Where(d => d.Capability.Version == capabilityVersion.Value).ToImmutableArray()
                : matches;
        }

        return Array.Empty<CapabilityEndpointDescriptor>();
    }

    protected override RegistrySnapshot<CapabilityEndpointDescriptor> BuildSnapshot(
        List<CapabilityEndpointDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        var byCapabilityId = descriptors
            .GroupBy(d => d.Capability.Id)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var customIndexes = ImmutableDictionary<Type, IRegistryIndex>.Empty
            .Add(typeof(ByCapabilityIdIndex), new ByCapabilityIdIndex(byCapabilityId));

        return new RegistrySnapshot<CapabilityEndpointDescriptor>(
            byId,
            byName,
            byVersion,
            descriptors.ToImmutableArray(),
            customIndexes);
    }
}

public sealed class ByCapabilityIdIndex : IRegistryIndex
{
    public FrozenDictionary<string, ImmutableArray<CapabilityEndpointDescriptor>> Map { get; }

    public ByCapabilityIdIndex(FrozenDictionary<string, ImmutableArray<CapabilityEndpointDescriptor>> map)
    {
        Map = map;
    }
}
