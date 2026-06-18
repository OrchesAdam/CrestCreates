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
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? UpdatedAt { get; set; }

    public WorkflowInstance Clone()
    {
        return new WorkflowInstance
        {
            InstanceId = this.InstanceId,
            Workflow = this.Workflow,
            Status = this.Status,
            CurrentStepId = this.CurrentStepId,
            StepIndex = this.StepIndex,
            WaitingHumanTaskId = this.WaitingHumanTaskId,
            StartedAt = this.StartedAt,
            CompletedAt = this.CompletedAt,
            Variables = new Dictionary<string, object?>(this.Variables),
            StepVariables = new Dictionary<string, object?>(this.StepVariables),
            StepResults = new List<WorkflowStepResult>(this.StepResults),
            ErrorMessage = this.ErrorMessage,
            ConcurrencyStamp = this.ConcurrencyStamp,
            UpdatedAt = this.UpdatedAt
        };
    }
}
