using CrestCreates.Capability.Abstractions;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

internal static class CapabilityEndpointResultMapper
{
    public static IResult Map(CapabilityExecutionResult result, CapabilityEndpointOutputMapping outputMapping)
    {
        return result.Status switch
        {
            CapabilityExecutionStatus.Succeeded => MapSuccess(result.Output, outputMapping),
            CapabilityExecutionStatus.Failed => MapFailure(result),
            CapabilityExecutionStatus.TimedOut => Results.StatusCode(504),
            CapabilityExecutionStatus.Compensated => Results.StatusCode(409),
            _ => Results.StatusCode(500)
        };
    }

    private static IResult MapSuccess(object? output, CapabilityEndpointOutputMapping mapping)
    {
        if (output is null)
        {
            return Results.StatusCode(mapping.SuccessStatusCode);
        }

        return Results.Json(output, statusCode: mapping.SuccessStatusCode, contentType: mapping.ContentType);
    }

    private static IResult MapFailure(CapabilityExecutionResult result)
    {
        return result.ErrorCode switch
        {
            "UNAUTHORIZED" => Results.Forbid(),
            "CAPABILITY_NOT_FOUND" => Results.NotFound(),
             "CAPABILITY_VALIDATION_FAILED" => Results.Problem(statusCode: 400, detail: result.ErrorMessage),
            "RATE_LIMIT_EXCEEDED" => Results.StatusCode(429),
            _ => Results.Problem(detail: result.ErrorMessage)
        };
    }
}
