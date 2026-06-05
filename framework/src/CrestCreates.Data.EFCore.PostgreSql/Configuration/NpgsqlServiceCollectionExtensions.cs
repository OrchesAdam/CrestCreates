using CrestCreates.AspNetCore.Authentication.OpenIddict;
using CrestCreates.Data.EFCore.Configuration;
using CrestCreates.Data.EFCore.PostgreSql.DatabaseProviders.PostgreSQL;
using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Data.EFCore.PostgreSql.Configuration;

public static class NpgsqlServiceCollectionExtensions
{
    /// <summary>
    /// Registers PostgreSQL-specific EF Core services:
    /// <list type="bullet">
    ///   <item><see cref="IEfCoreDbContextOptionsContributor"/> with UseNpgsql</item>
    ///   <item><see cref="OpenIddictDbContext"/> configured with Npgsql</item>
    ///   <item><see cref="ITenantDatabaseInitializer"/> with PostgreSQL CREATE DATABASE support</item>
    /// </list>
    /// Call this before <c>AddOpenIddictServer()</c> and after <c>AddDbContext&lt;TYourDbContext&gt;()</c>.
    /// </summary>
    public static IServiceCollection AddCrestCreatesEfCorePostgreSql(this IServiceCollection services)
    {
        // Register the Npgsql options contributor so that OpenIddictDbContext and other
        // framework DbContexts can resolve their provider configuration via DI.
        services.AddSingleton<IEfCoreDbContextOptionsContributor, NpgsqlDbContextOptionsContributor>();

        // Register OpenIddictDbContext with Npgsql configuration
        services.AddDbContext<OpenIddictDbContext>((serviceProvider, optionsBuilder) =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("Default");
            optionsBuilder.UseNpgsql(connectionString);
        });

        // Register PostgreSQL tenant database provisioner (CREATE DATABASE support)
        services.AddScoped<PostgreSqlTenantDatabaseProvisioner>();
        services.AddScoped<ITenantDatabaseProvisioner, PostgreSqlTenantDatabaseProvisioner>();

        return services;
    }
}
