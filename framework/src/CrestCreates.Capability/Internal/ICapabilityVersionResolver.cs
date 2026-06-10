using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Internal;

internal interface ICapabilityVersionResolver
{
    IVersionedDescriptor Resolve(CapabilityRef capabilityRef);
}
