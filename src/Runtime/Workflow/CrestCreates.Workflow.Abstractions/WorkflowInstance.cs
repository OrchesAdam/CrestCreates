using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.Runtime;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;
using CrestCreates.Snapshot.Abstractions;
using CrestCreates.Accountability.Abstractions.Context;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowInstance : ISnapshotable<WorkflowInstance>
{
    public RuntimeInstanceKey Key { get; init; } = new(null, Guid.NewGuid().ToString("N"));
    public AuditOrigin? AuditOrigin { get; init; }
    public RuntimeDescriptorPin WorkflowPin { get; init; } = CreateUnresolvedPin();
    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;
    public string? CurrentStepId { get; set; }
    public int StepIndex { get; set; }
    public RuntimeInstanceKey? WaitingHumanTaskKey { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public Dictionary<string, RuntimeStateValue> Variables { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, RuntimeStateValue> StepVariables { get; init; } = new(StringComparer.Ordinal);
    public List<WorkflowStepResult> StepResults { get; init; } = new();
    public string? ErrorMessage { get; set; }
    public string? LastLifecycleAuditId { get; set; }
    public long Revision { get; internal set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public string InstanceId => Key.InstanceId;
    public string? TenantId => Key.TenantId;
    public string? WaitingHumanTaskId => WaitingHumanTaskKey?.InstanceId;
    public VersionedDescriptorRef<WorkflowDescriptor> Workflow => new(
        WorkflowPin.Ref.Id,
        WorkflowPin.Ref.Version ?? 0,
        VersionSelectionMode.Exact,
        WorkflowPin.ContractHash.Value);

    public WorkflowInstance Snapshot()
    {
        return new WorkflowInstance
        {
            Key = Key,
            AuditOrigin = AuditOrigin,
            WorkflowPin = WorkflowPin,
            Status = Status,
            CurrentStepId = CurrentStepId,
            StepIndex = StepIndex,
            WaitingHumanTaskKey = WaitingHumanTaskKey,
            StartedAt = StartedAt,
            CompletedAt = CompletedAt,
            Variables = new Dictionary<string, RuntimeStateValue>(Variables, StringComparer.Ordinal),
            StepVariables = new Dictionary<string, RuntimeStateValue>(StepVariables, StringComparer.Ordinal),
            StepResults = new List<WorkflowStepResult>(StepResults),
            ErrorMessage = ErrorMessage,
            LastLifecycleAuditId = LastLifecycleAuditId,
            Revision = Revision,
            UpdatedAt = UpdatedAt
        };
    }

    private static RuntimeDescriptorPin CreateUnresolvedPin() => new()
    {
        Ref = new DescriptorRef("workflow", "unresolved", 1),
        ContractHash = PlaceholderHash("Contract"),
        DefinitionHash = PlaceholderHash("Definition")
    };

    private static CanonicalHash PlaceholderHash(string purpose) => new()
    {
        Value = "unresolved",
        Algorithm = "unresolved",
        AlgorithmVersion = "unresolved",
        ArtifactKind = "Descriptor",
        DescriptorKind = "Workflow",
        Scope = "InternalFull",
        Purpose = purpose,
        ContractVersion = "unresolved",
        CanonicalShapeVersion = "unresolved"
    };
}
