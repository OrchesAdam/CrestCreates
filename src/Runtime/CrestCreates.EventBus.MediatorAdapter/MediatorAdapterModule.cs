using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.EventBus.Local;
using CrestCreates.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.MediatorAdapter;

[CrestModule]
public class MediatorAdapterModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        services.AddScoped<ILocalEventBus, MediatorLocalEventBus>();
        services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, MediatorLocalEventBus>();
        services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();
    }
}
