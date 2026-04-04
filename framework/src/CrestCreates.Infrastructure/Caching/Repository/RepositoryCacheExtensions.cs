using System;
using CrestCreates.Domain.Entities;
using CrestCreates.Domain.Repositories;
using CrestCreates.Domain.Shared.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Infrastructure.Caching.Repository
{
    public static class RepositoryCacheExtensions
    {
        public static IServiceCollection AddCachedRepository<TEntity, TId>(
            this IServiceCollection services,
            TimeSpan? defaultExpiration = null)
            where TEntity : class, IEntity<TId>
            where TId : IEquatable<TId>
        {
            services.AddScoped<CachedRepository<TEntity, TId>>();
            return services;
        }

        public static IServiceCollection AddRepositoryCaching(
            this IServiceCollection services,
            Action<RepositoryCacheOptions>? configureOptions = null)
        {
            var options = new RepositoryCacheOptions();
            configureOptions?.Invoke(options);
            services.AddSingleton(options);

            return services;
        }
    }

    public class RepositoryCacheOptions
    {
        public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(10);
        public bool EnableCaching { get; set; } = true;
    }
}
