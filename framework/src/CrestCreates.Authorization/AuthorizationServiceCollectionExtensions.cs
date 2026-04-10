using CrestCreates.Authorization.Abstractions;
using CrestCreates.Caching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Authorization;

public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddCrestAuthorization(this IServiceCollection services)
    {
        services.AddCrestCaching();

        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.TryAddSingleton<IPermissionDefinitionManager, PermissionDefinitionManager>();
        services.TryAddSingleton<PermissionGrantCacheOptions>();
        services.TryAddScoped<PermissionGrantCacheService>();
        services.TryAddScoped<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
        services.TryAddScoped<ICurrentUser, CurrentUser>();
        services.TryAddScoped<TenantPermissionScopeValidator>();
        services.TryAddScoped<IPermissionChecker, PermissionChecker>();
        services.TryAddScoped<IPermissionGrantStore, PermissionGrantStore>();
        services.TryAddScoped<IPermissionGrantManager, PermissionGrantManager>();

        services.AddAuthorizationCore();

        return services;
    }

    public static IServiceCollection AddPermissionDefinitionProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IPermissionDefinitionProvider
    {
        services.AddTransient<IPermissionDefinitionProvider, TProvider>();
        return services;
    }
}
