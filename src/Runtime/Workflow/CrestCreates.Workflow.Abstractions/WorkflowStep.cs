namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowStep
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public InteractionTarget Target { get; init; } = null!;

    /// <summary>
    /// Optional prerequisite token. <see langword="null"/> is unconditional;
    /// the only supported values are <see cref="WorkflowConditionTokens.PreviousHumanTaskApproved"/>
    /// and <see cref="WorkflowConditionTokens.PreviousHumanTaskRejected"/>. A token requires the
    /// immediately preceding HumanTask to be durably completed; all other values are rejected.
    /// </summary>
    public string? Condition { get; init; }
    public IReadOnlyList<string> Transitions { get; init; } = Array.Empty<string>();
    public string? InputMapping { get; init; }
    public string? OutputMapping { get; init; }
    public StepErrorBehavior OnError { get; init; } = StepErrorBehavior.Fail;
}
