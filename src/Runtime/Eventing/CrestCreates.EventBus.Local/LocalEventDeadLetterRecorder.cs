using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Shared failure-recording component for the synchronous bus and the channel consumer. Because
/// the replayed payload never goes through the original publish pipeline again, the recorded
/// event identity is the owning assembly's typed binding.
/// </summary>
internal sealed class LocalEventDeadLetterRecorder : ILocalEventFailureRecorder
{
    private readonly IDeadLetterStore? _store;
    private readonly IEventRegistry? _eventRegistry;
    private readonly LocalDeadLetterOptions _options;
    private readonly ILogger _logger;

    public LocalEventDeadLetterRecorder(
        IDeadLetterStore? store,
        IEventRegistry? eventRegistry,
        IOptions<LocalDeadLetterOptions> options,
        ILogger logger)
    {
        _store = store;
        _eventRegistry = eventRegistry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LocalEventRecordOutcome> TryRecordAsync(
        LocalEventBinding binding,
        ILocalEvent @event,
        Exception failure,
        CancellationToken cancellationToken)
    {
        if (_store is null)
        {
            return new LocalEventRecordOutcome(false, "no dead-letter store is registered.");
        }

        if (!binding.Replayable)
        {
            return new LocalEventRecordOutcome(
                false,
                $"event '{binding.LocalName}' is dispatch-only; DLQ recording requires a replayable declaration.");
        }

        var typeInfo = binding.CodecFactory?.Invoke();
        if (typeInfo is null)
        {
            return new LocalEventRecordOutcome(
                false,
                $"event '{binding.LocalName}' declared replayable but resolved no JSON codec.");
        }

        byte[] payload;
        try
        {
            payload = JsonSerializer.SerializeToUtf8Bytes(@event, typeInfo);
        }
        catch (Exception serializationException)
        {
            return new LocalEventRecordOutcome(false, $"payload serialization failed: {serializationException.Message}");
        }

        var eventName = binding.LocalName;
        string? descriptorId = null;
        var version = binding.ReplayVersion;

        if (_eventRegistry is { State: RegistryState.Built })
        {
            var descriptor = _eventRegistry.GetByPayloadType(binding.EventType);
            if (descriptor is not null)
            {
                if (descriptor.Version != binding.ReplayVersion)
                {
                    return new LocalEventRecordOutcome(
                        false,
                        $"identity conflict for '{binding.LocalName}': governance descriptor " +
                        $"'{descriptor.Name}' declares v{descriptor.Version} but the local replay version is " +
                        $"v{binding.ReplayVersion}.");
                }

                eventName = descriptor.Name;
                descriptorId = descriptor.Id;
                version = descriptor.Version;
            }
        }

        var now = DateTime.UtcNow;
        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventName: eventName,
            EventVersion: version,
            EventDescriptorId: descriptorId,
            CorrelationId: null,
            TenantId: null,
            Scope: EventScope.Local,
            PayloadTypeFullName: binding.EventType.AssemblyQualifiedName!,
            Payload: payload,
            ErrorMessage: failure.Message,
            ExceptionType: failure.GetType().FullName,
            OccurredAt: now,
            FailedAt: now,
            RetryCount: 0,
            MaxRetries: _options.MaxRetries,
            Status: DeadLetterStatus.Pending,
            LocalReplayKey: binding.LocalReplayKey);

        try
        {
            await _store.EnqueueAsync(message, cancellationToken).ConfigureAwait(false);
            return new LocalEventRecordOutcome(true, null);
        }
        catch (Exception storeException)
        {
            _logger.LogError(
                storeException,
                "Failed to record local event '{EventName}' to the dead-letter store for message {MessageId}.",
                binding.LocalName,
                message.MessageId);
            return new LocalEventRecordOutcome(false, $"dead-letter store write failed: {storeException.Message}");
        }
    }
}
