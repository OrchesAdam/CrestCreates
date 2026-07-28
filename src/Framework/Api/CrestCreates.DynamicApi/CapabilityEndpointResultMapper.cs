using System.Diagnostics.CodeAnalysis;
using CrestCreates.Capability.Abstractions;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

internal static class CapabilityEndpointResultMapper
{
    public static IResult Map(CapabilityExecutionResult result, CapabilityEndpointOutputMapping outputMapping)
    {
        // Legacy overload without HttpContext — used by callers that don't have
        // request context. Falls back to reflection-based Results.Json.
        // AOT-verified callers should use the HttpContext overload below.
        return result.Status switch
        {
            CapabilityExecutionStatus.Succeeded => MapSuccess(result.Output, outputMapping),
            CapabilityExecutionStatus.Failed => MapFailure(result),
            CapabilityExecutionStatus.TimedOut => Results.StatusCode(504),
            CapabilityExecutionStatus.Compensated => Results.StatusCode(409),
            _ => Results.StatusCode(500)
        };
    }

    public static IResult Map(CapabilityExecutionResult result, CapabilityEndpointOutputMapping outputMapping, HttpContext httpContext)
    {
        return result.Status switch
        {
            CapabilityExecutionStatus.Succeeded => MapSuccessAot(result.Output, outputMapping, httpContext),
            CapabilityExecutionStatus.Failed => MapFailure(result),
            CapabilityExecutionStatus.TimedOut => Results.StatusCode(504),
            CapabilityExecutionStatus.Compensated => Results.StatusCode(409),
            _ => Results.StatusCode(500)
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Legacy fallback — AOT-verified callers use the HttpContext overload with JsonTypeInfo.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Legacy fallback — AOT-verified callers use the HttpContext overload with JsonTypeInfo.")]
    private static IResult MapSuccess(object? output, CapabilityEndpointOutputMapping mapping)
    {
        if (output is null)
        {
            return Results.StatusCode(mapping.SuccessStatusCode);
        }

        return Results.Json(output, statusCode: mapping.SuccessStatusCode, contentType: mapping.ContentType);
    }

    private static IResult MapSuccessAot(object? output, CapabilityEndpointOutputMapping mapping, HttpContext httpContext)
    {
        if (output is null)
        {
            return Results.StatusCode(mapping.SuccessStatusCode);
        }

        // AOT-safe: resolve JsonTypeInfo from the application's configured
        // JsonSerializerOptions (which contains the source-generated TypeInfoResolver).
        var jsonTypeInfo = CapabilityEndpointJsonTypeInfoResolver.Resolve(httpContext, output.GetType());
        if (jsonTypeInfo is not null)
        {
            return Results.Json(output, jsonTypeInfo, contentType: mapping.ContentType, statusCode: mapping.SuccessStatusCode);
        }

        // Fallback: no JsonTypeInfo available — use reflection-based serialization.
        // Suppressed because this path is only reached when the application does not
        // configure a JsonSerializerContext, which is a Tier 3/4 scenario.
        return MapSuccessFallback(output, mapping);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Fallback for applications without JsonSerializerContext — Tier 3/4 scenario.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Fallback for applications without JsonSerializerContext — Tier 3/4 scenario.")]
    private static IResult MapSuccessFallback(object? output, CapabilityEndpointOutputMapping mapping)
    {
        return Results.Json(output, statusCode: mapping.SuccessStatusCode, contentType: mapping.ContentType);
    }

    private static IResult MapFailure(CapabilityExecutionResult result)
    {
        return result.ErrorCode switch
        {
            "UNAUTHORIZED" => Results.Forbid(),
            "CAPABILITY_NOT_FOUND" => Results.NotFound(),
            "CAPABILITY_RESOURCE_NOT_FOUND" => Results.NotFound(),
            "CAPABILITY_FORBIDDEN" => Results.StatusCode(StatusCodes.Status403Forbidden),
            "CAPABILITY_INVOCATION_SOURCE_FORBIDDEN" => Results.StatusCode(StatusCodes.Status403Forbidden),
            "CAPABILITY_DECISION_CONFLICT" => Results.StatusCode(StatusCodes.Status409Conflict),
            "CAPABILITY_DECISION_STATE_INVALID" => Results.StatusCode(StatusCodes.Status409Conflict),
            "PROCUREMENT_APPROVAL_WORKFLOW_UNAVAILABLE" => Results.StatusCode(StatusCodes.Status503ServiceUnavailable),
             "CAPABILITY_VALIDATION_FAILED" => Results.Problem(statusCode: 400, detail: result.ErrorMessage),
            "RATE_LIMIT_EXCEEDED" => Results.StatusCode(429),
            _ => Results.Problem(detail: result.ErrorMessage)
        };
    }
}
