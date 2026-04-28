using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.HealthCheck.Modules;

[CrestModule]
public class HealthCheckModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        base.OnConfigureServices(services);
    }
}