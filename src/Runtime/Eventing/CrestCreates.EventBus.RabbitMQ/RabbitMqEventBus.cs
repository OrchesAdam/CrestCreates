using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.RabbitMQ.Exceptions;
using CrestCreates.EventBus.RabbitMQ.Publishing;
using CrestCreates.EventBus.RabbitMQ.Registration;

namespace CrestCreates.EventBus.RabbitMQ;

/// <summary>
/// Canonical-identity event bus. Both entries share the same frozen publish index: the generic entry
/// looks up typeof(TEvent) and the non-generic entry looks up the actual concrete type — the payload is
/// never erased to the IDomainEvent contract. Undeclared events, missing codecs and identity mismatches
/// fail before any broker I/O.
/// </summary>
public class RabbitMqEventBus : DistributedEventBusBase
{
    private readonly RabbitMqPublisher _publisher;
    private readonly IRabbitMqBindingRegistry _registry;
    private readonly IEventRegistry _eventRegistry;

    public RabbitMqEventBus(
        RabbitMqPublisher publisher,
        IRabbitMqBindingRegistry registry,
        IEventRegistry eventRegistry,
        IEventValidator validator) : base(validator)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _eventRegistry = eventRegistry ?? throw new ArgumentNullException(nameof(eventRegistry));
    }

    public override Task PublishAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return PublishCoreAsync(@event.GetType(), @event, cancellationToken);
    }

    public override Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return PublishCoreAsync(typeof(TEvent), @event, cancellationToken);
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

    private async Task PublishCoreAsync(Type eventType, object @event, CancellationToken cancellationToken)
    {
        if (!_registry.TryGetPublishBinding(eventType, out var binding) || binding is null)
        {
            throw new RabbitMqPublishException(
                DescribeUnpublishable(eventType),
                eventType.FullName,
                correlationId: null);
        }

        ValidateEvent(binding.Descriptor.Name, @event);
        await _publisher.PublishEventAsync(binding, @event, correlationId: null, cancellationToken).ConfigureAwait(false);
    }

    private string DescribeUnpublishable(Type eventType)
    {
        var descriptor = _eventRegistry.GetByPayloadType(eventType);
        if (descriptor is null)
        {
            return $"Event '{eventType.FullName}' has no Active descriptor after the event kernel bootstrap. " +
                   "Declare [CrestEvent(Name = ..., Scope = EventScope.Integration)] on the type and activate the " +
                   "owning assembly's AddEventDescriptors() extension.";
        }

        if (descriptor.Scope != EventScope.Integration)
        {
            return $"Event '{eventType.FullName}' resolves to '{descriptor.Name}' with scope {descriptor.Scope}; " +
                   "RabbitMQ publishes Integration events only.";
        }

        return $"Event '{descriptor.Name}' v{descriptor.Version} has no frozen codec. Activate the owning " +
               $"assembly's AddEventDescriptors() extension (payload assembly: " +
               $"'{descriptor.PayloadType.Assembly.GetName().Name}') or supply an explicit typed codec through " +
               "AddPublishedEvent<TEvent>().";
    }
}
