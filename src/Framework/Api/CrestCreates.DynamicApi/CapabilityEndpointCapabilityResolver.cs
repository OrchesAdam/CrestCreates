using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.DescriptorCapability;

namespace CrestCreates.DynamicApi;

internal static class CapabilityEndpointCapabilityResolver
{
    /// <summary>
    /// ExpectedContractHash validation deferred — 8a does not validate hash.
    /// 8a supports Exact version and LatestActive semantics only.
    /// Other VersionSelectionMode values are out of scope.
    /// </summary>
    internal static CapabilityDescriptor Resolve(
        ICapabilityRegistry registry,
        VersionedDescriptorRef<CapabilityDescriptor> capabilityRef)
    {
        // 1. Exact version resolution (Version > 0)
        if (capabilityRef.Version > 0)
        {
            var exact = registry.GetByVersion(capabilityRef.Id, capabilityRef.Version);
            if (exact is not null)
                return exact;
        }

        // 2. Latest active by Id
        var byId = registry.GetById(capabilityRef.Id);
        if (byId is not null && byId.State == DescriptorState.Active)
            return byId;

        // 3. Fallback: scan all for matching id + active state, take max version
        var active = registry.GetAll()
            .Where(d => d.Id == capabilityRef.Id && d.State == DescriptorState.Active)
            .MaxBy(d => d.Version);
        if (active is not null)
            return active;

        // 4. Not found
        throw new InvalidOperationException(
            $"Capability descriptor not found for id='{capabilityRef.Id}', version={capabilityRef.Version}.");
    }
}
