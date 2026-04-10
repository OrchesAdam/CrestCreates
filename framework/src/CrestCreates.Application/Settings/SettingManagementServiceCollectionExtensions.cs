using CrestCreates.Application.Contracts.Interfaces;
using CrestCreates.Caching;
using CrestCreates.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Application.Settings;

public static class SettingManagementServiceCollectionExtensions
{
    public static IServiceCollection AddSettingManagement(this IServiceCollection services)
    {
        services.AddCrestCaching();

        services.TryAddSingleton<ISettingDefinitionManager, SettingDefinitionManager>();
        services.TryAddSingleton<SettingValueTypeConverter>();
        services.TryAddScoped<ISettingStore, SettingStore>();
        services.TryAddScoped<ISettingValueResolver, SettingValueResolver>();
        services.TryAddScoped<ISettingProvider, SettingProvider>();
        services.TryAddScoped<ISettingManager, SettingManager>();
        services.TryAddScoped<ISettingDefinitionAppService, SettingDefinitionAppService>();
        services.TryAddScoped<ISettingAppService, SettingAppService>();
        services.TryAddScoped<SettingValueAppServiceMapper>();
        services.TryAddScoped<SettingDefinitionAppServiceMapper>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ISettingDefinitionProvider, CoreSettingDefinitionProvider>());

        return services;
    }

    public static IServiceCollection AddSettingDefinitionProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, ISettingDefinitionProvider
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISettingDefinitionProvider, TProvider>());
        return services;
    }
}
