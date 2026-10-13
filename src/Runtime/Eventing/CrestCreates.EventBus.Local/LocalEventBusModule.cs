using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.EventBus.Local;

[CrestModule]
public class LocalEventBusModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddCrestLocalEventDispatch();
    }
}
