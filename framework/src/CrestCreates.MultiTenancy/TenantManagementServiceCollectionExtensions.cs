using CrestCreates.Domain.Permission;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.MultiTenancy;

public static class TenantManagementServiceCollectionExtensions
{
    public static IServiceCollection AddTenantManagementCore(this IServiceCollection services)
    {
        services.TryAddScoped<ITenantManager, TenantManager>();
        return services;
    }
}
