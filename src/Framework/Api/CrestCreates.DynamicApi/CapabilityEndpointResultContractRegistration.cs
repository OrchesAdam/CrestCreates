using System.ComponentModel;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Static registration helper for result contract mappers.
/// Follows the same pattern as <see cref="CapabilityEndpointBindingRegistry"/>:
/// static <c>Register</c> called from generated <c>[ModuleInitializer]</c> code,
/// <c>ApplyTo</c> called at startup to flush pending registrations into the
/// runtime <see cref="ICapabilityEndpointResultContractRegistry"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CapabilityEndpointResultContractRegistration
{
    private static readonly List<(string EndpointId, int Version, Func<EndpointExecutionContext, IServiceProvider, object> Mapper)> _pending = new();

    /// <summary>
    /// Enqueues a result mapper for deferred registration.
    /// Called by generated <c>[ModuleInitializer]</c> code during startup.
    /// </summary>
    public static void Register(string endpointId, int version, Func<EndpointExecutionContext, IServiceProvider, object> mapResult)
    {
        _pending.Add((endpointId, version, mapResult));
    }

    /// <summary>
    /// Flushes all pending registrations into the runtime registry.
    /// Called once at application startup before endpoint mapping.
    /// </summary>
    internal static void ApplyTo(ICapabilityEndpointResultContractRegistry registry)
    {
        foreach (var (endpointId, version, mapper) in _pending)
            registry.Register(endpointId, version, mapper);
    }

    /// <summary>
    /// Clears all pending registrations and the runtime registry.
    /// Internal for test isolation.
    /// </summary>
    internal static void Reset()
    {
        _pending.Clear();
    }
}
