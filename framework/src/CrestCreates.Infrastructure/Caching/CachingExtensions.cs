using System;
using Microsoft.Extensions.DependencyInjection;
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

            if (options.Provider == "redis")
            {
                services.AddSingleton<IConnectionMultiplexer>(sp =>
                {
                    var configuration = ConfigurationOptions.Parse(options.RedisConnectionString);
                    configuration.DefaultDatabase = options.RedisDatabase;
                    return ConnectionMultiplexer.Connect(configuration);
                });
                services.AddScoped<CrestCreates.Infrastructure.Caching.ICache, CrestCreates.Infrastructure.Caching.RedisCache>();
            }
            else
            {
                services.AddScoped<CrestCreates.Infrastructure.Caching.ICache, CrestCreates.Infrastructure.Caching.MemoryCache>();
            }

            return services;
        }


    }
}