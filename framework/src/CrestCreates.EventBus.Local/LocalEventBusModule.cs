using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.EventBus.Local
{
    [CrestModule]
    public class LocalEventBusModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(LocalEventBusModule).Assembly));
            services.AddScoped<CrestCreates.EventBus.Abstract.IEventBus, LocalEventBus>();
            services.AddScoped<CrestCreates.Domain.DomainEvents.IDomainEventPublisher, DomainEventPublisher>();
        }
    }
}