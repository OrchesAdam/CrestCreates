using System;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Web.Legacy;

/// <summary>
/// Explicit activation for the legacy AppService-oriented Dynamic API. The default formal Web
/// composition never calls this path; a Host must opt in and declare the exact assemblies and
/// service/action allowlist it still needs. The policy is registered as the same gate the retained
/// raw Add/Map entry points consult, so no public activation path bypasses it.
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
        services.AddSingleton<ILegacyDynamicApiAllowlist>(legacy);
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

        _ = endpoints.ServiceProvider.GetService<LegacyGeneratedApiOptions>()
            ?? throw new InvalidOperationException(
                "CCHTTP007: MapLegacyGeneratedApi requires AddLegacyGeneratedApi with an explicit allowlist.");

        // The runtime map revalidates the exact allowlist and route collisions, so the retained
        // MapCrestDynamicApi path cannot skip either check.
        return endpoints.MapCrestDynamicApi();
    }
}
