using CrestCreates.Domain.Shared.Attributes;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.Configuration.Services;

namespace CrestCreates.Configuration.Modules
{
    [CrestModule]
    public class ConfigurationModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);

            // 注册配置服务
            services.AddScoped<IConfigurationService, ConfigurationService>();
        }
    }
}