using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Per-endpoint result contract registry.
/// Compatibility endpoints register legacy-compatible result mapping
/// (e.g. <see cref="DynamicApiResponse"/> envelope wrapping).
/// Native endpoints use the default mapper.
/// </summary>
public interface ICapabilityEndpointResultContractRegistry
{
    void Register(string endpointId, int version, Func<EndpointExecutionContext, HttpContext, object> mapResult);
    Func<EndpointExecutionContext, HttpContext, object>? TryGetResultMapper(string endpointId, int version);
}
