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
    /// #128 S3: the typed accessor generated binding/result code uses when a missing metadata table
    /// must fail closed. §7.3 explicitly forbids falling back to a general resolver, so a host that did
    /// not assemble the generated contract gets a deterministic error instead of a reflection guess.
    /// </summary>
    public static JsonTypeInfo<T> Require<T>(IServiceProvider serviceProvider)
    {
        JsonTypeInfo<T>? metadata;
        try
        {
            metadata = Resolve<T>(serviceProvider);
        }
        catch (NotSupportedException)
        {
            // A host whose resolver carries no metadata for T reports this the moment the type is asked
            // for; §7.3 wants one deterministic rejection here rather than STJ's implementation detail.
            metadata = null;
        }

        return metadata ?? throw new InvalidOperationException(
            $"Source-generated JSON metadata for '{typeof(T)}' is not available in this host. "
            + "The generated CRUD/compatibility JSON contract is not assembled or not activated for this Host.");
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
