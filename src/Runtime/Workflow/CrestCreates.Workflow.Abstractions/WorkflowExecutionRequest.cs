using CrestCreates.Accountability.Abstractions.Context;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowExecutionRequest
{
    public string WorkflowId { get; init; } = default!;
    public string? TenantId { get; init; }
    public AuditOrigin? Origin { get; init; }
    public Dictionary<string, object?> InputVariables { get; init; } = new();
}
