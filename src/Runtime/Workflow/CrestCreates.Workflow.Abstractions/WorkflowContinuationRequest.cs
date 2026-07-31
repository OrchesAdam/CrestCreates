using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowContinuationRequest
{
    /// <summary>
    /// Legacy name. Since Phase 5, this value is HumanTaskInstance.Id,
    /// NOT HumanTaskDescriptor.Id. Do not rename to avoid cascading changes.
    /// </summary>
    public RuntimeInstanceKey HumanTaskKey { get; init; }
    public RuntimeInstanceKey WorkflowKey { get; init; }

    public string Outcome { get; init; } = string.Empty;
    public RuntimeStateValue? Result { get; init; }
    public string? CompletionEventId { get; init; }
    public string? TriggerOperationId { get; init; }
    public string? TriggerAuditId { get; init; }
}
