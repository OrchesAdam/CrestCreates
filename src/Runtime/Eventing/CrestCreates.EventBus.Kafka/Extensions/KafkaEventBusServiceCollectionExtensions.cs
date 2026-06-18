using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Kafka.Connection;
using CrestCreates.EventBus.Kafka.Consuming;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CrestCreates.EventBus.Kafka.Extensions;

public static class KafkaEventBusServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaEventBus<TContext>(
        this IServiceCollection services,
        Action<KafkaOptions>? configure = null)
        where TContext : JsonSerializerContext, new()
    {
        // Register options
        services.Configure<KafkaOptions>(configure ?? (_ => { }));

        // Register JsonSerializerContext as singleton
        services.AddSingleton<JsonSerializerContext>(sp => new TContext());

        // Register producer pool as singleton
        services.AddSingleton<KafkaProducerPool>();

        // Register publisher as transient
        services.AddTransient<KafkaPublisher>();

        // Register event bus
        services.AddSingleton<IEventBus, KafkaEventBus>();

        // Register consumer as hosted service
        services.AddHostedService<KafkaConsumer>();

        return services;
    }

    public static IServiceCollection AddKafkaEventBus(
        this IServiceCollection services,
        JsonSerializerContext jsonContext,
        Action<KafkaOptions>? configure = null)
    {
        // Register options
        services.Configure<KafkaOptions>(configure ?? (_ => { }));

        // Register JsonSerializerContext as singleton
        services.AddSingleton(jsonContext);

        // Register producer pool as singleton
        services.AddSingleton<KafkaProducerPool>();

        // Register publisher as transient
        services.AddTransient<KafkaPublisher>();

        // Register event bus
        services.AddSingleton<IEventBus, KafkaEventBus>();

        // Register consumer as hosted service
        services.AddHostedService<KafkaConsumer>();

        return services;
    }
}