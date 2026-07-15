using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Neutral HTTP result factory for compatibility projections.
/// Produces <see cref="DynamicApiResponse"/> / <see cref="DynamicApiResponse{T}"/>
/// envelopes matching legacy AppService HTTP contract semantics.
///
/// Both legacy Dynamic API and compatibility result contracts call this class
/// so that the response envelope is owned by a single authority rather than
/// coupling compatibility projections to <see cref="DynamicApiGeneratedRuntime"/>.
/// </summary>
public static class CompatibilityHttpResultMapper
{
    /// <summary>
    /// Wraps a value in a success <see cref="DynamicApiResponse{T}"/> envelope.
    /// Uses AOT-safe JSON serialization when JsonTypeInfo is available.
    /// </summary>
    public static IResult WrapResult<T>(T? value, IServiceProvider? serviceProvider = null)
    {
        var envelope = new DynamicApiResponse<T?>
        {
            Code = StatusCodes.Status200OK,
            Message = "操作成功",
            Data = value
        };

        return MapAotJson(envelope, serviceProvider, StatusCodes.Status200OK);
    }

    /// <summary>
    /// Returns a void success envelope (no data field).
    /// Uses AOT-safe JSON serialization when JsonTypeInfo is available.
    /// </summary>
    public static IResult WrapVoidResult(IServiceProvider? serviceProvider = null)
    {
        var envelope = new DynamicApiResponse
        {
            Code = StatusCodes.Status200OK,
            Message = "操作成功"
        };

        return MapAotJson(envelope, serviceProvider, StatusCodes.Status200OK);
    }

    /// <summary>
    /// Returns a GET result: 404 envelope for null, 200 envelope for non-null.
    /// Uses AOT-safe JSON serialization when JsonTypeInfo is available.
    /// </summary>
    public static IResult WrapGetResult<T>(T? value, IServiceProvider? serviceProvider = null)
    {
        if (value is null)
        {
            var notFoundEnvelope = new DynamicApiResponse
            {
                Code = StatusCodes.Status404NotFound,
                Message = "资源不存在"
            };

            return MapAotJson(notFoundEnvelope, serviceProvider, StatusCodes.Status404NotFound);
        }

        return WrapResult(value, serviceProvider);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Fallback for applications without JsonSerializerContext — Tier 3/4 scenario.")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "Fallback for applications without JsonSerializerContext — Tier 3/4 scenario.")]
    private static IResult MapAotJson<TEnvelope>(TEnvelope envelope, IServiceProvider? serviceProvider, int statusCode)
    {
        if (serviceProvider is not null)
        {
            var jsonTypeInfo = CapabilityEndpointJsonTypeInfoResolver.Resolve(serviceProvider, typeof(TEnvelope));
            if (jsonTypeInfo is not null)
            {
                return Results.Json(envelope, jsonTypeInfo, statusCode: statusCode);
            }
        }

        // Fallback: no service provider or no JsonTypeInfo — use reflection-based serialization.
        return Results.Json(envelope, statusCode: statusCode);
    }
}
