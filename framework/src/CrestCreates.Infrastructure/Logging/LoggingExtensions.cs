using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.Infrastructure.Logging
{
    public static class LoggingExtensions
    {
        public static IServiceCollection AddSerilogLogging(this IServiceCollection services, Action<LoggingConfiguration> configure = null)
        {
            var configuration = new LoggingConfiguration();
            configure?.Invoke(configuration);

            services.AddSingleton(configuration);
            services.AddSingleton<ILoggerProviderAdapter, SerilogLoggerAdapter>();
            services.AddLogging(builder => {
                builder.ClearProviders();
                builder.AddProvider(services.BuildServiceProvider().GetRequiredService<ILoggerProviderAdapter>());
            });

            return services;
        }

        public static IServiceCollection AddSerilogLogging(this IServiceCollection services, LoggingConfiguration configuration)
        {
            services.AddSingleton(configuration);
            services.AddSingleton<ILoggerProviderAdapter, SerilogLoggerAdapter>();
            services.AddLogging(builder => {
                builder.ClearProviders();
                builder.AddProvider(services.BuildServiceProvider().GetRequiredService<ILoggerProviderAdapter>());
            });

            return services;
        }
    }
}