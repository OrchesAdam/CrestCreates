using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Data.EFCore.SqlServer.DatabaseProviders.SqlServer;
using CrestCreates.Data.EFCore.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Data.EFCore.SqlServer.Configuration;

public static class SqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers SQL Server-specific EF Core services:
    /// <list type="bullet">
    ///   <item><see cref="ITenantDatabaseProvisioner"/> with SQL Server CREATE DATABASE support</item>
    ///   <item>Default <see cref="Func{string, DbContext}"/> factory for tenant schema migration</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddCrestCreatesEfCoreSqlServer(this IServiceCollection services)
    {
        services.TryAddScoped<SqlServerTenantDatabaseProvisioner>();
        services.TryAddScoped<ITenantDatabaseProvisioner, SqlServerTenantDatabaseProvisioner>();

        // Default tenant DbContext factory for schema migration
        services.TryAddSingleton<Func<string, DbContext>>(connectionString =>
        {
            var options = new DbContextOptionsBuilder<CrestCreatesDbContext>()
                .UseSqlServer(connectionString)
                .Options;
            return new CrestCreatesDbContext(options);
        });

        return services;
    }
}