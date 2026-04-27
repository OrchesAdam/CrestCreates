using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;

namespace CrestCreates.EventBus.Kafka;

/// <summary>
/// Kafka-based distributed event bus implementation.
/// </summary>
public class KafkaEventBus : DistributedEventBusBase
{
    public override Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("KafkaEventBus implementation pending.");
    }

    public override Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("KafkaEventBus implementation pending.");
    }

    public override void Subscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Kafka subscriptions are discovered at compile-time via [KafkaSubscribe] attribute. " +
            "Mark your handler method with [KafkaSubscribe(\"topic\")] to register a subscription.");
    }

    public override void Unsubscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Kafka subscriptions are managed at compile-time and cannot be dynamically unsubscribed.");
    }
}
