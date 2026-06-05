using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Logging.Extensions;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.Logging.Modules;

[CrestModule]
public class LoggingModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddCrestLogging(_ => { });
    }
}
