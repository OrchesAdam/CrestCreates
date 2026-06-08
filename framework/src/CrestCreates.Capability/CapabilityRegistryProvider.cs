using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public static class CapabilityRegistryProvider
{
    private static ICapabilityRegistry? _registry;

    public static void SetRegistry(ICapabilityRegistry registry)
    {
        _registry = registry;
    }

    public static void Register(CapabilityDescriptor descriptor)
    {
        if (_registry is CapabilityRegistry concrete)
        {
            concrete.Register(descriptor);
        }
    }
}
