using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestCreates.DynamicApi;

[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete("Use CapabilityEndpointBodyReader with JsonTypeInfo<T> from CapabilityEndpointJsonTypeInfoResolver.")]
[UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
    Justification = "Legacy runtime — not on the AOT-verified mainline. Use CapabilityEndpointBodyReader instead.")]
[UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
    Justification = "Legacy runtime — not on the AOT-verified mainline. Use CapabilityEndpointBodyReader instead.")]
public static class CapabilityEndpointJsonRuntime
{
    public static async ValueTask<T?> ReadBodyAsync<T>(
        HttpContext context, bool optional, CancellationToken ct = default)
    {
        if (context.Request.ContentLength == 0 && optional)
            return default;

        var options = ResolveJsonSerializerOptions(context);
        T? result;
        try
        {
            result = await JsonSerializer.DeserializeAsync<T>(
                context.Request.Body, options, ct);
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

    public static async ValueTask<T?> ReadBodyAsync<T>(
        HttpContext context, JsonTypeInfo<T> jsonTypeInfo,
        bool optional, CancellationToken ct = default)
    {
        if (context.Request.ContentLength == 0 && optional)
            return default;

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

    private static JsonSerializerOptions? _cachedDefaultOptions;

    private static JsonSerializerOptions ResolveJsonSerializerOptions(HttpContext context)
    {
        var jsonOptions = context.RequestServices
            .GetService<IOptions<JsonOptions>>();

        if (jsonOptions is not null)
            return jsonOptions.Value.SerializerOptions;

        return _cachedDefaultOptions ??= new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
