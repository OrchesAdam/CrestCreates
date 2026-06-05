using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Data.EFCore.SqlServer.DatabaseProviders.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Data.EFCore.SqlServer.Configuration;

public static class SqlServerServiceCollectionExtensions
{
    /// <summary>
    /// Registers SQL Server-specific EF Core services:
    /// <list type="bullet">
    ///   <item><see cref="ITenantDatabaseProvisioner"/> with SQL Server CREATE DATABASE support</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddCrestCreatesEfCoreSqlServer(this IServiceCollection services)
    {
        services.TryAddScoped<SqlServerTenantDatabaseProvisioner>();
        services.TryAddScoped<ITenantDatabaseProvisioner, SqlServerTenantDatabaseProvisioner>();

        return services;
    }
}