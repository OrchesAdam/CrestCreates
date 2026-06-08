namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowDraftPolicy
{
    public bool EnableCheckpointing { get; init; }
    public TimeSpan SaveInterval { get; init; } = TimeSpan.FromMinutes(5);
    public bool SaveBeforeHumanTask { get; init; } = true;
    public bool SaveBeforeSubWorkflow { get; init; } = true;
}
