using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local.Channel;

/// <summary>
/// Channel-flavored local event bus. Membership and validation happen before enqueue; a
/// successful return means the event was enqueued — nothing more. Handler failures are
/// observed by the background consumer.
/// </summary>
public class BackgroundChannelLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly ChannelLocalEventQueue _queue;
    private readonly ILocalEventDispatchRegistry _registry;
    private readonly IEventValidator? _validator;
    private readonly IEventRegistry? _eventRegistry;

    public BackgroundChannelLocalEventBus(
        ChannelLocalEventQueue queue,
        ILocalEventDispatchRegistry registry,
        IEventValidator? validator = null,
        IEventRegistry? eventRegistry = null)
    {
        _queue = queue;
        _registry = registry;
        _validator = validator;
        _eventRegistry = eventRegistry;
    }

    public Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        var eventType = @event.GetType();
        if (!_registry.TryGet(eventType, out var binding))
        {
            throw new UnknownLocalEventException(eventType);
        }

        _validator?.ValidateOrThrow(ResolveCanonicalName(binding), @event);
        return _queue.EnqueueAsync(@event, cancellationToken).AsTask();
    }

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        return PublishAsync((ILocalEvent)@event, cancellationToken);
    }

    Task IEventBus.PublishAsync(IDomainEvent @event, CancellationToken cancellationToken)
        => PublishAsync((ILocalEvent)@event, cancellationToken);

    Task IEventBus.PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        => PublishAsync((ILocalEvent)@event, cancellationToken);

    void IEventBus.Subscribe<TEvent, THandler>()
    {
    }

    void IEventBus.Unsubscribe<TEvent, THandler>()
    {
    }

    private string ResolveCanonicalName(LocalEventBinding binding)
    {
        if (_eventRegistry is { State: RegistryState.Built })
        {
            var descriptor = _eventRegistry.GetByPayloadType(binding.EventType);
            if (descriptor is not null)
            {
                return descriptor.Name;
            }
        }

        return binding.LocalName;
    }
}
