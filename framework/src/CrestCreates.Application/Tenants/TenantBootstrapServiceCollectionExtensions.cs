using CrestCreates.Domain.Permission;
using CrestCreates.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Application.Tenants;

public static class TenantBootstrapServiceCollectionExtensions
{
    public static IServiceCollection AddTenantBootstrapper(this IServiceCollection services)
    {
        services.Configure<TenantBootstrapOptions>(options =>
        {
        });

        services.AddScoped<ITenantBootstrapper, TenantBootstrapper>();

        return services;
    }
}
