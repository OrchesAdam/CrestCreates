using System.Collections.Concurrent;
using System.ComponentModel;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Static store for CapabilityEndpointBindingContract entries.
/// Populated at startup by source-generated module initializer code.
/// Designed for fast lookup during endpoint execution.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CapabilityEndpointBindingRegistry
{
    private static readonly ConcurrentDictionary<(string EndpointId, int Version), CapabilityEndpointBindingContract> _bindings = new();

    /// <summary>
    /// Registers a binding contract. Throws if a binding already exists for the same (EndpointId, Version).
    /// </summary>
    public static void Register(CapabilityEndpointBindingContract contract)
    {
        if (!_bindings.TryAdd((contract.EndpointId, contract.EndpointVersion), contract))
        {
            throw new InvalidOperationException(
                $"A binding for endpoint '{contract.EndpointId}' version {contract.EndpointVersion} is already registered.");
        }
    }

    /// <summary>
    /// Finds a binding contract by endpoint id and version. Returns null if not found.
    /// </summary>
    public static CapabilityEndpointBindingContract? Find(string endpointId, int version)
    {
        _bindings.TryGetValue((endpointId, version), out var contract);
        return contract;
    }

    /// <summary>
    /// Gets a binding contract by endpoint id and version. Throws <see cref="InvalidOperationException"/> if not found.
    /// </summary>
    public static CapabilityEndpointBindingContract GetRequired(string endpointId, int version)
    {
        if (!_bindings.TryGetValue((endpointId, version), out var contract))
        {
            throw new InvalidOperationException(
                $"No binding registered for endpoint '{endpointId}' version {version}.");
        }

        return contract;
    }

    /// <summary>
    /// Clears all registered bindings. Internal for test isolation.
    /// </summary>
    internal static void Reset()
    {
        _bindings.Clear();
    }
}
