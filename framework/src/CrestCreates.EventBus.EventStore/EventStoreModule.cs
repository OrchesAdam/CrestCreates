using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;

namespace CrestCreates.EventBus.EventStore
{
    public class EventStoreModule : ModuleBase
    {
        public override void OnConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<CrestCreates.EventBus.Abstract.IEventStoreSerializer, EventStoreJsonSerializer>();
            services.AddSingleton<CrestCreates.EventBus.Abstract.IEventRetryStore, InMemoryEventRetryStore>();
            services.AddScoped<EventRetryService>();
        }
    }
}