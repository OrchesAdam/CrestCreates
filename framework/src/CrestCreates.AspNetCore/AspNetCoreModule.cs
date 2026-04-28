using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;
using System;

namespace CrestCreates.AspNetCore
{
    [CrestModule]
    public class AspNetCoreModule : ModuleBase
    {
        public override string Name => "AspNetCoreModule";

        public override void OnPreInitialize()
        {
            Console.WriteLine("[AspNetCoreModule] OnPreInitialize");
        }

        public override void OnInitialize()
        {
            Console.WriteLine("[AspNetCoreModule] OnInitialize");
        }

        public override void OnPostInitialize()
        {
            Console.WriteLine("[AspNetCoreModule] OnPostInitialize");
        }

        public override void OnConfigureServices(IServiceCollection services)
        {
            Console.WriteLine("[AspNetCoreModule] OnConfigureServices");
        }

        public override void OnApplicationInitialization(IHost host)
        {
            Console.WriteLine("[AspNetCoreModule] OnApplicationInitialization");
        }
    }
}