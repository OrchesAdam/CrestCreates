using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public static class CapabilityHandlerResolverProvider
{
    private static ICapabilityHandlerResolver? _resolver;

    public static void SetResolver(ICapabilityHandlerResolver resolver)
    {
        _resolver = resolver;
    }

    public static ICapabilityHandlerResolver? GetResolver()
    {
        return _resolver;
    }
}
