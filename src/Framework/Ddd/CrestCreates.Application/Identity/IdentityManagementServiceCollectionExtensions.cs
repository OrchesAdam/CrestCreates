using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.Identity;

public static class IdentityManagementServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityManagement(this IServiceCollection services)
    {
        services.TryAddScoped<IUserAppService, UserAppService>();
        services.TryAddScoped<IRoleAppService, RoleAppService>();
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IPermissionDefinitionProvider, IdentityManagementPermissionDefinitionProvider>());
        return services;
    }
}
