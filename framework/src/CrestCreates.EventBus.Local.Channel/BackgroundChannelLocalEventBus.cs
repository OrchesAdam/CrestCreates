using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local.Channel;

public class BackgroundChannelLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly ChannelLocalEventQueue _queue;
    private readonly IEventValidator _validator;

    public BackgroundChannelLocalEventBus(
        ChannelLocalEventQueue queue,
        IEventValidator validator)
    {
        _queue = queue;
        _validator = validator;
    }

    public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        _validator.ValidateOrThrow(@event.GetType().Name, @event);
        return _queue.EnqueueAsync(@event, cancellationToken).AsTask();
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        return PublishAsync((ILocalEvent)@event, cancellationToken);
    }

    Task IEventBus.PublishAsync(IDomainEvent @event, CancellationToken cancellationToken)
    {
        return PublishAsync(@event, cancellationToken);
    }

    Task IEventBus.PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
    {
        return PublishAsync(@event, cancellationToken);
    }

    void IEventBus.Subscribe<TEvent, THandler>()
    {
    }

    void IEventBus.Unsubscribe<TEvent, THandler>()
    {
    }
}
