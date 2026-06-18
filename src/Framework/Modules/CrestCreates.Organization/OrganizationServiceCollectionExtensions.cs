using CrestCreates.Organization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Organization;

public static class OrganizationServiceCollectionExtensions
{
    public static IServiceCollection AddOrganizationKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IOrganizationStore, InMemoryOrganizationStore>();
        services.TryAddScoped<IOrganizationHierarchyService, DefaultOrganizationHierarchyService>();
        services.TryAddScoped<IOrganizationIdentityService, DefaultOrganizationIdentityService>();
        services.TryAddScoped<IDataPermissionScopeProvider, DefaultDataPermissionScopeProvider>();
        services.TryAddSingleton<IDataPermissionScopeRuleStore, InMemoryDataPermissionScopeRuleStore>();
        services.TryAddSingleton<IDataPermissionFilterBuilder, DefaultDataPermissionFilterBuilder>();
        services.TryAddScoped<IDataPermissionRuntime, DefaultDataPermissionRuntime>();
        services.TryAddSingleton<IOrganizationContextAccessor, NullOrganizationContextAccessor>();
        return services;
    }
}
