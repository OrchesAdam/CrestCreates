using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Modularity;
using CrestCreates.DynamicApi;
using System;

namespace CrestCreates.AspNetCore
{
    public static class AspNetCoreModuleExtensions
    {
        public static IHostBuilder UseAspNetCore(this IHostBuilder hostBuilder)
        {
            Console.WriteLine("[AspNetCoreModuleExtensions] UseAspNetCore called");
            return hostBuilder;
        }

        public static IApplicationBuilder UseAspNetCore(this IApplicationBuilder app)
        {
            Console.WriteLine("[AspNetCoreModuleExtensions] UseAspNetCore (IApplicationBuilder) called");
            return app;
        }

        public static IServiceCollection AddAspNetCoreServices(this IServiceCollection services)
        {
            Console.WriteLine("[AspNetCoreModuleExtensions] AddAspNetCoreServices called");
            return services;
        }

        public static IServiceCollection AddCrestAspNetCoreDynamicApi(
            this IServiceCollection services,
            Action<DynamicApiOptions>? configure = null)
        {
            return services.AddCrestDynamicApi(configure);
        }

        public static IEndpointRouteBuilder MapCrestAspNetCoreDynamicApi(this IEndpointRouteBuilder endpoints)
        {
            return endpoints.MapCrestDynamicApi();
        }
    }
}
