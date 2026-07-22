using System;
using System.Collections.Concurrent;

namespace CrestCreates.Capability.Abstractions;

public static class CapabilityHandlerResolverProvider
{
    private static readonly CapabilityHandlerResolver Resolver = new();
    private static readonly ConcurrentDictionary<string, Action<CapabilityHandlerResolver>> Definitions = new(StringComparer.Ordinal);

    public static void Register(string capabilityId, ICapabilityHandlerInvoker invoker)
        => Resolver.Register(capabilityId, invoker);

    public static ICapabilityHandlerResolver GetResolver() => Resolver;

    public static CapabilityHandlerResolver GetConcreteResolver() => Resolver;

    /// <summary>
    /// Registers an immutable generated provider definition. Definitions are
    /// declarations only; they never mutate the process-wide compatibility
    /// resolver and are applied explicitly to a Host-owned resolver.
    /// </summary>
    public static void RegisterDefinition(string providerId, Action<CapabilityHandlerResolver> apply)
    {
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider id is required.", nameof(providerId));
        ArgumentNullException.ThrowIfNull(apply);
        if (!Definitions.TryAdd(providerId, apply))
            throw new InvalidOperationException($"Duplicate generated capability provider definition '{providerId}'.");
    }

    public static void ApplyDefinition(string providerId, CapabilityHandlerResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (!Definitions.TryGetValue(providerId, out var apply))
            throw new InvalidOperationException($"Generated capability provider definition '{providerId}' is unavailable.");
        apply(resolver);
    }

    /// <summary>
    /// Clears all registered invokers. Internal for test isolation.
    /// </summary>
    internal static void Reset()
    {
        Resolver.Reset();
    }

    [Obsolete("Use Register() for additive registration.")]
    public static void SetResolver(ICapabilityHandlerResolver resolver)
    {
        // Compatibility no-op.
        // Old generated code will be replaced in the same phase.
    }

    /// <summary>
    /// Copies all Register()-added invokers from the process-wide static resolver
    /// to a target resolver. Used by LegacyCapabilityHandlerModule.
    /// Does NOT copy RegisterDefinition entries.
    /// </summary>
    public static void ApplyLegacyRegistrations(CapabilityHandlerResolver target)
    {
        Resolver.CopyRegistrationsTo(target);
    }
}
