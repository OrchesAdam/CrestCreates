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
///
/// <para><b>Obsolete:</b> Use <see cref="CapabilityEndpointBodyReader"/> instead,
/// which provides AOT-safe body reading with <c>JsonTypeInfo&lt;T&gt;</c> and
/// <c>Func&lt;T&gt;? emptyBodyFactory</c> for compatibility semantics.</para>
/// </summary>
[Obsolete("Use CapabilityEndpointBodyReader.ReadBodyAsync with JsonTypeInfo<T> and emptyBodyFactory instead. This class uses reflection-based JSON deserialization which is not AOT-safe.")]
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
