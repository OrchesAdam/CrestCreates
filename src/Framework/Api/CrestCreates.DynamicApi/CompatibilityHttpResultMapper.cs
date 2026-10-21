using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Neutral HTTP result factory for compatibility projections.
/// Produces <see cref="DynamicApiResponse"/> / <see cref="DynamicApiResponse{T}"/>
/// envelopes matching legacy AppService HTTP contract semantics.
///
/// Compatibility result contracts call this class so that the response envelope is owned by a single
/// authority; the legacy runtime was archived with the #129 S4 exit.
/// </summary>
public static class CompatibilityHttpResultMapper
{
    /// <summary>
    /// #128 S3 typed contract: wraps a value using the caller's declared static type and the matching
    /// source-generated envelope metadata. There is no reflection fallback here — §7.3 requires the
    /// typed overload to reject instead of guessing, so a missing metadata table throws rather than
    /// silently serializing through reflection.
    /// </summary>
    public static IResult WrapResult<T>(T value, JsonTypeInfo<DynamicApiResponse<T>> envelopeTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(envelopeTypeInfo);
        var envelope = new DynamicApiResponse<T>
        {
            Code = StatusCodes.Status200OK,
            Message = "操作成功",
            Data = value
        };

        return Results.Json(envelope, envelopeTypeInfo, statusCode: StatusCodes.Status200OK);
    }

    /// <summary>
    /// #128 S3 typed contract: GET result — 404 envelope for null, typed 200 envelope otherwise.
    /// Both branches use source-generated metadata and neither can fall back to reflection.
    /// </summary>
    public static IResult WrapGetResult<T>(
        T? value,
        JsonTypeInfo<DynamicApiResponse<T>> envelopeTypeInfo,
        JsonTypeInfo<DynamicApiResponse> notFoundTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(notFoundTypeInfo);
        if (value is null)
        {
            var notFoundEnvelope = new DynamicApiResponse
            {
                Code = StatusCodes.Status404NotFound,
                Message = "资源不存在"
            };

            return Results.Json(notFoundEnvelope, notFoundTypeInfo, statusCode: StatusCodes.Status404NotFound);
        }

        return WrapResult(value, envelopeTypeInfo);
    }

    /// <summary>
    /// GET result for the hand-written AppService front-end: a null output defers to the host's
    /// authoritative not-found response (CrestErrorResponse on CrestCreates hosts) instead of
    /// inventing a second 404 envelope shape. Non-null values use the typed 200 envelope.
    /// </summary>
    public static object WrapGetResultOrNotFound<T>(
        T? value,
        JsonTypeInfo<DynamicApiResponse<T>> envelopeTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(envelopeTypeInfo);
        return value is null
            ? CapabilityEndpointNotFoundResult.Instance
            : WrapResult(value, envelopeTypeInfo);
    }

    /// <summary>
    /// #128 S3 typed contract: the void/404 envelope with explicit metadata and no fallback.
    /// </summary>
    public static IResult WrapVoidResult(JsonTypeInfo<DynamicApiResponse> envelopeTypeInfo)
    {
        ArgumentNullException.ThrowIfNull(envelopeTypeInfo);
        var envelope = new DynamicApiResponse
        {
            Code = StatusCodes.Status200OK,
            Message = "操作成功"
        };

        return Results.Json(envelope, envelopeTypeInfo, statusCode: StatusCodes.Status200OK);
    }
}
