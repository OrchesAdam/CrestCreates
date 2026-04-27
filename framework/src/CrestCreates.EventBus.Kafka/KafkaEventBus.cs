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

    public KafkaEventBus(
        KafkaPublisher publisher,
        Microsoft.Extensions.Options.IOptions<KafkaOptions> options)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options.Value;
    }

    public override async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = @event.GetType();
        var topic = _options.DefaultTopic;

        await _publisher.PublishAsync(
            topic,
            @event,
            key: eventType.Name,
            headers: null,
            cancellationToken: cancellationToken);
    }

    public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = typeof(TEvent);
        var topic = _options.DefaultTopic;

        await _publisher.PublishAsync(
            topic,
            @event,
            key: eventType.Name,
            headers: null,
            cancellationToken: cancellationToken);
    }

    public override void Subscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Kafka subscriptions are discovered at compile-time via [KafkaSubscribe] attribute. " +
            "Mark your handler method with [KafkaSubscribe(\"topic-name\")] to register a subscription.");
    }

    public override void Unsubscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Kafka subscriptions are managed at compile-time and cannot be dynamically unsubscribed.");
    }
}
