using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.MultiTenancy;
using CrestCreates.MultiTenancy.Abstract;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.Tenants;

public static class TenantManagementServiceCollectionExtensions
{
    public static IServiceCollection AddTenantManagement(this IServiceCollection services)
    {
        services.TryAddScoped<CrestCreates.Application.Contracts.Interfaces.ITenantAppService, TenantAppService>();
        services.TryAddEnumerable(ServiceDescriptor.Transient<IPermissionDefinitionProvider, TenantManagementPermissionDefinitionProvider>());
        services.TryAddScoped<TenantInitializationOrchestrator>();
        services.TryAddScoped<ITenantSettingDefaultsSeeder, TenantSettingDefaultsSeeder>();
        services.TryAddScoped<ITenantFeatureDefaultsSeeder, TenantFeatureDefaultsSeeder>();
        services.TryAddScoped<ITenantInitializationEventSink, TenantInitializationEventSink>();
        return services;
    }
}
