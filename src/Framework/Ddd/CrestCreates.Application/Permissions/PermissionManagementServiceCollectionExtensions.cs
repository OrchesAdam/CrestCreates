using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.Permissions;

public static class PermissionManagementServiceCollectionExtensions
{
    public static IServiceCollection AddPermissionManagement(this IServiceCollection services)
    {
        services.TryAddScoped<IPermissionGrantAppService, PermissionGrantAppService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IPermissionDefinitionProvider, PermissionManagementPermissionDefinitionProvider>());
        return services;
    }
}
