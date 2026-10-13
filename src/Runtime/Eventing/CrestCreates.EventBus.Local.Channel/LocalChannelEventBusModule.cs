using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Local.Channel;

[CrestModule]
public class LocalChannelEventBusModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddCrestLocalChannelEventDispatch();
    }
}
