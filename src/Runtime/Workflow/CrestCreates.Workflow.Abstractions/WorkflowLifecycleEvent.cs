using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Workflow.Abstractions;

public sealed record WorkflowLifecycleEvent
{
    public string EventId { get; init; } = string.Empty;
    public string AuditId { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string WorkflowInstanceId { get; init; } = string.Empty;
    public string WorkflowId { get; init; } = string.Empty;
    public int WorkflowVersion { get; init; }
    public CanonicalHash? ContractHash { get; init; }
    public string? TenantId { get; init; }
    public string? CorrelationId { get; init; }
    public WorkflowInstanceStatus Status { get; init; }
    public WorkflowInstanceStatus? FromStatus { get; init; }
    public WorkflowInstanceStatus? ToStatus { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    [Obsolete("Use OccurredAt.")]
    public DateTimeOffset Timestamp { get => OccurredAt; init => OccurredAt = value; }
    public string? CausationId { get; init; }
    public string? ParentAuditId { get; init; }
    public string? PreviousAuditId { get; init; }
    public string? WorkflowRunOperationId { get; init; }
    public string? StepId { get; init; }
    public string? HumanTaskInstanceId { get; init; }
    public string? ReasonCode { get; init; }
    public AuditOrigin? Origin { get; init; }
    public string? HumanTaskCompletionEventId { get; init; }
}
