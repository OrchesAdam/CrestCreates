using System;
using System.Linq;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.MultiTenancy;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.Data.EFCore.DbContexts;
using CrestCreates.Data.EFCore.Interceptors;
using CrestCreates.Data.EFCore.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Data.EFCore.Configuration;

public static class EfCoreDbContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers EF Core infrastructure services using <see cref="CrestCreatesDbContext"/>.
    /// Projects using a custom DbContext should call <see cref="AddCrestCreatesEfCoreDbContext{TDbContext}"/> instead.
    /// </summary>
    public static IServiceCollection AddCrestCreatesEfCoreDbContext(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<AuditInterceptor>();
        services.TryAddScoped<MultiTenancyInterceptor>();
        services.TryAddSingleton<TenantAwareModelCacheKeyFactory>();

        // ITenantDatabaseProvisioner is registered by the provider-specific project
        // (e.g., AddCrestCreatesEfCoreSqlServer, AddCrestCreatesEfCorePostgreSql, AddCrestCreatesEfCoreMySql).
        // If no provider registers one, tenant database provisioning will fail at runtime.

        services.TryAddScoped<ITenantSchemaMigrator, EfCoreTenantSchemaMigrator>();
        services.TryAddScoped<ITenantInitializationStore, EfCoreTenantInitializationStore>();

        // ITenantSchemaMigrator requires a Func<string, DbContext> factory.
        // Provider-specific projects register this factory via their own extension methods
        // (e.g., AddCrestCreatesEfCoreSqlServer). If no provider registers one,
        // resolving ITenantSchemaMigrator will fail at runtime.

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
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<CrestCreatesDbContext>());

        return services;
    }
}
