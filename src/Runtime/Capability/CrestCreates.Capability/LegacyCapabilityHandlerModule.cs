using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

/// <summary>
/// Bridges legacy Register()-added invokers from the process-wide static
/// resolver into the new composable module system.
/// </summary>
internal sealed class LegacyCapabilityHandlerModule : ICapabilityHandlerModule
{
    internal static LegacyCapabilityHandlerModule Instance { get; } = new();

    private LegacyCapabilityHandlerModule() { }

    public string Id => "legacy-capability-pipeline";

    public void Apply(CapabilityHandlerResolver resolver)
    {
        CapabilityHandlerResolverProvider.ApplyLegacyRegistrations(resolver);
    }
}
