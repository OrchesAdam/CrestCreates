using CrestCreates.Logging.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Logging.Extensions;

public static class CrestLoggingServiceCollectionExtensions
{
    public static IServiceCollection AddCrestLogging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CrestLoggingOptions>()
            .Bind(configuration.GetSection(CrestLoggingOptions.SectionName));

        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

        return services;
    }

    public static IServiceCollection AddCrestLogging(
        this IServiceCollection services,
        Action<CrestLoggingOptions> configure)
    {
        services.AddOptions<CrestLoggingOptions>().Configure(configure);
        services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        return services;
    }
}
