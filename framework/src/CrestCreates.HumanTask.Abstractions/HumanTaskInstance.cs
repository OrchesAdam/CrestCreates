using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskInstance
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

    public string? CancellationReason { get; set; }

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? UpdatedAt { get; set; }

    public HumanTaskInstance Clone()
    {
        return new HumanTaskInstance
        {
            Id = this.Id,
            HumanTaskId = this.HumanTaskId,
            HumanTaskVersion = this.HumanTaskVersion,
            Status = this.Status,
            TenantId = this.TenantId,
            AssigneeUserId = this.AssigneeUserId,
            AssigneeRoleId = this.AssigneeRoleId,
            WorkflowInstanceId = this.WorkflowInstanceId,
            WorkflowStepId = this.WorkflowStepId,
            Input = this.Input,
            Output = this.Output,
            Outcome = this.Outcome,
            CreatedAt = this.CreatedAt,
            CompletedAt = this.CompletedAt,
            CancelledAt = this.CancelledAt,
            CancellationReason = this.CancellationReason,
            ConcurrencyStamp = this.ConcurrencyStamp,
            UpdatedAt = this.UpdatedAt
        };
    }
}
