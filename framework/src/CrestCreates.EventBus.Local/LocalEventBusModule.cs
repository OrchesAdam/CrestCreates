using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local;

[CrestModule]
public class LocalEventBusModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<LocalEventBusOptions>();
        services.AddSingleton<LocalDeadLetterOptions>();
        services.AddScoped<ILocalEventDispatcher, DefaultLocalEventDispatcher>();
        services.AddScoped<ILocalEventBus, DefaultLocalEventBus>();
        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, DefaultLocalEventBus>();
        services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();
    }
}
