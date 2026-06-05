using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Data.EFCore.MySql.DatabaseProviders.MySql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Data.EFCore.MySql.Configuration;

public static class MySqlServiceCollectionExtensions
{
    /// <summary>
    /// Registers MySQL-specific EF Core services:
    /// <list type="bullet">
    ///   <item><see cref="ITenantDatabaseProvisioner"/> with MySQL CREATE DATABASE support</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddCrestCreatesEfCoreMySql(this IServiceCollection services)
    {
        services.TryAddScoped<MySqlTenantDatabaseProvisioner>();
        services.TryAddScoped<ITenantDatabaseProvisioner, MySqlTenantDatabaseProvisioner>();

        return services;
    }
}
