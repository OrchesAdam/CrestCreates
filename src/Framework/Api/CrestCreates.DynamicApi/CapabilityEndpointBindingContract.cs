using System.ComponentModel;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Binding contract stored by the source-generated module initializer.
/// References an SG-generated function that binds a structured input model
/// from the current HttpContext for a specific CapabilityEndpoint version.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record CapabilityEndpointBindingContract(
    string EndpointId,
    int EndpointVersion,
    Func<HttpContext, CancellationToken, ValueTask<object?>> BindInputAsync);
