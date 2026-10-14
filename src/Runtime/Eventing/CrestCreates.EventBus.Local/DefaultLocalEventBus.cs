using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Synchronous local event bus. Membership is resolved in the unified dispatch registry, the shared
/// admission core (governance, explicit checks, activated validator) runs in the caller's frame and
/// the context snapshot is captured before any handler runs. Handler failures propagate to the
/// caller and are additionally recorded through the shared typed dead-letter recorder with the
/// captured snapshot and the admitted identity; admission rejections never create a dead letter.
/// </summary>
public class DefaultLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly ILocalEventDispatchRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILocalEventFailureRecorder? _recorder;
    private readonly ILogger<DefaultLocalEventBus>? _logger;

    public DefaultLocalEventBus(
        ILocalEventDispatchRegistry registry,
        IServiceProvider serviceProvider,
        ILocalEventFailureRecorder? recorder = null,
        ILogger<DefaultLocalEventBus>? logger = null)
    {
        _registry = registry;
        _serviceProvider = serviceProvider;
        _recorder = recorder;
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

        // The snapshot is captured before any handler runs, so a handler mutating ambient context
        // cannot change what a later failure record persists. A capture failure rejects the publish
        // before acceptance and never creates a dead letter.
        var snapshot = LocalEventContextBridge.Capture(_serviceProvider);

        var admission = await LocalEventAdmission
            .AdmitAsync(_serviceProvider, binding, @event, LocalEventExecutionSource.Publish, snapshot,
                persistedDescriptorId: null, cancellationToken)
            .ConfigureAwait(false);
        if (!admission.Allowed)
        {
            throw new LocalEventAdmissionException(admission.ProblemCode!, admission.Problem!);
        }

        try
        {
            await LocalEventExecution.ExecuteAsync(_serviceProvider, binding, @event, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (!IsCallerCancellation(failure, cancellationToken))
        {
            if (_recorder is not null)
            {
                var outcome = await _recorder
                    .TryRecordAsync(binding, @event, failure, snapshot, admission.Identity!, cancellationToken)
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
        => PublishAsync((ILocalEvent)@event, cancellationToken);

    Task IEventBus.PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken)
        => PublishAsync((ILocalEvent)@event, cancellationToken);

    void IEventBus.Subscribe<TEvent, THandler>()
    {
    }

    void IEventBus.Unsubscribe<TEvent, THandler>()
    {
    }

    private static bool IsCallerCancellation(Exception failure, CancellationToken cancellationToken)
        => failure is OperationCanceledException && cancellationToken.IsCancellationRequested;
}
