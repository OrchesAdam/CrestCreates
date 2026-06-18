using CrestCreates.Caching.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Caching;

public static class CachingServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCaching(this IServiceCollection services)
    {
        services.TryAddSingleton<CacheOptions>();
        services.TryAddSingleton<ICrestCache, CrestMemoryCache>();
        services.TryAddSingleton<ICrestCacheKeyGenerator, CrestCacheKeyGenerator>();
        services.TryAddSingleton<ICrestCacheService, CrestCacheService>();
        services.TryAddSingleton<SettingCacheKeyContributor>();
        services.TryAddSingleton<TenantCacheKeyContributor>();
        services.TryAddScoped<SettingCacheInvalidator>();

        return services;
    }
}
