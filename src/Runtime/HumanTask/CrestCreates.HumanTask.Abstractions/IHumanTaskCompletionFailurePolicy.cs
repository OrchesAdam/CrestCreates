namespace CrestCreates.HumanTask.Abstractions;

/// <summary>
/// Explicit recovery boundary for a completion whose event dispatch partially failed.
/// Implementations must resume from their own durable/checkpointed progress and must not
/// blindly replay handlers whose side effects may already have committed.
/// </summary>
public interface IHumanTaskCompletionFailurePolicy
{
    Task RecoverAsync(
        HumanTaskInstance instance,
        HumanTaskCompletedEvent completion,
        CancellationToken cancellationToken = default);
}

public sealed class HumanTaskCompletionRecoveryRequiredException(string humanTaskInstanceId)
    : InvalidOperationException(
        $"HumanTask '{humanTaskInstanceId}' has a failed completion dispatch and requires " +
        "an explicit IHumanTaskCompletionFailurePolicy to recover it.");
