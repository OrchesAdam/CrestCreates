using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class CapabilityRegistry : RegistryBase<CapabilityDescriptor>, ICapabilityRegistry
{
    protected override string RegistryNamespace => "capability";

    public CapabilityRegistry(IRegistryValidationEngine<CapabilityDescriptor> validationEngine)
        : base(validationEngine) { }

    // === ICapabilityRegistry ===

    public IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind)
        => GetAll().Where(d => d.CapabilityKind == kind).ToList();

    public IReadOnlyList<CapabilityDescriptor> GetByTag(string tag)
        => GetAll().Where(d => d.SemanticTags.Contains(tag)).ToList();

    // === IDescriptorRegistry<CapabilityDescriptor> ===

    CapabilityDescriptor? IDescriptorRegistry<CapabilityDescriptor>.GetByName(string name)
        => GetByName(name).FirstOrDefault(d => d.State == DescriptorState.Active) ?? GetByName(name).FirstOrDefault();

    // === IVersionedDescriptorRegistry<CapabilityDescriptor> ===

    public CapabilityDescriptor? GetByNameAndVersion(string name, int version)
        => GetByName(name).FirstOrDefault(d => d.Version == version);

    public IReadOnlyList<CapabilityDescriptor> GetAllByName(string name)
        => GetByName(name);

    public CapabilityDescriptor? GetActiveVersion(string name)
        => GetByName(name).Where(d => d.State == DescriptorState.Active).MaxBy(d => d.Version);

    public CapabilityDescriptor? GetLatestVersion(string name)
        => GetByName(name).MaxBy(d => d.Version);

    public IReadOnlyList<CapabilityDescriptor> GetDeprecatedVersions(string name)
        => GetByName(name).Where(d => d.State == DescriptorState.Deprecated).ToList();

    // === Build ===

    protected override RegistrySnapshot<CapabilityDescriptor> BuildSnapshot(
        List<CapabilityDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(d => d.Id)
            .ToFrozenDictionary(g => g.Key, g => g.OrderByDescending(d => d.Version).First());

        var byName = descriptors
            .GroupBy(d => d.Name)
            .ToFrozenDictionary(g => g.Key, g => g.ToImmutableArray());

        var byVersion = descriptors
            .ToFrozenDictionary(d => new DescriptorKey(d.Namespace, d.Id, d.Version), d => d);

        return new RegistrySnapshot<CapabilityDescriptor>(
            byId, byName, byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
