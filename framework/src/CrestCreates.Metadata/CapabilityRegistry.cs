using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class CapabilityRegistry : RegistryBase<CapabilityDescriptor>
{
    protected override string RegistryNamespace => "capability";

    public CapabilityRegistry(IRegistryValidationEngine<CapabilityDescriptor> validationEngine)
        : base(validationEngine) { }

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
