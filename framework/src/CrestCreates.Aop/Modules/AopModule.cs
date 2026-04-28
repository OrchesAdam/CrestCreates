using CrestCreates.Aop.Extensions;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Aop.Modules;

[CrestModule]
public class AopModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddCrestAop();
    }

    public override void OnApplicationInitialization(IHost host)
    {
    }
}
