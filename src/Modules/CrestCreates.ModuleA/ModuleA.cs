using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Infrastructure.Modularity;

namespace CrestCreates.ModuleA
{
    [Module(DependsOn = new[] { 
        "CrestCreates.Domain",
        "CrestCreates.Application",
        "CrestCreates.Infrastructure"
    })]
    public class ModuleA : ModuleBase
    {
        public override void PreInitialize()
        {
            // Module initialization logic can be added here
        }

        public override void Initialize()
        {
            // Register services by convention
        }

        public override void PostInitialize()
        {
            // Logic to execute after the module has been initialized
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            // Configure module services
        }

        public override void OnApplicationInitialization(IHost host)
        {
            // Logic to execute when the application is initializing
        }
    }
}