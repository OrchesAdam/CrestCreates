using System;
using CrestCreates.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Modularity
{
    /// <summary>
    /// 
    /// </summary>
    public interface ICrestCreatesModule
    {
        void ConfigureServices(IServiceCollection services);
        void Initialize(IServiceProvider serviceProvider);
        void Shutdown(IServiceProvider serviceProvider);
    }
}