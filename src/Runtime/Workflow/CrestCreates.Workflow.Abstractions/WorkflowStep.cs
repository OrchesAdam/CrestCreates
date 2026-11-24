namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowStep
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public InteractionTarget Target { get; init; } = null!;

    /// <summary>
    /// Optional prerequisite token. <see langword="null"/> is unconditional.
    /// Previous-task tokens require an unconditional, immediately preceding HumanTask.
    /// <see cref="WorkflowConditionTokens.AllPriorHumanTasksApproved"/> requires at least one
    /// predecessor, all HumanTask targets, and only approval-gated conditional predecessors.
    /// All other values are rejected.
    /// </summary>
    public string? Condition { get; init; }
    public IReadOnlyList<string> Transitions { get; init; } = Array.Empty<string>();
    public string? InputMapping { get; init; }
    public string? OutputMapping { get; init; }
    public StepErrorBehavior OnError { get; init; } = StepErrorBehavior.Fail;
}
