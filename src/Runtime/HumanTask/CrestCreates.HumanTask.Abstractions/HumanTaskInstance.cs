using CrestCreates.Metadata.Abstractions;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskInstance : ISnapshotable<HumanTaskInstance>
{
    public string Id { get; init; } = default!;
    public string HumanTaskId { get; init; } = default!;
    public int HumanTaskVersion { get; init; }

    public HumanTaskInstanceStatus Status { get; set; }

    public string? TenantId { get; init; }

    public string? AssigneeUserId { get; set; }
    public string? AssigneeRoleId { get; set; }

    public string? WorkflowInstanceId { get; init; }
    public string? WorkflowStepId { get; init; }

    public object? Input { get; init; }
    public object? Output { get; set; }

    public string? Outcome { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }

    public string? CompletionDispatchError { get; set; }
    public DateTimeOffset? CompletionDispatchFailedAt { get; set; }
    public int CompletionDispatchAttemptCount { get; set; }
    public string? CompletionEventId { get; set; }

    public string? CancellationReason { get; set; }

    public IReadOnlyList<string> CandidateUserIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> CandidateRoleIds { get; set; } = Array.Empty<string>();
    public string? OrganizationUnitId { get; set; }
    public string? PositionId { get; set; }
    public string? AssigneeResolutionReason { get; set; }

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? UpdatedAt { get; set; }

    public HumanTaskInstance Snapshot()
    {
        return new HumanTaskInstance
        {
            Id = Id,
            HumanTaskId = HumanTaskId,
            HumanTaskVersion = HumanTaskVersion,
            Status = Status,
            TenantId = TenantId,
            AssigneeUserId = AssigneeUserId,
            AssigneeRoleId = AssigneeRoleId,
            WorkflowInstanceId = WorkflowInstanceId,
            WorkflowStepId = WorkflowStepId,
            Input = Input,
            Output = Output,
            Outcome = Outcome,
            CreatedAt = CreatedAt,
            CompletedAt = CompletedAt,
            CancelledAt = CancelledAt,
            CompletionDispatchError = CompletionDispatchError,
            CompletionDispatchFailedAt = CompletionDispatchFailedAt,
            CompletionDispatchAttemptCount = CompletionDispatchAttemptCount,
            CompletionEventId = CompletionEventId,
            CancellationReason = CancellationReason,
            ConcurrencyStamp = ConcurrencyStamp,
            UpdatedAt = UpdatedAt,
            CandidateUserIds = CandidateUserIds.ToArray(),
            CandidateRoleIds = CandidateRoleIds.ToArray(),
            OrganizationUnitId = OrganizationUnitId,
            PositionId = PositionId,
            AssigneeResolutionReason = AssigneeResolutionReason
        };
    }
}
