using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CrestCreates.Modularity;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.EventBus.Kafka;

/// <summary>
/// Module for configuring Kafka distributed event bus.
/// </summary>
public class KafkaEventBusModule : ModuleBase
{
    private readonly IConfiguration _configuration;

    public KafkaEventBusModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public override void OnConfigureServices(IServiceCollection services)
    {
        // Configure options
        services.Configure<KafkaOptions>(_configuration.GetSection("Kafka"));

        // Register event bus
        services.AddSingleton<IEventBus, KafkaEventBus>();
    }
}
