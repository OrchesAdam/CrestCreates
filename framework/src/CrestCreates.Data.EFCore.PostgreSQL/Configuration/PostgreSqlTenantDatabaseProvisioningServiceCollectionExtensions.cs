using System;
using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Data.EFCore.PostgreSQL.DatabaseProviders.PostgreSQL;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.EFCore.PostgreSQL.Configuration;

public static class PostgreSqlTenantDatabaseProvisioningServiceCollectionExtensions
{
    public static IServiceCollection AddCrestCreatesPostgreSqlTenantDatabaseProvisioning(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<PostgreSqlTenantDatabaseProvisioner>();
        services.AddScoped<ITenantDatabaseProvisioner, PostgreSqlTenantDatabaseProvisioner>();

        return services;
    }
}
