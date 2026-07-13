using System.Collections.Concurrent;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Thread-safe per-endpoint result contract registry.
/// Populated at startup by source-generated <c>[ModuleInitializer]</c> code
/// via the static <see cref="CapabilityEndpointResultContractRegistration"/> helper.
/// </summary>
public sealed class CapabilityEndpointResultContractRegistry : ICapabilityEndpointResultContractRegistry
{
    private readonly ConcurrentDictionary<(string EndpointId, int Version), Func<EndpointExecutionContext, object>> _mappers = new();

    public void Register(string endpointId, int version, Func<EndpointExecutionContext, object> mapResult)
    {
        _mappers.TryAdd((endpointId, version), mapResult);
    }

    public Func<EndpointExecutionContext, object>? TryGetResultMapper(string endpointId, int version)
    {
        _mappers.TryGetValue((endpointId, version), out var mapper);
        return mapper;
    }
}
