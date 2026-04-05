using CrestCreates.Aop.Abstractions.Interfaces;
using CrestCreates.Aop.Abstractions.Options;
using CrestCreates.Aop.Configuration;
using CrestCreates.Aop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Aop.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCrestAop(this IServiceCollection services)
    {
        services.AddOptions<AopOptions>();
        
        services.AddScoped<IAuditLogger, DefaultAuditLogger>();
        services.AddScoped<IDataFilterContext, DefaultDataFilterContext>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddSingleton<ICacheKeyGenerator, DefaultCacheKeyGenerator>();
        services.AddSingleton<IDynamicConfigurationProvider, MemoryConfigurationProvider>();

        return services;
    }

    public static IServiceCollection AddCrestAop(this IServiceCollection services, Action<AopOptions> configure)
    {
        services.Configure(configure);
        return services.AddCrestAop();
    }

    public static IServiceCollection AddCrestAopWithDistributedCache(this IServiceCollection services, Action<AopOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }

        services.AddOptions<AopOptions>();
        
        services.AddScoped<IAuditLogger, DefaultAuditLogger>();
        services.AddScoped<IDataFilterContext, DefaultDataFilterContext>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddSingleton<ICacheKeyGenerator, DefaultCacheKeyGenerator>();
        services.AddScoped<IDynamicConfigurationProvider, DistributedCacheConfigurationProvider>();

        return services;
    }
}
