using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowInstance
{
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public VersionedDescriptorRef<WorkflowDescriptor> Workflow { get; init; }
    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;
    public string? CurrentStepId { get; set; }
    public int StepIndex { get; set; }
    public string? WaitingHumanTaskId { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public Dictionary<string, object?> Variables { get; init; } = new();
    public Dictionary<string, object?> StepVariables { get; init; } = new();
    public List<WorkflowStepResult> StepResults { get; init; } = new();
    public string? ErrorMessage { get; set; }
}
