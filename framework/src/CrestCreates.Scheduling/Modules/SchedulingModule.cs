using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using CrestCreates.Scheduling.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Scheduling.Modules;

[CrestModule]
public class SchedulingModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);

        services.AddSingleton<IJobFailureHandler, DefaultJobFailureHandler>();
    }
}
