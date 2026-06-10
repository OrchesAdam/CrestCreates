using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Internal;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability;

internal sealed class DefaultCapabilityResolver : ICapabilityResolver
{
    private readonly ICapabilityVersionResolver _versionResolver;

    public DefaultCapabilityResolver(ICapabilityVersionResolver versionResolver)
    {
        _versionResolver = versionResolver;
    }

    public IVersionedDescriptor Resolve(CapabilityRef capabilityRef)
        => _versionResolver.Resolve(capabilityRef);
}
