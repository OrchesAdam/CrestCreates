using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Shared replay execution core: load → precheck (status/budget/identity) → frozen-codec decode →
/// tenant resolution (awaited) and ambient context activation (in this frame) → shared admission
/// (governance/checks/validator) → one prepare of the whole handler batch inside a fresh async
/// scope → MarkRetrying → execute the same instances → release scope and context frames → independent
/// settlement. Preparation and decode happen before any state transition, and every attempt reports
/// execution, settlement and release facts independently. Replay re-enters the unified dispatch
/// core directly and never re-publishes.
/// </summary>
internal sealed class LocalDeadLetterReplayExecutor : ILocalDeadLetterReplayExecutor
{
    private static readonly TimeSpan StateWriteTimeout = TimeSpan.FromSeconds(5);

    private readonly ILocalEventDispatchRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalDeadLetterReplayExecutor>? _logger;

    public LocalDeadLetterReplayExecutor(
        ILocalEventDispatchRegistry registry,
        IServiceScopeFactory scopeFactory,
        ILogger<LocalDeadLetterReplayExecutor>? logger = null)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<DeadLetterRetryResult> ExecuteAsync(
        DeadLetterMessage message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Pre-execution cancellation leaves the message state untouched.
        cancellationToken.ThrowIfCancellationRequested();

        // Re-read the current persisted state to avoid stale EF tracking cache from the caller's scope.
        await using (var readScope = _scopeFactory.CreateAsyncScope())
        {
            var freshStore = readScope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
            var freshMessage = await freshStore.GetByIdAsync(message.MessageId, cancellationToken);
            if (freshMessage is null)
            {
                return Reject(message, $"message {message.MessageId} no longer exists in the store.", "MessageMissing");
            }

            message = freshMessage;
        }

        if (message.MaxRetries < 1)
        {
            return Reject(message,
                $"invalid persisted MaxRetries ({message.MaxRetries}); ordinary retry refused without state change.",
                "InvalidMaxRetries");
        }

        if (message.RetryCount < 0 || message.RetryCount > message.MaxRetries)
        {
            return Reject(message,
                $"invalid persisted RetryCount ({message.RetryCount}/{message.MaxRetries}); " +
                "ordinary retry refused without state change.",
                "InvalidRetryCount");
        }

        if (message.Status != DeadLetterStatus.Pending)
        {
            return Reject(message, $"status {message.Status} does not accept ordinary retry.", "NotPending");
        }

        if (message.RetryCount >= message.MaxRetries)
        {
            var write = await TryWriteStateAsync(
                message with { Status = DeadLetterStatus.Archived },
                "archive budget-exhausted pending message");
            _logger?.LogWarning(
                "Dead letter message {MessageId} reached its persisted retry budget ({RetryCount}/{MaxRetries}); archived without executing handlers. State write succeeded: {Written}.",
                message.MessageId, message.RetryCount, message.MaxRetries, write.Ok);
            return new DeadLetterRetryResult(
                message.MessageId,
                false,
                write.Ok
                    ? "retry budget exhausted; message archived without executing handlers."
                    : $"retry budget exhausted; archiving failed: {write.Problem}")
            {
                Execution = LocalEventExecutionOutcome.NotStarted,
                StateWrite = write.Ok ? LocalEventStateWriteOutcome.Confirmed : LocalEventStateWriteOutcome.Unknown,
                ProblemCode = "BudgetExhausted",
            };
        }

        if (!TryResolveBinding(message, out var binding, out var resolveProblem))
        {
            return Reject(message, resolveProblem!, "UnknownIdentity");
        }

        if (!binding!.Replayable)
        {
            return Reject(message, $"event '{binding.LocalName}' is dispatch-only and cannot be replayed.", "DispatchOnly");
        }

        var typeInfo = binding.Codec;
        if (typeInfo is null)
        {
            return Reject(message,
                $"event '{binding.LocalName}' carries no frozen JSON codec; the replay cannot decode.", "MissingCodec");
        }

        // Decode with the frozen codec BEFORE any state transition: a corrupt payload must leave the
        // original state and budget untouched (MarkRetrying is never called for it).
        object decoded;
        try
        {
            decoded = JsonSerializer.Deserialize(message.Payload, typeInfo)
                ?? throw new LocalEventPayloadDecodeException(
                    $"payload of message {message.MessageId} deserialized to null");
            if (!binding.EventType.IsInstanceOfType(decoded))
            {
                throw new LocalEventPayloadDecodeException(
                    $"payload of message {message.MessageId} deserialized to '{decoded.GetType().FullName}' " +
                    $"instead of '{binding.EventType.FullName}'");
            }
        }
        catch (Exception decodeFailure) when (decodeFailure is JsonException or LocalEventPayloadDecodeException)
        {
            _logger?.LogWarning(
                "Replay of dead letter message {MessageId} refused before any state transition: payload decode failed with {Failure}: {Message}.",
                message.MessageId, decodeFailure.GetType().Name, decodeFailure.Message);
            return Reject(message,
                $"payload decode failed before any state transition: {decodeFailure.Message}", "PayloadDecode");
        }

        // One fresh async scope owns the exact handler instances for this attempt: resolve and
        // activate the persisted context in this frame, run the shared admission checks, prepare the
        // whole batch, then MarkRetrying, then execute the same instances, then release.
        var executionScope = _scopeFactory.CreateAsyncScope();
        var replaySnapshot = LocalEventExecutionContextSnapshot.FromPersisted(
            message.LocalContextVersion, message.TenantId, message.CorrelationId);

        LocalEventContextFrame contextFrame;
        try
        {
            var resolution = await LocalEventContextBridge
                .ResolveAsync(executionScope.ServiceProvider, binding, replaySnapshot, cancellationToken)
                .ConfigureAwait(false);
            contextFrame = LocalEventContextBridge.Activate(executionScope.ServiceProvider, resolution);
        }
        catch (OperationCanceledException cancel) when (cancellationToken.IsCancellationRequested)
        {
            var releaseProblem = await TryDisposeScopeAsync(executionScope);
            AttachCancellationFacts(cancel, "NotStarted", "ContextNotRestored", releaseProblem);
            throw;
        }
        catch (LocalEventContextRestoreException restoreFailure)
        {
            var releaseProblem = await TryDisposeScopeAsync(executionScope);
            _logger?.LogWarning(
                "Replay of dead letter message {MessageId} refused before MarkRetrying: context restore failed with {Code}: {Message}.",
                message.MessageId, restoreFailure.ProblemCode, restoreFailure.Message);
            return Reject(message, restoreFailure.Message, restoreFailure.ProblemCode, releaseProblem, scopeOwned: true);
        }
        catch (Exception restoreFailure)
        {
            var releaseProblem = await TryDisposeScopeAsync(executionScope);
            _logger?.LogError(
                restoreFailure,
                "Replay of dead letter message {MessageId} failed while resolving its context before MarkRetrying; no handler was executed.",
                message.MessageId);
            return Reject(message,
                $"context resolution failed before MarkRetrying: {restoreFailure.Message}", "ContextRestoreFailed",
                releaseProblem, scopeOwned: true);
        }

        LocalEventAdmissionResult admission;
        try
        {
            admission = await LocalEventAdmission
                .AdmitAsync(executionScope.ServiceProvider, binding, (ILocalEvent)decoded, LocalEventExecutionSource.Replay,
                    replaySnapshot, message.EventDescriptorId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException cancel) when (cancellationToken.IsCancellationRequested)
        {
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            AttachCancellationFacts(cancel, "NotStarted", "AdmissionNotCompleted", releaseProblem);
            throw;
        }
        catch (Exception admissionFailure)
        {
            // A throwing admission (e.g. an execution-check factory that fails during scoped
            // resolution) must release the owned scope and context frame LIFO instead of bypassing
            // cleanup, and reports execution as not started without touching the budget.
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            _logger?.LogError(
                admissionFailure,
                "Replay admission for dead letter message {MessageId} threw before MarkRetrying; no handler was executed and the message state is unchanged.",
                message.MessageId);
            return Reject(message,
                $"admission failed before MarkRetrying: {admissionFailure.Message}", "AdmissionFailed",
                releaseProblem, scopeOwned: true);
        }

        if (!admission.Allowed)
        {
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            _logger?.LogWarning(
                "Replay of dead letter message {MessageId} refused before MarkRetrying: {Code}: {Problem}.",
                message.MessageId, admission.ProblemCode, admission.Problem);
            return Reject(message, admission.Problem!, admission.ProblemCode!, releaseProblem, scopeOwned: true);
        }

        LocalEventPreparedInvocation prepared;
        try
        {
            prepared = binding.Invoker.Prepare(executionScope.ServiceProvider, (ILocalEvent)decoded, binding.ExpectedHandlerTypes);
        }
        catch (OperationCanceledException cancel) when (cancellationToken.IsCancellationRequested)
        {
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            AttachCancellationFacts(cancel, "NotStarted", "PrepareNotCompleted", releaseProblem);
            throw;
        }
        catch (Exception prepareFailure)
        {
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            _logger?.LogError(
                prepareFailure,
                "Replay preparation for dead letter message {MessageId} failed before MarkRetrying; no handler was executed and the message state is unchanged.",
                message.MessageId);
            return Reject(message,
                $"handler preparation failed before MarkRetrying: {prepareFailure.Message}", "PrepareFailed",
                releaseProblem, scopeOwned: true);
        }

        if (prepared.HandlerCount == 0)
        {
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            return Reject(message,
                $"event '{binding.LocalName}' resolved no handlers in the execution scope; a dead-letter replay " +
                "requires at least one matching handler and must not be marked successful.", "ZeroHandlers",
                releaseProblem, scopeOwned: true);
        }

        // MarkRetrying: an unconfirmed write means no execution. The old snapshot must not overwrite
        // the unconfirmed persistent fact back to Pending.
        try
        {
            await using var stateScope = _scopeFactory.CreateAsyncScope();
            var store = stateScope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
            await store.MarkRetryingAsync(message.MessageId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException cancel) when (cancellationToken.IsCancellationRequested)
        {
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            cancel.Data["LocalEventReplayExecution"] = "NotStarted";
            cancel.Data["LocalEventReplaySettlement"] = "MarkRetryingUnconfirmed";
            if (releaseProblem is not null)
            {
                cancel.Data["LocalEventReplayRelease"] = releaseProblem.ToString();
                _logger?.LogError(
                    releaseProblem,
                    "MarkRetrying for dead letter message {MessageId} was interrupted by caller cancellation " +
                    "and releasing the execution environment also failed. No handler was executed and the persisted " +
                    "state of the message is unconfirmed.",
                    message.MessageId);
            }

            _logger?.LogWarning(
                "MarkRetrying for dead letter message {MessageId} was interrupted by caller cancellation; " +
                "no handler was executed and the persisted state is unconfirmed, so it is not overwritten.",
                message.MessageId);
            throw;
        }
        catch (Exception markFailure)
        {
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
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

        try
        {
            await prepared.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException cancel) when (cancellationToken.IsCancellationRequested)
        {
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            var restored = await TryWriteStateAsync(
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

            throw;
        }
        catch (Exception handlerFailure)
        {
            var releaseProblem = CombineReleaseProblems(
                await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            var newRetryCount = message.RetryCount + 1;
            var updated = message with
            {
                RetryCount = newRetryCount,
                Status = newRetryCount >= message.MaxRetries ? DeadLetterStatus.Archived : DeadLetterStatus.Pending,
                ErrorMessage = handlerFailure.Message,
                ExceptionType = handlerFailure.GetType().FullName,
            };

            var write = await TryWriteStateAsync(updated, "record failed replay attempt");
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

        // Business success is recorded the moment the last handler returned; release and settlement
        // are reported independently and never rewrite the execution fact.
        var successReleaseProblem = CombineReleaseProblems(
            await TryDisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
        var successWrite = await TryWriteStateAsync(
            message with { Status = DeadLetterStatus.Retried },
            "record successful replay");

        if (!successWrite.Ok || successReleaseProblem is not null)
        {
            var problem = successWrite.Ok
                ? "handlers executed but the execution environment release failed"
                : $"handlers executed but the Retried state write failed: {successWrite.Problem}";
            if (successReleaseProblem is not null)
            {
                problem += $" (release failed: {successReleaseProblem.Message})";
            }

            _logger?.LogError(
                "Replay of dead letter message {MessageId} dispatched successfully but settlement/release reported a failure: {Problem}. The completed execution fact is preserved.",
                message.MessageId, problem);
            return new DeadLetterRetryResult(message.MessageId, false, problem)
            {
                Execution = LocalEventExecutionOutcome.Completed,
                StateWrite = successWrite.Ok ? LocalEventStateWriteOutcome.Confirmed : LocalEventStateWriteOutcome.Unknown,
                Release = successReleaseProblem is null ? LocalEventReleaseOutcome.Completed : LocalEventReleaseOutcome.Failed,
                AnyHandlerInvoked = prepared.AnyHandlerInvoked,
                ProblemCode = successWrite.Ok ? "ReleaseFailed" : "SettlementUnconfirmed",
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

    private static void AttachCancellationFacts(
        OperationCanceledException cancel,
        string execution,
        string settlement,
        Exception? releaseProblem)
    {
        cancel.Data["LocalEventReplayExecution"] = execution;
        cancel.Data["LocalEventReplaySettlement"] = settlement;
        if (releaseProblem is not null)
        {
            cancel.Data["LocalEventReplayRelease"] = releaseProblem.ToString();
        }
    }

    private static Exception? CombineReleaseProblems(Exception? scopeProblem, Exception? frameProblem)
        => (scopeProblem, frameProblem) switch
        {
            (null, null) => null,
            (not null, null) => scopeProblem,
            (null, not null) => frameProblem,
            _ => new AggregateException("multiple release steps failed", scopeProblem, frameProblem),
        };

    private bool TryResolveBinding(
        DeadLetterMessage message,
        out LocalEventBinding? binding,
        out string? problem)
    {
        if (!string.IsNullOrEmpty(message.LocalReplayKey))
        {
            if (_registry.TryGetByReplayKey(message.LocalReplayKey, message.EventVersion, out var byKey))
            {
                binding = byKey;
                problem = null;
                return true;
            }

            binding = null;
            problem =
                $"no registered local event matches replay key '{message.LocalReplayKey}:v{message.EventVersion}' " +
                "(current key or explicit alias); the message is preserved and not treated as failed.";
            return false;
        }

        if (_registry.TryResolvePayloadIdentity(message.PayloadTypeFullName, message.EventVersion, out var byIdentity))
        {
            binding = byIdentity;
            problem = null;
            return true;
        }

        binding = null;
        problem =
            $"no registered local event or static legacy alias matches payload identity " +
            $"'{LocalEventIdentity.NormalizePayloadTypeName(message.PayloadTypeFullName)}:v{message.EventVersion}'; " +
            "the message is preserved and not treated as failed.";
        return false;
    }

    private static async Task<Exception?> TryDisposeScopeAsync(AsyncServiceScope scope)
    {
        try
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (Exception releaseFailure)
        {
            return releaseFailure;
        }
    }

    private static DeadLetterRetryResult Reject(
        DeadLetterMessage message,
        string problem,
        string problemCode,
        Exception? releaseProblem = null,
        bool scopeOwned = false)
        => new(message.MessageId, false,
            releaseProblem is null ? problem : $"{problem} (release failed: {releaseProblem.Message})")
        {
            Execution = LocalEventExecutionOutcome.NotStarted,
            StateWrite = LocalEventStateWriteOutcome.NotAttempted,
            Release = !scopeOwned
                ? LocalEventReleaseOutcome.NotOwned
                : releaseProblem is null ? LocalEventReleaseOutcome.Completed : LocalEventReleaseOutcome.Failed,
            ProblemCode = problemCode,
        };

    private async Task<(bool Ok, string? Problem)> TryWriteStateAsync(DeadLetterMessage updated, string description)
    {
        try
        {
            using var cts = new CancellationTokenSource(StateWriteTimeout);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
            await store.EnqueueAsync(updated, cts.Token).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception writeFailure)
        {
            _logger?.LogError(
                writeFailure,
                "Failed to {Description} for dead letter message {MessageId}.",
                description, updated.MessageId);
            return (false, writeFailure.Message);
        }
    }
}

/// <summary>Deterministic payload decode failure (corrupt JSON, null or mismatched type).</summary>
internal sealed class LocalEventPayloadDecodeException : InvalidOperationException
{
    public LocalEventPayloadDecodeException(string message)
        : base(message)
    {
    }
}
