using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Modularity;
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
    }
}