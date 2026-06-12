namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCreationRequest
{
    public string HumanTaskId { get; init; } = default!;
    public int? Version { get; init; }

    public string? TenantId { get; init; }

    public string? AssigneeUserId { get; init; }
    public string? AssigneeRoleId { get; init; }

    public string? WorkflowInstanceId { get; init; }
    public string? WorkflowStepId { get; init; }

    public object? Input { get; init; }

    public string? RequestedOrganizationUnitId { get; init; }
    public string? RequestedPositionId { get; init; }
    public string? RequestedByUserId { get; init; }
}
