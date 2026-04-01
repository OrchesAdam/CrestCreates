using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.Scheduling.Services;

namespace CrestCreates.Scheduling.Quartz.Modules
{
    public class SchedulingQuartzModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            base.OnConfigureServices(services);

            // 注册调度服务
            services.AddSingleton<ISchedulerService, CrestCreates.Scheduling.Quartz.Services.SchedulerService>();
        }

        public override void OnApplicationInitialization(Microsoft.Extensions.Hosting.IHost host)
        {
            base.OnApplicationInitialization(host);

            // 启动调度器
            var schedulerService = host.Services.GetRequiredService<ISchedulerService>();
            schedulerService.StartAsync().Wait();
        }
    }
}