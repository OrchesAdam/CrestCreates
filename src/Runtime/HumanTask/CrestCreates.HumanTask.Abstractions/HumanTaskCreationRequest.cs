using CrestCreates.Runtime.Persistence.Abstractions.Keys;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCreationRequest
{
    /// <summary>
    /// Stable caller-supplied identity for a retryable Runtime operation.
    /// When omitted, an interactive/non-durable caller receives a new identity.
    /// </summary>
    public string? InstanceId { get; init; }

    public string HumanTaskId { get; init; } = default!;
    public int? Version { get; init; }

    public string? TenantId { get; init; }

    public string? AssigneeUserId { get; init; }
    public string? AssigneeRoleId { get; init; }

    public RuntimeInstanceKey? WorkflowKey { get; init; }
    public string? WorkflowStepId { get; init; }

    public RuntimeStateValue? Input { get; init; }

    public string? RequestedOrganizationUnitId { get; init; }
    public string? RequestedPositionId { get; init; }
    public string? RequestedByUserId { get; init; }

    public IReadOnlyList<string> RequiredCompletionConsumerIds { get; init; } = Array.Empty<string>();
}
