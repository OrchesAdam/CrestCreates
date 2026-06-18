using CrestCreates.Application.Contracts.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.Permissions;

public static class PermissionManagementServiceCollectionExtensions
{
    public static IServiceCollection AddPermissionManagement(this IServiceCollection services)
    {
        services.TryAddScoped<IPermissionGrantAppService, PermissionGrantAppService>();
        return services;
    }
}
