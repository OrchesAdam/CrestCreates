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

    public CapabilityDescriptor Resolve(CapabilityRef capabilityRef)
    {
        if (capabilityRef.Version.HasValue)
        {
            // Id + Version → direct DescriptorKey lookup
            var descriptor = _registry.GetByVersion(capabilityRef.Id, capabilityRef.Version.Value);
            if (descriptor is not null) return descriptor;
        }
        else
        {
            // Id-only → prefer active version, fall back to latest
            var latest = _registry.GetById(capabilityRef.Id);
            if (latest is not null)
            {
                if (latest.State == DescriptorState.Active)
                    return latest;

                // Latest is not active — scan for any active version
                var active = _registry.GetAll()
                    .Where(d => d.Id == capabilityRef.Id && d.State == DescriptorState.Active)
                    .MaxBy(d => d.Version);
                if (active is not null) return active;

                // No active version — return latest (even if deprecated)
                return latest;
            }
        }

        throw new CapabilityNotFoundException(capabilityRef);
    }
}
