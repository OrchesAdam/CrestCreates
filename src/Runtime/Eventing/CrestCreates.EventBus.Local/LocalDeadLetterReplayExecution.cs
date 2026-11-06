using System;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.EventBus.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Replay execution phase. It owns the attempt's async frame: one fresh scope supplies the exact
/// handler instances, context resolution/activation, shared admission and whole-batch preparation
/// happen before MarkRetrying, and the same frame executes the handlers and restores the ambient
/// context. Activation and release are frame-local AsyncLocal writes, so they must not cross an
/// awaited collaborator boundary.
/// </summary>
internal sealed class LocalDeadLetterReplayExecution
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocalDeadLetterReplayExecutor>? _logger;

    public LocalDeadLetterReplayExecution(
        IServiceScopeFactory scopeFactory,
        ILogger<LocalDeadLetterReplayExecutor>? logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<DeadLetterRetryResult> RunAttemptAsync(
        DeadLetterMessage message,
        LocalEventBinding binding,
        object decoded,
        LocalDeadLetterReplaySettlement settlement,
        CancellationToken cancellationToken)
    {
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
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope),
                LocalDeadLetterReplayFacts.ExtractFrameCleanupFailure(cancel));
            LocalDeadLetterReplayFacts.AttachCancellationFacts(
                cancel, "NotStarted", "ContextNotRestored", releaseProblem);
            throw;
        }
        catch (LocalEventContextActivationException activationFailure)
        {
            // Activation entered frames and its unwind failed as well: the frame cleanup failure is
            // a release fact, not a footnote, so it folds into the release outcome.
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope),
                activationFailure.CleanupFailure);
            _logger?.LogError(
                activationFailure,
                "Replay of dead letter message {MessageId} failed while activating its context before MarkRetrying; the entered frames were unwound with errors and no handler was executed.",
                message.MessageId);
            return Refuse(message,
                $"context activation failed before MarkRetrying: {activationFailure.Message}", "ContextRestoreFailed",
                releaseProblem, scopeOwned: true);
        }
        catch (LocalEventContextRestoreException restoreFailure)
        {
            var releaseProblem = await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope);
            _logger?.LogWarning(
                "Replay of dead letter message {MessageId} refused before MarkRetrying: context restore failed with {Code}: {Message}.",
                message.MessageId, restoreFailure.ProblemCode, restoreFailure.Message);
            return Refuse(message, restoreFailure.Message, restoreFailure.ProblemCode, releaseProblem, scopeOwned: true);
        }
        catch (Exception restoreFailure)
        {
            // A non-caller OCE from the activation phase lands here too: its frame-unwind failure
            // (attached when Activate could not return a frame) must reach the release outcome
            // instead of being reported as a completed release.
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope),
                LocalDeadLetterReplayFacts.ExtractFrameCleanupFailure(restoreFailure));
            _logger?.LogError(
                restoreFailure,
                "Replay of dead letter message {MessageId} failed while resolving or activating its context before MarkRetrying; no handler was executed.",
                message.MessageId);
            return Refuse(message,
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
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            LocalDeadLetterReplayFacts.AttachCancellationFacts(
                cancel, "NotStarted", "AdmissionNotCompleted", releaseProblem);
            throw;
        }
        catch (Exception admissionFailure)
        {
            // A throwing admission (e.g. an execution-check factory that fails during scoped
            // resolution) must release the owned scope and context frame LIFO instead of bypassing
            // cleanup, and reports execution as not started without touching the budget.
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            _logger?.LogError(
                admissionFailure,
                "Replay admission for dead letter message {MessageId} threw before MarkRetrying; no handler was executed and the message state is unchanged.",
                message.MessageId);
            return Refuse(message,
                $"admission failed before MarkRetrying: {admissionFailure.Message}", "AdmissionFailed",
                releaseProblem, scopeOwned: true);
        }

        if (!admission.Allowed)
        {
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            _logger?.LogWarning(
                "Replay of dead letter message {MessageId} refused before MarkRetrying: {Code}: {Problem}.",
                message.MessageId, admission.ProblemCode, admission.Problem);
            return Refuse(message, admission.Problem!, admission.ProblemCode!, releaseProblem, scopeOwned: true);
        }

        LocalEventPreparedInvocation prepared;
        try
        {
            prepared = binding.Invoker.Prepare(executionScope.ServiceProvider, (ILocalEvent)decoded, binding.ExpectedHandlerTypes);
        }
        catch (OperationCanceledException cancel) when (cancellationToken.IsCancellationRequested)
        {
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            LocalDeadLetterReplayFacts.AttachCancellationFacts(
                cancel, "NotStarted", "PrepareNotCompleted", releaseProblem);
            throw;
        }
        catch (Exception prepareFailure)
        {
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            _logger?.LogError(
                prepareFailure,
                "Replay preparation for dead letter message {MessageId} failed before MarkRetrying; no handler was executed and the message state is unchanged.",
                message.MessageId);
            return Refuse(message,
                $"handler preparation failed before MarkRetrying: {prepareFailure.Message}", "PrepareFailed",
                releaseProblem, scopeOwned: true);
        }

        if (prepared.HandlerCount == 0)
        {
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            return Refuse(message,
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
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
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
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            return settlement.RecordMarkRetryingFailure(message, releaseProblem, markFailure);
        }

        try
        {
            await prepared.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException cancel) when (cancellationToken.IsCancellationRequested)
        {
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            await settlement
                .RecordCancellationAsync(message, prepared, releaseProblem, cancel)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception handlerFailure)
        {
            var releaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
                await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
            return await settlement
                .RecordHandlerFailureAsync(message, prepared, releaseProblem, handlerFailure)
                .ConfigureAwait(false);
        }

        var successReleaseProblem = LocalDeadLetterReplayFacts.CombineReleaseProblems(
            await LocalDeadLetterReplayFacts.DisposeScopeAsync(executionScope), contextFrame.DisposeAndCapture());
        return await settlement
            .RecordSuccessAsync(message, prepared, successReleaseProblem)
            .ConfigureAwait(false);
    }

    private static DeadLetterRetryResult Refuse(
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
}
