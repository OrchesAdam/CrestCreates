using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Modularity
{
    /// <summary>
    /// 
    /// </summary>
    public interface ICrestCreatesModule : IOnPostApplicationInitialization, IOnPostApplicationShutdown, IOnPreApplicationInitialization, IOnPreApplicationShutdown
    {
        void ConfigureServices(IServiceCollection services);
        
        Task ConfigureServicesAsync(IServiceCollection services);
    }
}