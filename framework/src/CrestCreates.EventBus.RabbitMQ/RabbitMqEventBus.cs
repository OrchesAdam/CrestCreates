using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Options;

namespace CrestCreates.EventBus.RabbitMQ;

public class RabbitMqEventBus : DistributedEventBusBase
{
    private readonly RabbitMqPublisher _publisher;
    private readonly RabbitMqOptions _options;

    public RabbitMqEventBus(
        RabbitMqPublisher publisher,
        Microsoft.Extensions.Options.IOptions<RabbitMqOptions> options)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options.Value;
    }

    public override async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = @event.GetType();
        var routingKey = eventType.Name;

        await _publisher.PublishAsync(
            @event,
            _options.DefaultExchange,
            routingKey,
            null,
            cancellationToken);
    }

    public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = typeof(TEvent);
        var routingKey = eventType.Name;

        await _publisher.PublishAsync(
            @event,
            _options.DefaultExchange,
            routingKey,
            null,
            cancellationToken);
    }

    public override void Subscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "RabbitMQ subscriptions are discovered at compile-time via [RabbitMqSubscribe] attribute. " +
            "Mark your handler method with [RabbitMqSubscribe(\"EventTypeName\")] to register a subscription.");
    }

    public override void Unsubscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "RabbitMQ subscriptions are managed at compile-time and cannot be dynamically unsubscribed.");
    }
}
