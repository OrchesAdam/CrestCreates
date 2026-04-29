using System;
using System.Linq;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.OrmProviders.EFCore.Configuration;

public static class EfCoreDbContextServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesEfCoreDbContext(this IServiceCollection services)
    {
        services.TryAddScoped<AuditInterceptor>();
        services.TryAddScoped<MultiTenancyInterceptor>();
        services.TryAddSingleton<TenantAwareModelCacheKeyFactory>();

        services.AddDbContext<CrestCreatesDbContext>((serviceProvider, optionsBuilder) =>
        {
            var contributors = serviceProvider.GetServices<IEfCoreDbContextOptionsContributor>().ToArray();
            if (contributors.Length == 0)
            {
                throw new InvalidOperationException(
                    "No EF Core DbContext options contributor was registered. Register a provider-specific IEfCoreDbContextOptionsContributor before adding CrestCreatesDbContext.");
            }

            foreach (var contributor in contributors)
            {
                contributor.Configure(serviceProvider, optionsBuilder);
            }

            optionsBuilder.AddInterceptors(
                serviceProvider.GetRequiredService<AuditInterceptor>(),
                serviceProvider.GetRequiredService<MultiTenancyInterceptor>());
            optionsBuilder.ReplaceService<IModelCacheKeyFactory, TenantAwareModelCacheKeyFactory>();
        });

        services.TryAdd(ServiceDescriptor.Scoped<IEntityFrameworkCoreDbContext>(sp => sp.GetRequiredService<CrestCreatesDbContext>()));
        services.TryAdd(ServiceDescriptor.Scoped<IDataBaseContext>(sp => sp.GetRequiredService<IEntityFrameworkCoreDbContext>()));

        return services;
    }
}
