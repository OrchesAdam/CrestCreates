using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.EventBus.Local;

public class DefaultLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly ILocalEventDispatcher _dispatcher;

    public DefaultLocalEventBus(ILocalEventDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        return _dispatcher.DispatchAsync(@event, cancellationToken);
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        return _dispatcher.DispatchAsync(@event, cancellationToken);
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
