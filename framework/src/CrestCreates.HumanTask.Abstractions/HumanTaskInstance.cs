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
}
