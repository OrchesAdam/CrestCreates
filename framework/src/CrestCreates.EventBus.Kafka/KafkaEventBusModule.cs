using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;

namespace CrestCreates.EventBus.Kafka;

[CrestModule]
public class KafkaEventBusModule : ModuleBase
{
    public override void OnConfigureServices(IServiceCollection services)
    {
        // Module provides defaults - user should call AddKafkaEventBus<TContext>
        // in their startup with their specific JsonSerializerContext
    }
}