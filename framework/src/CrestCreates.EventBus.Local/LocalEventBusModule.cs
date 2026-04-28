using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;
using System.Reflection;

namespace CrestCreates.EventBus.Local
{
    [CrestModule]
    public class LocalEventBusModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, LocalEventBus>();
            services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();
        }
    }
}