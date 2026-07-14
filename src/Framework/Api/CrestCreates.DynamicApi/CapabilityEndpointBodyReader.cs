using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Unified AOT-safe body reader for capability endpoints.
/// Replaces both CapabilityEndpointJsonRuntime (8a native) and CompatibilityBodyReader (8d compatibility).
/// 
/// Policy differences via emptyBodyFactory parameter:
///   - Native path (emptyBodyFactory = null): empty required body → 400 BadHttpRequestException
///   - Compatibility path (emptyBodyFactory provided): empty required body → factory() default instance
/// </summary>
public static class CapabilityEndpointBodyReader
{
    /// <summary>
    /// AOT-safe body reading with JsonTypeInfo.
    /// </summary>
    /// <param name="context">HTTP context</param>
    /// <param name="jsonTypeInfo">STJ-generated type metadata for AOT safety</param>
    /// <param name="emptyBodyFactory">
    ///   null (Native path): empty required body → 400 BadHttpRequestException
    ///   non-null (Compatibility path): empty required body → factory() default instance
    /// </param>
    /// <param name="optional">If true, missing/invalid body returns default instead of throwing</param>
    /// <param name="ct">Cancellation token</param>
    public static async ValueTask<T?> ReadBodyAsync<T>(
        HttpContext context,
        JsonTypeInfo<T> jsonTypeInfo,
        Func<T>? emptyBodyFactory,
        bool optional,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);

        // ContentLength == 0: handle based on policy
        if (context.Request.ContentLength == 0)
        {
            if (optional)
                return default;
            
            if (emptyBodyFactory is not null)
                return emptyBodyFactory();
            
            throw new BadHttpRequestException(
                $"Request body is required and cannot be empty for {typeof(T).Name}.");
        }

        // Enable buffering for seek-back (Compatibility path compatibility)
        context.Request.EnableBuffering();
        if (context.Request.Body.CanSeek)
            context.Request.Body.Seek(0, SeekOrigin.Begin);

        // For Compatibility path: read full body as string first to support empty/whitespace detection
        if (emptyBodyFactory is not null)
        {
            return await ReadWithLegacySemantics(context, jsonTypeInfo, emptyBodyFactory, optional, ct);
        }

        // Native path: stream-based deserialization
        return await ReadWithNativeSemantics(context, jsonTypeInfo, optional, ct);
    }

    private static async ValueTask<T?> ReadWithNativeSemantics<T>(
        HttpContext context,
        JsonTypeInfo<T> jsonTypeInfo,
        bool optional,
        CancellationToken ct)
    {
        T? result;
        try
        {
            result = await JsonSerializer.DeserializeAsync(
                context.Request.Body, jsonTypeInfo, ct);
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException(
                $"Request body could not be deserialized as {typeof(T).Name}.", ex);
        }

        if (result is null && !optional)
            throw new BadHttpRequestException(
                $"Request body could not be deserialized as {typeof(T).Name}.");

        return result;
    }

    private static async ValueTask<T?> ReadWithLegacySemantics<T>(
        HttpContext context,
        JsonTypeInfo<T> jsonTypeInfo,
        Func<T> emptyBodyFactory,
        bool optional,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var payload = await reader.ReadToEndAsync(ct);
            
            if (string.IsNullOrWhiteSpace(payload))
            {
                return optional ? default : emptyBodyFactory();
            }

            var result = JsonSerializer.Deserialize(payload, jsonTypeInfo);
            if (result is not null)
                return result;

            return optional ? default : emptyBodyFactory();
        }
        catch (JsonException) when (optional)
        {
            return default;
        }
        finally
        {
            if (context.Request.Body.CanSeek)
                context.Request.Body.Seek(0, SeekOrigin.Begin);
        }
    }
}
