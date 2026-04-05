using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Application.Permissions;

public static class PermissionSyncServiceCollectionExtensions
{
    public static IServiceCollection AddPermissionSync(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<PermissionSyncOptions>? configure = null)
    {
        services.Configure<PermissionSyncOptions>(
            configuration.GetSection(PermissionSyncOptions.SectionName));

        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<IPermissionSyncService, PermissionSyncService>();
        services.AddHostedService<PermissionSyncHostedService>();

        return services;
    }
}
