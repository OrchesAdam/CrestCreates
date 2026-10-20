using System;
using System.Collections.Generic;
using System.Linq;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Web.Legacy;

/// <summary>
/// Explicit activation for the legacy AppService-oriented Dynamic API. The default formal Web
/// composition never calls this path; a Host must opt in and declare the exact assemblies and
/// service/action allowlist it still needs.
/// </summary>
public static class LegacyGeneratedApiExtensions
{
    public static IServiceCollection AddLegacyGeneratedApi(
        this IServiceCollection services,
        Action<LegacyGeneratedApiOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var legacy = new LegacyGeneratedApiOptions();
        configure(legacy);
        if (legacy.ServiceMarkerTypes.Count == 0)
        {
            throw new InvalidOperationException(
                "CCHTTP007: AddLegacyGeneratedApi requires at least one explicitly selected application assembly.");
        }

        if (legacy.AllowedActions.Count == 0)
        {
            throw new InvalidOperationException(
                "CCHTTP007: AddLegacyGeneratedApi requires an explicit per-service/action allowlist; "
                + "the legacy surface is never activated by assembly or namespace scope.");
        }

        services.AddSingleton(legacy);
        return services.AddCrestDynamicApi(options =>
        {
            foreach (var marker in legacy.ServiceMarkerTypes)
            {
                options.AddApplicationServiceAssembly(marker.Assembly);
            }
        });
    }

    public static IEndpointRouteBuilder MapLegacyGeneratedApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var legacy = endpoints.ServiceProvider.GetService<LegacyGeneratedApiOptions>()
            ?? throw new InvalidOperationException(
                "CCHTTP007: MapLegacyGeneratedApi requires AddLegacyGeneratedApi with an explicit allowlist.");
        var options = endpoints.ServiceProvider.GetRequiredService<DynamicApiOptions>();
        var registry = DynamicApiGeneratedRegistryStore.BuildRegistry(options)
            ?? throw DynamicApiGeneratedRegistryStore.CreateMissingGeneratedProviderException(options);

        ValidateAllowlist(registry, legacy);
        ValidateRoutes(endpoints, registry);
        return endpoints.MapCrestDynamicApi();
    }

    private static void ValidateAllowlist(DynamicApiRegistry registry, LegacyGeneratedApiOptions legacy)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var service in registry.Services)
        {
            var key = ServiceKey(service);
            if (!legacy.AllowedActions.TryGetValue(key, out var allowed))
            {
                throw new InvalidOperationException(
                    $"CCHTTP007: the legacy provider publishes service '{service.ServiceName}' ({key}), "
                    + "but the Host allowlist does not declare it. Every legacy service needs an explicit "
                    + "Allow<TService>(...) entry; whole-assembly or namespace activation is not supported.");
            }

            foreach (var action in service.Actions)
            {
                declared.Add(key + "::" + action.ActionName);
                if (!allowed.Contains(action.ActionName))
                {
                    throw new InvalidOperationException(
                        $"CCHTTP007: the legacy provider publishes '{service.ServiceName}.{action.ActionName}', "
                        + "but that action is not in the Host allowlist. Add it explicitly or move the "
                        + "service to the formal ExplicitHost projection.");
                }
            }
        }

        foreach (var (service, actions) in legacy.AllowedActions)
        {
            foreach (var action in actions)
            {
                if (!declared.Contains(service + "::" + action))
                {
                    throw new InvalidOperationException(
                        $"CCHTTP007: the Host allowlist declares legacy action '{service}.{action}', but no "
                        + "generated legacy provider publishes it. Remove the stale entry.");
                }
            }
        }
    }

    private static void ValidateRoutes(IEndpointRouteBuilder endpoints, DynamicApiRegistry registry)
    {
        var existing = endpoints.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    ?? Array.Empty<string>();
                var route = NormalizeRoute(endpoint.RoutePattern.RawText);
                return methods.Select(method => (Method: method.ToUpperInvariant(), Route: route));
            })
            .ToHashSet();

        var legacyRoutes = new HashSet<(string Method, string Route)>();
        foreach (var service in registry.Services)
        {
            foreach (var action in service.Actions)
            {
                var key = (action.HttpMethod.ToUpperInvariant(), NormalizeRoute(action.FullRoute));
                if (!legacyRoutes.Add(key) || existing.Contains(key))
                {
                    throw new InvalidOperationException(
                        $"CCHTTP005: legacy action '{service.ServiceName}.{action.ActionName}' publishes "
                        + $"{key.Item1} '{key.Item2}', which is already mapped by another endpoint. "
                        + "A formal/legacy double publish (or duplicate legacy route) is rejected before mapping.");
                }
            }
        }
    }

    private static string ServiceKey(DynamicApiServiceDescriptor service)
        => service.ServiceType?.FullName ?? service.ServiceName;

    private static string NormalizeRoute(string? route)
        => "/" + (route ?? string.Empty).Trim('/');
}
