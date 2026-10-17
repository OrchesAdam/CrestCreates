using System.ComponentModel;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Result mapper contributed by one assembly, keyed by (endpoint id, version).
/// The mapper keeps the object-typed delegate shape of
/// <see cref="ICapabilityEndpointResultContractRegistry"/> so generated code does not need an
/// ASP.NET Core reference; the concrete mapper always returns an <c>IResult</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record CapabilityEndpointResultBinding(
    string EndpointId,
    int EndpointVersion,
    Func<EndpointExecutionContext, IServiceProvider, object> MapResult);
