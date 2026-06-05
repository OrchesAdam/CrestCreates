using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Local.Channel;

[CrestModule]
public class LocalChannelEventBusModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<ChannelLocalEventQueue>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, BackgroundChannelLocalEventBus>();
        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, BackgroundChannelLocalEventBus>();
        services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();
        services.AddHostedService<BackgroundChannelLocalEventBusConsumer>();
    }
}
