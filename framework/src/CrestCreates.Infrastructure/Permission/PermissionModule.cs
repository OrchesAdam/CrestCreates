using CrestCreates.Domain.DataFilter;
using CrestCreates.Infrastructure.Authorization;
using CrestCreates.Infrastructure.DataFilter;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Infrastructure.Permission;

public static class PermissionServiceCollectionExtensions
{
    public static IServiceCollection AddPermissionServices(this IServiceCollection services)
    {
        services.AddScoped<ICurrentUser, CurrentUser>();
        
        services.AddScoped<DataFilterState>();
        services.AddScoped<IDataPermissionFilter, DataPermissionFilter>();
        
        services.AddScoped<IPermissionChecker, PermissionChecker>();
        services.AddScoped<IPermissionStore, InMemoryPermissionStore>();
        services.AddScoped<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
        
        services.AddScoped<IOrganizationHierarchyService, OrganizationHierarchyService>();
        
        return services;
    }
}
