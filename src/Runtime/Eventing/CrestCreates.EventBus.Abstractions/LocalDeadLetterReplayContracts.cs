using System;
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.EventBus.Abstractions;

/// <summary>Outcome of a typed dead-letter recording attempt.</summary>
public readonly record struct LocalEventRecordOutcome(bool Recorded, string? Problem);

/// <summary>
/// Shared failure-recording component used by the synchronous bus and the channel consumer.
/// A record is only produced for replayable events with a resolved source-generated codec;
/// the record uses the accepted identity and the context snapshot captured at the acceptance
/// boundary, and serialization or store failures are reported to the caller and never overwrite
/// the original handler failure.
/// </summary>
public interface ILocalEventFailureRecorder
{
    Task<LocalEventRecordOutcome> TryRecordAsync(
        LocalEventBinding binding,
        ILocalEvent @event,
        Exception failure,
        LocalEventExecutionContextSnapshot snapshot,
        LocalEventAcceptedIdentity acceptedIdentity,
        CancellationToken cancellationToken);
}

/// <summary>
/// Shared replay execution core used by the manual manager and the background retry service.
/// </summary>
public interface ILocalDeadLetterReplayExecutor
{
    Task<DeadLetterRetryResult> ExecuteAsync(DeadLetterMessage message, CancellationToken cancellationToken);
}
