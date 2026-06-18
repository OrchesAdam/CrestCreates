using System.Threading.Tasks;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using CrestCreates.Scheduling.Services;
using CrestCreates.Scheduling.Quartz.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Scheduling.Quartz.Modules;

[CrestModule]
public class SchedulingQuartzModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISchedulerService, QuartzSchedulerService>();
    }

    public override async Task OnApplicationInitializationAsync(IHost host)
    {
        var schedulerService = host.Services.GetRequiredService<ISchedulerService>();
        await schedulerService.StartAsync();
    }
}
