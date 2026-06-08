using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Kafka.Options;
using CrestCreates.EventBus.Kafka.Publishing;

namespace CrestCreates.EventBus.Kafka;

public class KafkaEventBus : DistributedEventBusBase
{
    private readonly KafkaPublisher _publisher;
    private readonly KafkaOptions _options;

    public KafkaEventBus(KafkaPublisher publisher, Microsoft.Extensions.Options.IOptions<KafkaOptions> options)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options.Value;
    }

    public override async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var topic = EventNamingConvention.GetTopic(@event.GetType());
        var key = EventNamingConvention.GetRoutingKey(@event.GetType());
        await _publisher.PublishAsync(topic, @event, key: key, headers: null, cancellationToken: cancellationToken);
    }

    public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var topic = EventNamingConvention.GetTopic<TEvent>();
        var key = EventNamingConvention.GetRoutingKey<TEvent>();
        await _publisher.PublishAsync(topic, @event, key: key, headers: null, cancellationToken: cancellationToken);
    }

    public override void Subscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Runtime subscription is not supported. Use the compile-time [KafkaSubscribe] attribute instead.");
    }

    public override void Unsubscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Runtime unsubscription is not supported. Subscriptions are managed at compile time.");
    }
}