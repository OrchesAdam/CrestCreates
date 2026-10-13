using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CrestCreates.Modularity;
using CrestCreates.Domain.Shared.Attributes;
using CrestCreates.EventBus.RabbitMQ.Extensions;

namespace CrestCreates.EventBus.RabbitMQ;

[CrestModule]
public class RabbitMqEventBusModule : ModuleBase
{
    private readonly IConfiguration _configuration;

    public RabbitMqEventBusModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public override void OnConfigureServices(IServiceCollection services)
        => services.AddRabbitMqEventBus(_configuration.GetSection("RabbitMQ"));
}
