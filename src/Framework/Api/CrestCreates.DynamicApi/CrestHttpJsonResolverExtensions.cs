using System;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.DynamicApi;

/// <summary>
/// Host HTTP JSON composition: inserts the host's generated combined resolver immediately before the
/// untouched built-in reflection resolver (or appends it when none exists), so reflection can never
/// shadow generated contracts while explicit host resolvers keep their priority.
/// </summary>
public static class CrestHttpJsonResolverExtensions
{
    /// <summary>
    /// Registers <paramref name="resolver"/> ahead of the untouched built-in reflection resolver and,
    /// by default, rejects unmapped members so unknown wire fields fail instead of being ignored.
    /// </summary>
    public static IServiceCollection AddCrestHttpJsonResolver(
        this IServiceCollection services,
        IJsonTypeInfoResolver resolver,
        bool disallowUnmappedMembers = true)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(resolver);

        services.Configure<JsonOptions>(options =>
        {
            var chain = options.SerializerOptions.TypeInfoResolverChain;
            var insertionIndex = chain.Count;
            for (var index = 0; index < chain.Count; index++)
            {
                if (chain[index] is DefaultJsonTypeInfoResolver defaultResolver
                    && defaultResolver.GetType() == typeof(DefaultJsonTypeInfoResolver)
                    && defaultResolver.Modifiers.Count == 0)
                {
                    insertionIndex = index;
                    break;
                }
            }

            chain.Insert(insertionIndex, resolver);

            if (disallowUnmappedMembers)
            {
                options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            }
        });

        return services;
    }
}
