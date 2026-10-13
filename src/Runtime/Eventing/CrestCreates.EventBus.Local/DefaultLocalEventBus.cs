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

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Synchronous local event bus. Membership is resolved in the unified dispatch registry before
/// validation and dispatch; handler failures propagate to the caller and are additionally
/// recorded through the shared typed dead-letter recorder when the event is replayable.
/// </summary>
public class DefaultLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly ILocalEventDispatchRegistry _registry;
    private readonly ILocalEventDispatcher _dispatcher;
    private readonly ILocalEventFailureRecorder? _recorder;
    private readonly IEventValidator? _validator;
    private readonly IEventRegistry? _eventRegistry;
    private readonly ILogger<DefaultLocalEventBus>? _logger;

    public DefaultLocalEventBus(
        ILocalEventDispatchRegistry registry,
        ILocalEventDispatcher dispatcher,
        IEventValidator? validator = null,
        ILocalEventFailureRecorder? recorder = null,
        IEventRegistry? eventRegistry = null,
        ILogger<DefaultLocalEventBus>? logger = null)
    {
        _registry = registry;
        _dispatcher = dispatcher;
        _validator = validator;
        _recorder = recorder;
        _eventRegistry = eventRegistry;
        _logger = logger;
    }

    public async Task PublishAsync(ILocalEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        var eventType = @event.GetType();
        if (!_registry.TryGet(eventType, out var binding))
        {
            throw new UnknownLocalEventException(eventType);
        }

        _validator?.ValidateOrThrow(ResolveCanonicalName(binding), @event);

        try
        {
            await _dispatcher.DispatchAsync(@event, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (!IsCallerCancellation(failure, cancellationToken))
        {
            if (_recorder is not null)
            {
                var outcome = await _recorder
                    .TryRecordAsync(binding, @event, failure, cancellationToken)
                    .ConfigureAwait(false);

                if (outcome.Recorded)
                {
                    _logger?.LogInformation(
                        "Recorded failed local event '{EventName}' to the dead-letter store.",
                        binding.LocalName);
                }
                else if (outcome.Problem is not null)
                {
                    _logger?.LogWarning(
                        "Local event '{EventName}' failed and was not recorded to the dead-letter store: {Problem}",
                        binding.LocalName, outcome.Problem);
                }
            }

            throw;
        }
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : ILocalEvent
    {
        ArgumentNullException.ThrowIfNull(@event);
        await PublishAsync((ILocalEvent)@event, cancellationToken).ConfigureAwait(false);
    }

    Task IEventBus.PublishAsync(IDomainEvent @event, CancellationToken cancellationToken)
        => PublishAsync(@event, cancellationToken);

    Task IEventBus.PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        => PublishAsync(@event, cancellationToken);

    void IEventBus.Subscribe<TEvent, THandler>()
    {
    }

    void IEventBus.Unsubscribe<TEvent, THandler>()
    {
    }

    private static bool IsCallerCancellation(Exception failure, CancellationToken cancellationToken)
        => failure is OperationCanceledException && cancellationToken.IsCancellationRequested;

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
