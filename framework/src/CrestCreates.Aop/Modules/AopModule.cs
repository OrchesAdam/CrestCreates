using CrestCreates.Aop.Extensions;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.Aop.Modules;

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
