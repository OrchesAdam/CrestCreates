using System;
using CrestCreates.DynamicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.AspNetCore;

/// <summary>
/// Obsolete legacy entry points retained only inside the explicit compatibility package. They do not
/// register an allowlist themselves, so mapping through them fails closed with CCHTTP007; callers
/// must migrate to
/// <see cref="CrestCreates.Web.Legacy.LegacyGeneratedApiExtensions.AddLegacyGeneratedApi"/> and provide
/// explicit per-service/action entries before mapping.
/// </summary>
public static class LegacyAspNetCoreModuleExtensions
{
    [Obsolete("Use CrestCreates.Web.Legacy.AddLegacyGeneratedApi with an explicit per-service/action allowlist.")]
    public static IServiceCollection AddCrestAspNetCoreDynamicApi(
        this IServiceCollection services,
        Action<DynamicApiOptions>? configure = null)
        => services.AddCrestDynamicApi(configure);

    [Obsolete("Use CrestCreates.Web.Legacy.MapLegacyGeneratedApi after configuring the explicit allowlist.")]
    public static IEndpointRouteBuilder MapCrestAspNetCoreDynamicApi(this IEndpointRouteBuilder endpoints)
        => CrestCreates.Web.Legacy.LegacyGeneratedApiExtensions.MapLegacyGeneratedApi(endpoints);
}
