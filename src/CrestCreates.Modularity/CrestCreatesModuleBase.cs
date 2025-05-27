using System;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Modularity
{
    public abstract class CrestCreatesModuleBase : ICrestCreatesModule
    {
        public virtual void ConfigureServices(IServiceCollection services)
        {
        }

        public virtual void Initialize(IServiceProvider serviceProvider)
        {
        }

        public virtual void Shutdown(IServiceProvider serviceProvider)
        {
        }
    }
}