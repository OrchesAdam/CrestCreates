using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Infrastructure.Modularity;
using System.Reflection;

namespace CrestCreates.EventBus.Local
{
    public class LocalEventBusModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, LocalEventBus>();
            services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();
        }
    }
}