using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Internal;

internal sealed class DefaultCapabilityVersionResolver : ICapabilityVersionResolver
{
    private readonly ICapabilityRegistry _registry;

    public DefaultCapabilityVersionResolver(ICapabilityRegistry registry)
    {
        _registry = registry;
    }

    public IVersionedDescriptor Resolve(CapabilityRef capabilityRef)
    {
        if (capabilityRef.Version.HasValue)
        {
            var descriptor = _registry.GetByNameAndVersion(capabilityRef.Id, capabilityRef.Version.Value);
            if (descriptor is not null) return descriptor;
        }
        else
        {
            var byName = _registry.GetAllByName(capabilityRef.Id);
            var active = byName
                .Where(d => d.State == DescriptorState.Active)
                .MaxBy(d => d.Version);
            if (active is not null) return active;
        }

        throw new CapabilityNotFoundException(capabilityRef);
    }
}
