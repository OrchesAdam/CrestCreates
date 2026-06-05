using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;
using System;
using System.Threading.Tasks;

namespace CrestCreates.AspNetCore
{
    [CrestModule]
    public class AspNetCoreModule : ModuleBase
    {
        public override string Name => "AspNetCoreModule";

        public override Task OnPreInitializeAsync()
        {
            Console.WriteLine("[AspNetCoreModule] OnPreInitialize");
            return Task.CompletedTask;
        }

        public override Task OnInitializeAsync()
        {
            Console.WriteLine("[AspNetCoreModule] OnInitialize");
            return Task.CompletedTask;
        }

        public override Task OnPostInitializeAsync()
        {
            Console.WriteLine("[AspNetCoreModule] OnPostInitialize");
            return Task.CompletedTask;
        }

        public override void OnConfigureServices(IServiceCollection services)
        {
            Console.WriteLine("[AspNetCoreModule] OnConfigureServices");
        }

        public override Task OnApplicationInitializationAsync(IHost host)
        {
            Console.WriteLine("[AspNetCoreModule] OnApplicationInitialization");
            return Task.CompletedTask;
        }
    }
}