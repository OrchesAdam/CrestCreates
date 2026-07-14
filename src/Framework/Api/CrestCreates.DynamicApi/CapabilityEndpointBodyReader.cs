using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// AOT-safe body reader for Capability Endpoint binding code.
/// Provides two entry points with distinct semantics:
///
/// <list type="bullet">
///   <item><see cref="ReadNativeBodyAsync{T}"/> — native capability endpoints:
///     empty body → 400 BAD_REQUEST, no body construction needed.</item>
///   <item><see cref="ReadCompatibilityBodyAsync{T}"/> — compatibility projection endpoints:
///     preserves legacy Dynamic API body-reading contract exactly.</item>
/// </list>
///
/// Generated binding code resolves JsonTypeInfo&lt;T&gt; from
/// CapabilityEndpointJsonTypeInfoResolver and passes it here.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CapabilityEndpointBodyReader
{
    /// <summary>
    /// Reads and deserializes the request body for native capability endpoints.
    ///
    /// <para>Semantics:</para>
    /// <list type="bullet">
    ///   <item>ContentLength == 0 + required → 400 BadHttpRequestException</item>
    ///   <item>ContentLength == 0 + optional → default</item>
    ///   <item>Whitespace body + required → 400 BadHttpRequestException</item>
    ///   <item>Whitespace body + optional → default</item>
    ///   <item>JSON null + required → 400 BadHttpRequestException</item>
    ///   <item>JSON null + optional → default</item>
    ///   <item>Invalid JSON + required → BadHttpRequestException</item>
    ///   <item>Invalid JSON + optional → default</item>
    /// </list>
    /// </summary>
    public static async ValueTask<T?> ReadNativeBodyAsync<T>(
        HttpContext context,
        JsonTypeInfo<T> jsonTypeInfo,
        bool optional,
        CancellationToken ct = default)
    {
        // ContentLength == 0: no body content
        if (context.Request.ContentLength == 0)
        {
            if (optional)
                return default;
            throw new BadHttpRequestException(
                $"Request body is required for type {typeof(T).Name}.");
        }

        // Enable buffering so we can check for whitespace
        context.Request.EnableBuffering();
        if (context.Request.Body.CanSeek)
            context.Request.Body.Seek(0, SeekOrigin.Begin);

        // Check for whitespace body
        using var peekReader = new StreamReader(context.Request.Body, Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var peekBuffer = new char[1];
        var bytesRead = await peekReader.ReadAsync(peekBuffer.AsMemory(0, 1), ct);
        if (bytesRead == 0 || char.IsWhiteSpace(peekBuffer[0]))
        {
            // Rewind for potential re-reading
            if (context.Request.Body.CanSeek)
                context.Request.Body.Seek(0, SeekOrigin.Begin);

            if (optional)
                return default;
            throw new BadHttpRequestException(
                $"Request body is required for type {typeof(T).Name}.");
        }

        // Rewind and deserialize
        if (context.Request.Body.CanSeek)
            context.Request.Body.Seek(0, SeekOrigin.Begin);

        try
        {
            var result = await JsonSerializer.DeserializeAsync(
                context.Request.Body, jsonTypeInfo, ct);

            if (result is null)
            {
                if (optional)
                    return default;
                throw new BadHttpRequestException(
                    $"Request body could not be deserialized as {typeof(T).Name}.");
            }

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

    /// <summary>
    /// Reads and deserializes the request body for compatibility projection endpoints.
    /// Preserves the exact body-reading semantics of the legacy Dynamic API pipeline.
    ///
    /// <para>Semantics (matching legacy CompatibilityBodyReader):</para>
    /// <list type="bullet">
    ///   <item>ContentLength == 0 + optional → default (null)</item>
    ///   <item>ContentLength == 0 + required → emptyBodyFactory()</item>
    ///   <item>Whitespace body + optional → default (null)</item>
    ///   <item>Whitespace body + required → emptyBodyFactory()</item>
    ///   <item>JSON null + optional → default (null)</item>
    ///   <item>JSON null + required → emptyBodyFactory()</item>
    ///   <item>Invalid JSON + optional → default (null)</item>
    ///   <item>Invalid JSON + required → BadHttpRequestException</item>
    /// </list>
    /// </summary>
    /// <param name="context">The current HttpContext.</param>
    /// <param name="jsonTypeInfo">AOT-safe type metadata resolved from the application's serializer options.</param>
    /// <param name="emptyBodyFactory">
    /// Factory for creating a default instance when the body is empty/whitespace/null and required.
    /// For compatibility projection, pass <c>static () => new T()</c> to match legacy behavior.
    /// </param>
    /// <param name="optional">
    /// If true, empty/missing/null body returns default. If false, uses emptyBodyFactory.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static async ValueTask<T?> ReadCompatibilityBodyAsync<T>(
        HttpContext context,
        JsonTypeInfo<T> jsonTypeInfo,
        Func<T> emptyBodyFactory,
        bool optional,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(emptyBodyFactory);

        // ContentLength == 0: use factory or default
        if (context.Request.ContentLength == 0)
        {
            return optional ? default : emptyBodyFactory();
        }

        // Enable buffering for whitespace check and re-reading
        context.Request.EnableBuffering();
        if (context.Request.Body.CanSeek)
            context.Request.Body.Seek(0, SeekOrigin.Begin);

        try
        {
            var result = await JsonSerializer.DeserializeAsync(
                context.Request.Body, jsonTypeInfo, ct);

            // Null result from valid JSON (e.g., "null" literal)
            if (result is null)
            {
                return optional ? default : emptyBodyFactory();
            }

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
        finally
        {
            if (context.Request.Body.CanSeek)
                context.Request.Body.Seek(0, SeekOrigin.Begin);
        }
    }
}
