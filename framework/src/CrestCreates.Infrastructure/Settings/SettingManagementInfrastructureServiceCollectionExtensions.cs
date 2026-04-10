using CrestCreates.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Infrastructure.Settings;

public static class SettingManagementInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSettingManagementInfrastructure(this IServiceCollection services)
    {
        services.TryAddScoped<ISettingEncryptionService, SettingEncryptionService>();
        return services;
    }
}
