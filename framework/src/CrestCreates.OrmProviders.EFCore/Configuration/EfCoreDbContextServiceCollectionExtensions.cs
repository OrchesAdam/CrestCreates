using System;
using System.Linq;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Application.Tenants;
using CrestCreates.DbContextProvider.Abstract;
using CrestCreates.OrmProviders.EFCore.DbContexts;
using CrestCreates.OrmProviders.EFCore.Interceptors;
using CrestCreates.OrmProviders.EFCore.MultiTenancy;
using CrestCreates.OrmProviders.EFCore.ValueConverters;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CrestCreates.OrmProviders.EFCore.Configuration;

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

        services.TryAddScoped<ITenantDatabaseInitializer, EfCoreTenantDatabaseInitializer>();
        services.TryAddScoped<ITenantMigrationRunner, EfCoreTenantMigrationRunner>();
        services.TryAddScoped<ITenantInitializationStore, EfCoreTenantInitializationStore>();

        // Default factory for tenant migration: creates CrestCreatesDbContext with UseSqlServer.
        // Projects using a custom DbContext or different provider should register their own factory
        // BEFORE calling this method, so TryAddSingleton skips this default registration.
        services.TryAddSingleton<Func<string, DbContext>>(connectionString =>
        {
            var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            return new CrestCreatesDbContext(options);
        });

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