namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowContinuationRequest
{
    public string HumanTaskId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
