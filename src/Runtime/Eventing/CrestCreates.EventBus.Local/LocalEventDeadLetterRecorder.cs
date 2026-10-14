using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.Event.Abstractions;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Shared failure-recording component for the synchronous bus and the channel consumer. The record
/// is produced from the frozen binding identity, the admitted identity and the context snapshot
/// captured at the acceptance boundary — never from an advisory registry re-lookup and never from
/// a re-invoked factory — so the persisted identity/context match the accepted execution contract
/// even when the governance registry reports another version later.
/// </summary>
internal sealed class LocalEventDeadLetterRecorder : ILocalEventFailureRecorder
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly LocalDeadLetterOptions _options;
    private readonly ILogger _logger;

    public LocalEventDeadLetterRecorder(
        IServiceScopeFactory? scopeFactory,
        IOptions<LocalDeadLetterOptions> options,
        ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<LocalEventRecordOutcome> TryRecordAsync(
        LocalEventBinding binding,
        ILocalEvent @event,
        Exception failure,
        LocalEventExecutionContextSnapshot snapshot,
        LocalEventAcceptedIdentity acceptedIdentity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(acceptedIdentity);

        if (_scopeFactory is null)
        {
            return new LocalEventRecordOutcome(false, "no dead-letter scope factory is registered.");
        }

        if (!binding.Replayable)
        {
            return new LocalEventRecordOutcome(
                false,
                $"event '{binding.LocalName}' is dispatch-only; DLQ recording requires a replayable declaration.");
        }

        var typeInfo = binding.Codec;
        if (typeInfo is null)
        {
            return new LocalEventRecordOutcome(
                false,
                $"event '{binding.LocalName}' declared replayable but carries no frozen JSON codec.");
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

        var now = DateTime.UtcNow;
        var message = new DeadLetterMessage(
            MessageId: Guid.NewGuid().ToString("N"),
            EventName: acceptedIdentity.DescriptorName ?? binding.LocalName,
            EventVersion: acceptedIdentity.ReplayVersion,
            EventDescriptorId: acceptedIdentity.DescriptorId,
            CorrelationId: snapshot.CorrelationId,
            TenantId: snapshot.TenantId,
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
            LocalReplayKey: acceptedIdentity.LocalReplayKey,
            LocalContextVersion: snapshot.ContextVersion);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetService<IDeadLetterStore>();
            if (store is null)
            {
                return new LocalEventRecordOutcome(false, "no dead-letter store is registered.");
            }

            await store.EnqueueAsync(message, cancellationToken).ConfigureAwait(false);
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
