using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Modularity
{
    public abstract class CrestCreatesModuleBase : ICrestCreatesModule
    {
        
        public virtual void OnPreApplicationInitialization()
        {
        }

        public virtual Task OnPreApplicationInitializationAsync()
        {
            return Task.CompletedTask;
        }

        public virtual void OnPostApplicationInitialization()
        {
        }

        public virtual Task OnPostApplicationInitializationAsync()
        {
            return Task.CompletedTask;
        }

        public virtual void OnPreApplicationShutdown()
        {
        }

        public virtual Task OnPreApplicationShutdownAsync()
        {
            return Task.CompletedTask;
        }

        public virtual void OnPostApplicationShutdown()
        {
        }

        public virtual Task OnPostApplicationShutdownAsync()
        {
            return Task.CompletedTask;
        }

        public virtual void ConfigureServices(IServiceCollection services)
        {
            
        }

        public virtual Task ConfigureServicesAsync(IServiceCollection services)
        {
            return Task.CompletedTask;
        }
    }
}