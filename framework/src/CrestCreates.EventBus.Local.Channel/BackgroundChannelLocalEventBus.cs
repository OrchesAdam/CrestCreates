using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local.Channel;

public class BackgroundChannelLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly ChannelLocalEventQueue _queue;

    public BackgroundChannelLocalEventBus(ChannelLocalEventQueue queue)
    {
        _queue = queue;
    }

    public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
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
