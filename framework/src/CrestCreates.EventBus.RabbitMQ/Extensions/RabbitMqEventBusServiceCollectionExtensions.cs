using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Connection;
using CrestCreates.EventBus.RabbitMQ.Consuming;
using CrestCreates.EventBus.RabbitMQ.Options;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.EventBus.RabbitMQ.Extensions;

public static class RabbitMqEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqEventBus<TContext>(
        this IServiceCollection services,
        Action<RabbitMqOptions>? configure = null)
        where TContext : JsonSerializerContext, new()
    {
        // Register options
        services.Configure<RabbitMqOptions>(configure ?? (_ => { }));

        // Register JsonSerializerContext as singleton
        services.AddSingleton<JsonSerializerContext>(sp => new TContext());

        // Register connection pool as singleton
        services.AddSingleton<RabbitMqConnectionPool>();

        // Register publisher as transient
        services.AddTransient<RabbitMqPublisher>();

        // Register event bus
        services.AddSingleton<IEventBus, RabbitMqEventBus>();

        // Register consumer as hosted service
        services.AddHostedService<RabbitMqConsumer>();

        return services;
    }

    public static IServiceCollection AddRabbitMqEventBus(
        this IServiceCollection services,
        JsonSerializerContext jsonContext,
        Action<RabbitMqOptions>? configure = null)
    {
        // Register options
        services.Configure<RabbitMqOptions>(configure ?? (_ => { }));

        // Register JsonSerializerContext as singleton
        services.AddSingleton(jsonContext);

        // Register connection pool as singleton
        services.AddSingleton<RabbitMqConnectionPool>();

        // Register publisher as transient
        services.AddTransient<RabbitMqPublisher>();

        // Register event bus
        services.AddSingleton<IEventBus, RabbitMqEventBus>();

        // Register consumer as hosted service
        services.AddHostedService<RabbitMqConsumer>();

        return services;
    }
}