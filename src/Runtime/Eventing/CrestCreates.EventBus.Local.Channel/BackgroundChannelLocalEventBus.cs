using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Domain.DomainEvents;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstract;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local.Channel;

/// <summary>
/// Channel-flavored local event bus. Membership, context capture and the shared admission checks
/// (governance, explicit checks, activated validator) run before enqueue; no handler is constructed
/// here. A successful return means the immutable work item was enqueued — the consumer re-runs the
/// policy checks against the then-current registry before executing.
/// </summary>
public class BackgroundChannelLocalEventBus : ILocalEventBus, IEventBus
{
    private readonly ChannelLocalEventQueue _queue;
    private readonly ILocalEventDispatchRegistry _registry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundChannelLocalEventBus>? _logger;

    public BackgroundChannelLocalEventBus(
        ChannelLocalEventQueue queue,
        ILocalEventDispatchRegistry registry,
        IServiceProvider serviceProvider,
        ILogger<BackgroundChannelLocalEventBus>? logger = null)
    {
        _queue = queue;
        _registry = registry;
        _serviceProvider = serviceProvider;
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

        // Admission at the boundary: capture the snapshot now (the consumer must not read its own
        // ambient state later) and run the shared checks once. The consumer still re-checks policy.
        var snapshot = LocalEventContextBridge.Capture(_serviceProvider);
        var admission = await LocalEventAdmission
            .AdmitAsync(_serviceProvider, binding, @event, LocalEventExecutionSource.ChannelConsume, snapshot,
                persistedDescriptorId: null, cancellationToken)
            .ConfigureAwait(false);
        if (!admission.Allowed)
        {
            throw new LocalEventAdmissionException(admission.ProblemCode!, admission.Problem!);
        }

        var workItem = new LocalEventWorkItem(@event, snapshot, admission.Identity!);
        await _queue.EnqueueAsync(workItem, cancellationToken).ConfigureAwait(false);
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
}
