using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Replay settlement: records the terminal state write for each attempt and builds the independent
/// execution/state-write/release facts. A completed execution fact is never rewritten by state-write
/// or release failures; a cooperative cancellation restores Pending and rethrows the original
/// exception with its facts attached.
/// </summary>
internal sealed class LocalDeadLetterReplaySettlement
{
    private readonly LocalDeadLetterStateWriter _stateWriter;
    private readonly ILogger<LocalDeadLetterReplayExecutor>? _logger;

    public LocalDeadLetterReplaySettlement(
        LocalDeadLetterStateWriter stateWriter,
        ILogger<LocalDeadLetterReplayExecutor>? logger)
    {
        _stateWriter = stateWriter;
        _logger = logger;
    }

    public DeadLetterRetryResult RecordMarkRetryingFailure(
        DeadLetterMessage message,
        Exception? releaseProblem,
        Exception markFailure)
    {
        _logger?.LogError(
            markFailure,
            "MarkRetrying for dead letter message {MessageId} failed; no handler was executed and the persisted state is unconfirmed.",
            message.MessageId);
        var problem = $"MarkRetrying failed ({markFailure.Message}); no handler was executed and the persisted " +
                      "state of the message is unconfirmed.";
        if (releaseProblem is not null)
        {
            problem += $" (release failed: {releaseProblem.Message})";
        }

        return new DeadLetterRetryResult(message.MessageId, false, problem)
        {
            Execution = LocalEventExecutionOutcome.NotStarted,
            StateWrite = LocalEventStateWriteOutcome.Unknown,
            Release = releaseProblem is null ? LocalEventReleaseOutcome.Completed : LocalEventReleaseOutcome.Failed,
            ProblemCode = "MarkRetryingUnconfirmed",
        };
    }

    public async Task RecordCancellationAsync(
        DeadLetterMessage message,
        LocalEventPreparedInvocation prepared,
        Exception? releaseProblem,
        OperationCanceledException cancel)
    {
        var restored = await _stateWriter.TryWriteAsync(
            message with { Status = DeadLetterStatus.Pending },
            "restore Pending after cooperative cancellation");

        // The caller's cancellation is rethrown unchanged; release and restore facts stay
        // observable on it (structured log + exception data) instead of being discarded.
        // Caller-cooperative cancellation is an execution fact of its own (Spec §8): it is
        // reported as Cancelled, with handler entry kept as a separate fact.
        cancel.Data["LocalEventReplayExecution"] = LocalEventExecutionOutcome.Cancelled.ToString();
        cancel.Data["LocalEventReplayAnyHandlerInvoked"] = prepared.AnyHandlerInvoked ? "true" : "false";
        cancel.Data["LocalEventReplaySettlement"] = restored.Ok
            ? "RestorePendingConfirmed"
            : $"RestorePendingUnconfirmed: {restored.Problem}";
        if (!restored.Ok)
        {
            _logger?.LogError(
                "Replay of dead letter message {MessageId} was cancelled and restoring Pending failed: {Problem}. " +
                "The message state write failure is reported and not claimed as recovered.",
                message.MessageId, restored.Problem);
        }

        if (releaseProblem is not null)
        {
            cancel.Data["LocalEventReplayRelease"] = releaseProblem.ToString();
            _logger?.LogError(
                releaseProblem,
                "Replay of dead letter message {MessageId} was cancelled and releasing the execution environment " +
                "also failed; the release failure is preserved alongside the caller cancellation.",
                message.MessageId);
        }
    }

    public async Task<DeadLetterRetryResult> RecordHandlerFailureAsync(
        DeadLetterMessage message,
        LocalEventPreparedInvocation prepared,
        Exception? releaseProblem,
        Exception handlerFailure)
    {
        var newRetryCount = message.RetryCount + 1;
        var updated = message with
        {
            RetryCount = newRetryCount,
            Status = newRetryCount >= message.MaxRetries ? DeadLetterStatus.Archived : DeadLetterStatus.Pending,
            ErrorMessage = handlerFailure.Message,
            ExceptionType = handlerFailure.GetType().FullName,
        };

        var write = await _stateWriter.TryWriteAsync(updated, "record failed replay attempt");
        _logger?.LogError(
            handlerFailure,
            "Replay attempt {RetryCount}/{MaxRetries} failed for dead letter message {MessageId}; state write succeeded: {Written}. Handler invoked: {AnyHandlerInvoked}.",
            newRetryCount, message.MaxRetries, message.MessageId, write.Ok, prepared.AnyHandlerInvoked);

        var problem = write.Ok ? handlerFailure.Message : $"{handlerFailure.Message} (state write failed: {write.Problem})";
        if (releaseProblem is not null)
        {
            problem += $" (release failed: {releaseProblem.Message})";
        }

        return new DeadLetterRetryResult(message.MessageId, false, problem)
        {
            Execution = LocalEventExecutionOutcome.Failed,
            StateWrite = write.Ok ? LocalEventStateWriteOutcome.Confirmed : LocalEventStateWriteOutcome.Unknown,
            Release = releaseProblem is null ? LocalEventReleaseOutcome.Completed : LocalEventReleaseOutcome.Failed,
            AnyHandlerInvoked = prepared.AnyHandlerInvoked,
            ProblemCode = "HandlerFailed",
        };
    }

    public async Task<DeadLetterRetryResult> RecordSuccessAsync(
        DeadLetterMessage message,
        LocalEventPreparedInvocation prepared,
        Exception? releaseProblem)
    {
        // Business success is recorded the moment the last handler returned; release and settlement
        // are reported independently and never rewrite the execution fact.
        var write = await _stateWriter.TryWriteAsync(
            message with { Status = DeadLetterStatus.Retried },
            "record successful replay");

        if (!write.Ok || releaseProblem is not null)
        {
            var problem = write.Ok
                ? "handlers executed but the execution environment release failed"
                : $"handlers executed but the Retried state write failed: {write.Problem}";
            if (releaseProblem is not null)
            {
                problem += $" (release failed: {releaseProblem.Message})";
            }

            _logger?.LogError(
                "Replay of dead letter message {MessageId} dispatched successfully but settlement/release reported a failure: {Problem}. The completed execution fact is preserved.",
                message.MessageId, problem);
            return new DeadLetterRetryResult(message.MessageId, false, problem)
            {
                Execution = LocalEventExecutionOutcome.Completed,
                StateWrite = write.Ok ? LocalEventStateWriteOutcome.Confirmed : LocalEventStateWriteOutcome.Unknown,
                Release = releaseProblem is null ? LocalEventReleaseOutcome.Completed : LocalEventReleaseOutcome.Failed,
                AnyHandlerInvoked = prepared.AnyHandlerInvoked,
                ProblemCode = write.Ok ? "ReleaseFailed" : "SettlementUnconfirmed",
            };
        }

        _logger?.LogInformation("Replayed dead letter message {MessageId} successfully.", message.MessageId);
        return new DeadLetterRetryResult(message.MessageId, true, null)
        {
            Execution = LocalEventExecutionOutcome.Completed,
            StateWrite = LocalEventStateWriteOutcome.Confirmed,
            Release = LocalEventReleaseOutcome.Completed,
            AnyHandlerInvoked = prepared.AnyHandlerInvoked,
        };
    }
}
