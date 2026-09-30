namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Canonical condition tokens supported by the Workflow runtime.
/// </summary>
public static class WorkflowConditionTokens
{
    public const string PreviousHumanTaskApproved = "previous-human-task-approved";
    public const string PreviousHumanTaskRejected = "previous-human-task-rejected";
}
