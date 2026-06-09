using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
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
        Microsoft.Extensions.Options.IOptions<RabbitMqOptions> options,
        IEventValidator validator) : base(validator)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _options = options.Value;
    }

    public override async Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventName = EventNamingConvention.GetRoutingKey(@event.GetType());
        ValidateEvent(eventName, @event);

        var routingKey = EventNamingConvention.GetRoutingKey(@event.GetType());
        await _publisher.PublishAsync(@event, _options.DefaultExchange, routingKey, null, cancellationToken);
    }

    public override async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventName = EventNamingConvention.GetRoutingKey<TEvent>();
        ValidateEvent(eventName, @event);

        var routingKey = EventNamingConvention.GetRoutingKey<TEvent>();
        await _publisher.PublishAsync(@event, _options.DefaultExchange, routingKey, null, cancellationToken);
    }

    public override void Subscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Runtime subscription is not supported. Use the compile-time [RabbitMqSubscribe] attribute instead.");
    }

    public override void Unsubscribe<TEvent, THandler>()
    {
        throw new NotSupportedException(
            "Runtime unsubscription is not supported. Subscriptions are managed at compile time.");
    }
}