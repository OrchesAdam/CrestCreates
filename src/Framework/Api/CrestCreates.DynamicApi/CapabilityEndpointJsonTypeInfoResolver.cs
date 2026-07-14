using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Resolves JsonTypeInfo&lt;T&gt; from the application's configured
/// JsonSerializerOptions. This is the single point of truth for how
/// generated binding code obtains source-generated JSON metadata.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CapabilityEndpointJsonTypeInfoResolver
{
    /// <summary>
    /// Resolves JsonTypeInfo&lt;T&gt; from the application's
    /// IOptions&lt;JsonOptions&gt; via the HttpContext's RequestServices.
    /// </summary>
    public static JsonTypeInfo<T>? Resolve<T>(HttpContext context)
    {
        var options = ResolveOptions(context.RequestServices);
        return (JsonTypeInfo<T>?)options.GetTypeInfo(typeof(T));
    }

    /// <summary>
    /// Resolves JsonTypeInfo for a type from the application's
    /// IOptions&lt;JsonOptions&gt; via the HttpContext's RequestServices.
    /// Non-generic version used by startup validation.
    /// </summary>
    public static JsonTypeInfo? Resolve(HttpContext context, Type type)
    {
        var options = ResolveOptions(context.RequestServices);
        return options.GetTypeInfo(type);
    }

    /// <summary>
    /// Resolves JsonTypeInfo&lt;T&gt; directly from an IServiceProvider.
    /// Used by startup validation (no HttpContext available).
    /// </summary>
    public static JsonTypeInfo<T>? Resolve<T>(IServiceProvider serviceProvider)
    {
        var options = ResolveOptions(serviceProvider);
        return (JsonTypeInfo<T>?)options.GetTypeInfo(typeof(T));
    }

    /// <summary>
    /// Resolves JsonTypeInfo directly from an IServiceProvider.
    /// Non-generic version used by startup validation.
    /// </summary>
    public static JsonTypeInfo? Resolve(IServiceProvider serviceProvider, Type type)
    {
        var options = ResolveOptions(serviceProvider);
        return options.GetTypeInfo(type);
    }

    private static JsonSerializerOptions ResolveOptions(IServiceProvider serviceProvider)
    {
        // Fail-closed: application MUST configure JsonOptions with a JsonSerializerContext.
        // Fallback to reflection-based options would silently break trimming safety.
        var jsonOptions = serviceProvider.GetRequiredService<IOptions<JsonOptions>>();
        return jsonOptions.Value.SerializerOptions;
    }
}
