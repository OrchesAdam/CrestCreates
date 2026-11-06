using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace CrestCreates.EventBus.Local;

/// <summary>
/// Cross-phase facts shared by the replay preflight, execution environment and settlement:
/// release-problem aggregation, frame-cleanup extraction and cancellation annotations. Keeping
/// these in one place ensures every phase reports the same release/cancellation vocabulary.
/// </summary>
internal static class LocalDeadLetterReplayFacts
{
    public static async Task<Exception?> DisposeScopeAsync(AsyncServiceScope scope)
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

    public static Exception? CombineReleaseProblems(Exception? scopeProblem, Exception? frameProblem)
        => (scopeProblem, frameProblem) switch
        {
            (null, null) => null,
            (not null, null) => scopeProblem,
            (null, not null) => frameProblem,
            _ => new AggregateException("multiple release steps failed", scopeProblem, frameProblem),
        };

    /// <summary>
    /// Frame-unwind failures that happened inside Activate travel either on the dedicated
    /// activation exception or attached to the original failure's Data (preserving a non-caller
    /// cancellation's identity); both shapes must reach the release outcome.
    /// </summary>
    public static Exception? ExtractFrameCleanupFailure(Exception failure)
    {
        if (failure is LocalEventContextActivationException activationFailure)
        {
            return activationFailure.CleanupFailure;
        }

        return failure.Data["LocalEventContextCleanupFailure"] as Exception;
    }

    public static void AttachCancellationFacts(
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
}
