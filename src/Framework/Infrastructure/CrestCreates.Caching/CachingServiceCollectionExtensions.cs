using CrestCreates.Caching.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CrestCreates.Caching;

public static class CachingServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCaching(this IServiceCollection services)
    {
        services.AddOptions<CacheOptions>();
        services.TryAddSingleton(serviceProvider => serviceProvider.GetRequiredService<IOptions<CacheOptions>>().Value);
        services.TryAddSingleton<ICrestCache, CrestMemoryCache>();
        services.TryAddSingleton<ICrestCacheKeyGenerator, CrestCacheKeyGenerator>();
        services.TryAddSingleton<ICrestCacheService, CrestCacheService>();
        services.TryAddSingleton<SettingCacheKeyContributor>();
        services.TryAddSingleton<TenantCacheKeyContributor>();
        services.TryAddScoped<SettingCacheInvalidator>();

        return services;
    }

    /// <summary>
    /// Replaces the in-memory cache with Redis while leaving the multiplexer owned by the DI
    /// container, so one connection is reused and disposed with the provider.
    /// </summary>
    public static IServiceCollection AddCrestRedisCaching(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddCrestCaching();
        services.TryAddSingleton<IConnectionMultiplexer>(_ =>
        {
            var configuration = ConfigurationOptions.Parse(connectionString);
            configuration.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(configuration);
        });
        services.Replace(ServiceDescriptor.Singleton<ICrestCache, RedisCrestCache>());

        return services;
    }
}
