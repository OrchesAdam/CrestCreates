namespace CrestCreates.EventBus.Abstractions;

/// <summary>Business execution fact of one replay attempt.</summary>
public enum LocalEventExecutionOutcome
{
    /// <summary>No handler was entered (refused before execution or before the first handler).</summary>
    NotStarted,

    /// <summary>Every prepared handler returned successfully (recorded the moment the last one did).</summary>
    Completed,

    /// <summary>A handler threw (including non-caller cancellation during business execution).</summary>
    Failed,

    /// <summary>Cooperative caller cancellation interrupted the attempt.</summary>
    Cancelled,
}

/// <summary>Persistent settlement fact of one replay attempt.</summary>
public enum LocalEventStateWriteOutcome
{
    NotAttempted,
    Confirmed,
    Unknown,
}

/// <summary>Async scope release fact of one replay attempt.</summary>
public enum LocalEventReleaseOutcome
{
    NotOwned,
    Completed,
    Failed,
}

/// <summary>
/// Replay result with independently reported execution, settlement and release facts. The
/// positional members preserve the #126 call shape; the init-only facts are additive.
/// </summary>
public sealed record DeadLetterRetryResult(
    string MessageId,
    bool Success,
    string? ErrorMessage)
{
    public LocalEventExecutionOutcome Execution { get; init; } = LocalEventExecutionOutcome.NotStarted;

    public LocalEventStateWriteOutcome StateWrite { get; init; } = LocalEventStateWriteOutcome.NotAttempted;

    public LocalEventReleaseOutcome Release { get; init; } = LocalEventReleaseOutcome.NotOwned;

    /// <summary>True once the first handler invocation has been entered (not a checkpoint).</summary>
    public bool AnyHandlerInvoked { get; init; }

    public string? ProblemCode { get; init; }
}
