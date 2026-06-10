using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata;

namespace CrestCreates.Capability.Internal;

internal interface ICapabilityVersionResolver
{
    CapabilityDescriptor Resolve(CapabilityRef capabilityRef);
}
