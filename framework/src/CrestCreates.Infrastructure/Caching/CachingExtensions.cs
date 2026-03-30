using System;
using Castle.DynamicProxy;
using CrestCreates.Application.Contracts.Caching;
using CrestCreates.Infrastructure.Caching.Advanced;
using CrestCreates.Infrastructure.Caching.Attributes;
using CrestCreates.Infrastructure.Caching.Interceptors;
using CrestCreates.Infrastructure.Caching.Metrics;
using CrestCreates.Infrastructure.Caching.MultiLevel;
using CrestCreates.Infrastructure.Caching.MultiTenancy;
using CrestCreates.Infrastructure.Caching.Repository;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class CachingExtensions
    {
        public static IServiceCollection AddCaching(this IServiceCollection services, Action<CrestCreates.Infrastructure.Caching.CacheOptions> configure = null)
        {
            var options = new CrestCreates.Infrastructure.Caching.CacheOptions();
            configure?.Invoke(options);

            services.AddSingleton(options);
            services.AddMemoryCache();

            switch (options.Provider?.ToLower())
            {
                case "redis":
                    ConfigureRedisCache(services, options);
                    break;
                case "multilevel":
                    ConfigureMultiLevelCache(services, options);
                    break;
                default:
                    ConfigureMemoryCache(services);
                    break;
            }

            return services;
        }

        public static IServiceCollection AddCaching(this IServiceCollection services, IConfiguration configuration)
        {
            var options = new CrestCreates.Infrastructure.Caching.CacheOptions();
            configuration.GetSection("Caching").Bind(options);

            services.AddSingleton(options);
            services.AddMemoryCache();

            switch (options.Provider?.ToLower())
            {
                case "redis":
                    ConfigureRedisCache(services, options);
                    break;
                case "multilevel":
                    ConfigureMultiLevelCache(services, options);
                    break;
                default:
                    ConfigureMemoryCache(services);
                    break;
            }

            return services;
        }

        private static void ConfigureMemoryCache(IServiceCollection services)
        {
            services.AddScoped<CrestCreates.Infrastructure.Caching.ICache, CrestCreates.Infrastructure.Caching.MemoryCache>();
        }

        private static void ConfigureRedisCache(IServiceCollection services, CrestCreates.Infrastructure.Caching.CacheOptions options)
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var configuration = ConfigurationOptions.Parse(options.RedisConnectionString);
                configuration.DefaultDatabase = options.RedisDatabase;
                return ConnectionMultiplexer.Connect(configuration);
            });
            services.AddScoped<CrestCreates.Infrastructure.Caching.ICache, CrestCreates.Infrastructure.Caching.RedisCache>();
        }

        private static void ConfigureMultiLevelCache(IServiceCollection services, CrestCreates.Infrastructure.Caching.CacheOptions options)
        {
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var configuration = ConfigurationOptions.Parse(options.RedisConnectionString);
                configuration.DefaultDatabase = options.RedisDatabase;
                return ConnectionMultiplexer.Connect(configuration);
            });

            services.Configure<MultiLevelCacheOptions>(multiLevelOptions =>
            {
                multiLevelOptions.L1Expiration = TimeSpan.FromMinutes(5);
                multiLevelOptions.L2Expiration = options.DefaultExpiration;
                multiLevelOptions.EnableL1Sync = true;
            });

            services.AddHostedService<CacheSynchronizer>();
            services.AddSingleton<CacheSynchronizer>();
            services.AddScoped<CrestCreates.Infrastructure.Caching.ICache, MultiLevelCache>();
        }

        public static IServiceCollection AddCachingInterceptors(this IServiceCollection services)
        {
            services.AddSingleton<ICacheKeyExpressionParser, CacheKeyExpressionParser>();
            services.AddSingleton<IProxyGenerator, ProxyGenerator>();
            services.AddScoped<CachingInterceptor>();

            return services;
        }

        public static IServiceCollection AddApplicationCaching(this IServiceCollection services)
        {
            services.AddScoped<ICacheService, CrestCreates.Infrastructure.Caching.CacheService>();
            return services;
        }

        public static IServiceCollection AddRepositoryCaching(this IServiceCollection services, Action<RepositoryCacheOptions>? configureOptions = null)
        {
            var options = new RepositoryCacheOptions();
            configureOptions?.Invoke(options);
            services.AddSingleton(options);

            return services;
        }

        public static IServiceCollection AddMultiTenancyCaching(this IServiceCollection services)
        {
            services.AddScoped<ICacheKeyGenerator, TenantCacheKeyGenerator>();
            services.AddScoped<TenantAwareCache>();
            return services;
        }

        public static IServiceCollection AddCacheMetrics(this IServiceCollection services)
        {
            services.AddSingleton<ICacheMetricsCollector, CacheMetricsCollector>();
            return services;
        }

        public static IServiceCollection AddCacheHealthChecks(this IServiceCollection services, string name = "cache")
        {
            return services;
        }

        public static IServiceCollection AddCacheWarmer(this IServiceCollection services)
        {
            services.AddHostedService<CacheWarmerService>();
            services.AddSingleton<ICacheBreakerProtection, CacheBreakerProtection>();
            return services;
        }

        public static IServiceCollection AddCacheBreakerProtection(this IServiceCollection services)
        {
            services.AddSingleton<ICacheBreakerProtection, CacheBreakerProtection>();
            return services;
        }
    }
}
