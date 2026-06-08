namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowStep
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public InteractionTarget Target { get; init; } = null!;
    public string? Condition { get; init; }
    public IReadOnlyList<string> Transitions { get; init; } = Array.Empty<string>();
    public string? InputMapping { get; init; }
    public string? OutputMapping { get; init; }
    public StepErrorBehavior OnError { get; init; } = StepErrorBehavior.Fail;
}
