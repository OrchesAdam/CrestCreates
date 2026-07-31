using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Runtime.Persistence.Abstractions.Keys;

namespace CrestCreates.Workflow.Abstractions;

public sealed record WorkflowSuspensionReceipt
{
    public required RuntimeTenantScope Scope { get; init; }
    public required string SuspensionOperationId { get; init; }
    public required CanonicalHash Integrity { get; init; }
    public required RuntimeInstanceKey WorkflowKey { get; init; }
    public required RuntimeInstanceKey HumanTaskKey { get; init; }
    public DateTimeOffset AcceptedAt { get; init; } = DateTimeOffset.UtcNow;
}
