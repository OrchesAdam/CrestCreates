using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// AOT-safe body reader for Capability Endpoint binding code.
/// Replaces both CapabilityEndpointJsonRuntime and CompatibilityBodyReader.
///
/// Generated binding code resolves JsonTypeInfo&lt;T&gt; from
/// CapabilityEndpointJsonTypeInfoResolver and passes it here.
/// This ensures all JSON deserialization uses pre-generated metadata
/// with no runtime reflection.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CapabilityEndpointBodyReader
{
    /// <summary>
    /// Reads and deserializes the request body using AOT-safe JsonTypeInfo.
    /// </summary>
    /// <typeparam name="T">The body type to deserialize.</typeparam>
    /// <param name="context">The current HttpContext.</param>
    /// <param name="jsonTypeInfo">AOT-safe type metadata resolved from the application's serializer options.</param>
    /// <param name="emptyBodyFactory">
    /// Factory for creating a default instance when the body is empty.
    /// Pass null to treat empty body as an error for required parameters.
    /// For compatibility projection, pass <c>static () => new T()</c> to match legacy behavior.
    /// </param>
    /// <param name="optional">
    /// If true, empty/missing/invalid body returns default. If false, throws BadHttpRequestException.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The deserialized body, or default if optional and body is empty.</returns>
    public static async ValueTask<T?> ReadBodyAsync<T>(
        HttpContext context,
        JsonTypeInfo<T> jsonTypeInfo,
        Func<T>? emptyBodyFactory,
        bool optional,
        CancellationToken ct = default)
    {
        // ContentLength == 0: use factory if available, else optional check
        if (context.Request.ContentLength == 0)
        {
            if (emptyBodyFactory is not null)
                return emptyBodyFactory();
            if (optional)
                return default;
            throw new BadHttpRequestException(
                $"Request body is required for type {typeof(T).Name}.");
        }

        // Attempt to read body
        try
        {
            var result = await JsonSerializer.DeserializeAsync(
                context.Request.Body, jsonTypeInfo, ct);

            // Null result from valid JSON (e.g., "null" literal)
            if (result is null && !optional && emptyBodyFactory is null)
                throw new BadHttpRequestException(
                    $"Request body could not be deserialized as {typeof(T).Name}.");

            return result;
        }
        catch (JsonException) when (optional)
        {
            return default;
        }
        catch (JsonException ex)
        {
            throw new BadHttpRequestException(
                $"Request body could not be deserialized as {typeof(T).Name}.", ex);
        }
    }
}
