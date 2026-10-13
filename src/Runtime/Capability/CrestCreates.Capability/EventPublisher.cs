using CrestCreates.Capability.Abstractions;
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.Capability;

public sealed class EventPublisher : IEventPublisher
{
    private readonly ILocalEventBus? _eventBus;

    public EventPublisher(ILocalEventBus? eventBus = null)
    {
        _eventBus = eventBus;
    }

    public async Task PublishAsync(string eventName, object? payload, CancellationToken ct = default)
    {
        if (_eventBus == null) return;

        var envelope = new CapabilityEventEnvelope
        {
            EventName = eventName,
            Payload = payload,
            Timestamp = DateTimeOffset.UtcNow
        };

        await _eventBus.PublishAsync(envelope, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Internal dispatch-only notification envelope used by the first-party <see cref="EventPublisher"/>.
/// It carries an opaque object payload, so it is deliberately not replayable: it participates in the
/// typed dispatch membership (declared here and activated by <c>AddCapabilityRuntime</c>) but has no
/// JSON codec and is never recorded to the dead-letter store.
/// </summary>
[LocalEvent]
internal sealed class CapabilityEventEnvelope : ILocalEvent
{
    public string EventName { get; init; } = string.Empty;
    public object? Payload { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
