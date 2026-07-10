using System;

namespace CrestCreates.Capability.Abstractions;

public static class CapabilityHandlerResolverProvider
{
    private static readonly CapabilityHandlerResolver Resolver = new();

    public static void Register(string capabilityId, ICapabilityHandlerInvoker invoker)
        => Resolver.Register(capabilityId, invoker);

    public static ICapabilityHandlerResolver GetResolver() => Resolver;

    [Obsolete("Use Register() for additive registration.")]
    public static void SetResolver(ICapabilityHandlerResolver resolver)
    {
        // Compatibility no-op.
        // Old generated code will be replaced in the same phase.
    }
}
