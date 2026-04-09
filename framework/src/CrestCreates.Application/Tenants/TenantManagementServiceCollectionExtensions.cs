using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.Tenants;

public static class TenantManagementServiceCollectionExtensions
{
    public static IServiceCollection AddTenantManagement(this IServiceCollection services)
    {
        services.TryAddScoped<ITenantAppService, TenantAppService>();
        return services;
    }
}
