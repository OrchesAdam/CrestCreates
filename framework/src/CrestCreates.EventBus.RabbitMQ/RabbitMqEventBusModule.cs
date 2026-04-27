using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using CrestCreates.Modularity;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.EventBus.RabbitMQ;

public class RabbitMqEventBusModule : ModuleBase
{
    private readonly IConfiguration _configuration;

    public RabbitMqEventBusModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public override void OnConfigureServices(IServiceCollection services)
    {
        // Configure options
        services.Configure<RabbitMqOptions>(_configuration.GetSection("RabbitMQ"));

        // Register connection pool
        services.AddSingleton<RabbitMqConnectionPool>();

        // Register publisher
        services.AddSingleton<Publishing.RabbitMqPublisher>();

        // Register event bus
        services.AddSingleton<IEventBus, RabbitMqEventBus>();
    }
}
