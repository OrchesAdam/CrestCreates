using CrestCreates.Metadata.Abstractions;
using CrestCreates.Snapshot.Abstractions;
using CrestCreates.Accountability.Abstractions.Context;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowInstance : ISnapshotable<WorkflowInstance>
{
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public string? TenantId { get; init; }
    public AuditOrigin? AuditOrigin { get; init; }
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
    public string? LastLifecycleAuditId { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? UpdatedAt { get; set; }

    public WorkflowInstance Snapshot()
    {
        return new WorkflowInstance
        {
            InstanceId = InstanceId,
            TenantId = TenantId,
            AuditOrigin = AuditOrigin,
            Workflow = Workflow,
            Status = Status,
            CurrentStepId = CurrentStepId,
            StepIndex = StepIndex,
            WaitingHumanTaskId = WaitingHumanTaskId,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            Variables = new Dictionary<string, object?>(Variables),
            StepVariables = new Dictionary<string, object?>(StepVariables),
            StepResults = new List<WorkflowStepResult>(StepResults),
            ErrorMessage = ErrorMessage,
            LastLifecycleAuditId = LastLifecycleAuditId,
            ConcurrencyStamp = ConcurrencyStamp,
            UpdatedAt = UpdatedAt
        };
    }
}
