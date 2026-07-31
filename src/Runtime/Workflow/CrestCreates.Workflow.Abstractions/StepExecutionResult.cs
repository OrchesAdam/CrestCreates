using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public sealed record StepExecutionResult(
    StepExecutionStatus Status,
    object? Output = null,
    IReadOnlyDictionary<string, object?>? Variables = null,
    string? WaitingHumanTaskId = null,
    HumanTaskInstance? PreparedHumanTask = null);
