using CrestCreates.Capability.Abstractions;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Transport adapter for capability failures. The host's web layer implements this to render its
/// authoritative error wire (for CrestCreates that is the CrestErrorResponse). Runtime code never
/// references the adapter assembly and never serializes exception objects or stacks.
/// </summary>
public interface ICapabilityEndpointFailurePresenter
{
    IResult Present(CapabilityExecutionResult result, HttpContext context);

    IResult PresentBindingFailure(Exception exception, HttpContext context);

    IResult PresentNotFound(HttpContext context);
}

/// <summary>
/// Marker returned by a generated GET result mapper when the handler returned no resource. The
/// endpoint mapper replaces it with the host's authoritative not-found response.
/// </summary>
public sealed class CapabilityEndpointNotFoundResult
{
    public static CapabilityEndpointNotFoundResult Instance { get; } = new();

    private CapabilityEndpointNotFoundResult()
    {
    }
}
