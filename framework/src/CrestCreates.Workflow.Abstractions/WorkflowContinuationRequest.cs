namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowContinuationRequest
{
    /// <summary>
    /// Legacy name. Since Phase 5, this value is HumanTaskInstance.Id,
    /// NOT HumanTaskDescriptor.Id. Do not rename to avoid cascading changes.
    /// </summary>
    public string HumanTaskId { get; init; } = string.Empty;

    /// <summary>
    /// Alias for HumanTaskId. Since Phase 5, this is HumanTaskInstance.Id.
    /// </summary>
    public string HumanTaskInstanceId => HumanTaskId;

    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
