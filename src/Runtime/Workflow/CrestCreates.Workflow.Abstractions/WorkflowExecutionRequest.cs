using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowExecutionRequest
{
    public string WorkflowId { get; init; } = default!;
    public string? TenantId { get; init; }
    public string? OperationId { get; init; }
    public AuditOrigin? Origin { get; init; }
    public Dictionary<string, RuntimeStateValue> InputVariables { get; init; } = new(StringComparer.Ordinal);
}
