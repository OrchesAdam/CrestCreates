using CrestCreates.Accountability.Abstractions.Context;
using CrestCreates.Runtime.Persistence.Abstractions.State;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowExecutionRequest
{
    public string WorkflowId { get; init; } = default!;

    /// <summary>The exact registered version to start, or null to use the default workflow version.</summary>
    public int? WorkflowVersion { get; init; }

    public string? TenantId { get; init; }
    public string? OperationId { get; init; }
    public AuditOrigin? Origin { get; init; }
    public Dictionary<string, RuntimeStateValue> InputVariables { get; init; } = new(StringComparer.Ordinal);
}
