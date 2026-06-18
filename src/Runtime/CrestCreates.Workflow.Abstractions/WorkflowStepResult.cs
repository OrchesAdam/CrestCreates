namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowStepResult
{
    public string StepId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public StepExecutionStatus Status { get; init; }
    public object? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset ExecutedAt { get; init; }
    public TimeSpan Duration { get; init; }
}
