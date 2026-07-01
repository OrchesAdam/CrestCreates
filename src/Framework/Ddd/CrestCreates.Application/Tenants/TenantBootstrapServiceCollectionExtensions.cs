using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.Tenants;

public static class TenantBootstrapServiceCollectionExtensions
{
    public static IServiceCollection AddTenantBootstrapper(this IServiceCollection services)
    {
        services.Configure<TenantBootstrapOptions>(options =>
        {
        });

        services.TryAddScoped<ITenantDataSeedContributor, TenantBootstrapper>();

        return services;
    }
}
