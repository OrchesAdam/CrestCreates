using CrestCreates.Domain.Settings;
using CrestCreates.OrmProviders.EFCore.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.OrmProviders.EFCore.Settings;

public static class SettingManagementEfCoreServiceCollectionExtensions
{
    public static IServiceCollection AddSettingManagementEfCore(this IServiceCollection services)
    {
        services.TryAddScoped<ISettingRepository, SettingRepository>();
        return services;
    }
}
