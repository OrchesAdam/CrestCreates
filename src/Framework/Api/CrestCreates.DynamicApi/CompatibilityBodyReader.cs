using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Legacy-compatible body reader for compatibility projections.
/// Matches the body-reading semantics of the legacy Dynamic API pipeline:
///   - ContentLength == 0 → optional ? default : new T()
///   - Empty/whitespace body → optional ? default : new T()
///   - Invalid JSON + optional → default (no exception)
///   - Invalid JSON + required → throws JsonException
///
/// This ensures that compatibility endpoints preserve the same request contract
/// as the legacy Dynamic API, rather than using ASP.NET Core's stricter
/// <see cref="HttpRequestReadExtensions.ReadFromJsonAsync{T}"/> which throws
/// <see cref="BadHttpRequestException"/> on empty bodies.
/// </summary>
[Obsolete("Use CapabilityEndpointBodyReader with JsonTypeInfo<T> from CapabilityEndpointJsonTypeInfoResolver.")]
public static class CompatibilityBodyReader
{
    public static async Task<T?> ReadBodyAsync<T>(HttpContext context, bool optional)
        where T : new()
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.ContentLength == 0)
        {
            return optional ? default : new T();
        }

        context.Request.EnableBuffering();
        if (context.Request.Body.CanSeek)
        {
            context.Request.Body.Seek(0, SeekOrigin.Begin);
        }

        try
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var payload = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(payload))
            {
                return optional ? default : new T();
            }

            var result = JsonSerializer.Deserialize<T>(payload,
                ResolveJsonSerializerOptions(context.RequestServices));
            if (result is not null)
            {
                return result;
            }

            return optional ? default : new T();
        }
        catch (JsonException) when (optional)
        {
            return default;
        }
        finally
        {
            if (context.Request.Body.CanSeek)
            {
                context.Request.Body.Seek(0, SeekOrigin.Begin);
            }
        }
    }

    private static JsonSerializerOptions ResolveJsonSerializerOptions(IServiceProvider serviceProvider)
    {
        var jsonOptions = (Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>?)
            serviceProvider.GetService(typeof(Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>));
        return new JsonSerializerOptions(jsonOptions?.Value.SerializerOptions ?? new JsonSerializerOptions())
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
