using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Infrastructure.Modularity
{
    public abstract class ModuleBase
    {
        public virtual void PreInitialize()
        {
        }

        public virtual void Initialize()
        {
        }

        public virtual void PostInitialize()
        {
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
        }

        public virtual void OnApplicationInitialization(IHost host)
        {
        }
    }
}
