using CrestCreates.Domain.Settings;
using CrestCreates.Data.EFCore.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Data.EFCore.Settings;

public static class SettingManagementEfCoreServiceCollectionExtensions
{
    public static IServiceCollection AddSettingManagementEfCore(this IServiceCollection services)
    {
        services.TryAddScoped<ISettingRepository, SettingRepository>();
        return services;
    }
}
