using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Abstractions;
using Mediator;

namespace CrestCreates.EventBus.MediatorAdapter;

public class MediatorLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly IMediator _mediator;

    public MediatorLocalEventBus(IMediator mediator)
    {
        _mediator = mediator;
    }

    public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event is not INotification notification)
        {
            throw new InvalidOperationException(
                $"Event type '{@event.GetType().FullName}' must implement {nameof(INotification)} to use {nameof(MediatorLocalEventBus)}.");
        }

        return _mediator.Publish(notification, cancellationToken).AsTask();
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
