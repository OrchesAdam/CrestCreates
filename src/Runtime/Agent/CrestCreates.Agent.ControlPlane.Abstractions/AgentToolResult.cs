using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentToolResult<T> where T : class
{
    public required AgentToolResultStatus Status { get; init; }
    public T? Value { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public AgentToolInvocationAuditRecord? AuditRecord { get; init; }

    public static AgentToolResult<T> Success(T value, AgentToolInvocationAuditRecord? audit = null)
        => new() { Status = AgentToolResultStatus.Success, Value = value, Diagnostics = Array.Empty<AgentToolDiagnostic>(), AuditRecord = audit };

    public static AgentToolResult<T> Success(T value, IReadOnlyList<AgentToolDiagnostic> diagnostics, AgentToolInvocationAuditRecord? audit = null)
        => new() { Status = AgentToolResultStatus.Success, Value = value, Diagnostics = diagnostics, AuditRecord = audit };

    /// <summary>
    /// Creates a result indicating the tool invocation succeeded but produced
    /// diagnostics that the caller should acknowledge. Use this when the operation
    /// completed but warnings or informational diagnostics were generated that
    /// affect how the result should be interpreted (e.g., CTXPACK_TRUNCATED_BY_COUNT,
    /// CTXPACK_AMBIGUOUS_DESCRIPTOR_REF, FIX_ACTIONS_SKIPPED).
    /// </summary>
    public static AgentToolResult<T> SucceededWithDiagnostics(T value, IReadOnlyList<AgentToolDiagnostic> diagnostics, AgentToolInvocationAuditRecord? audit = null)
        => new() { Status = AgentToolResultStatus.SucceededWithDiagnostics, Value = value, Diagnostics = diagnostics, AuditRecord = audit };

    public static AgentToolResult<T> Denied(IReadOnlyList<AgentToolDiagnostic> diagnostics, AgentToolInvocationAuditRecord? audit = null)
        => new() { Status = AgentToolResultStatus.Denied, Value = null, Diagnostics = diagnostics, AuditRecord = audit };

    public static AgentToolResult<T> Failed(IReadOnlyList<AgentToolDiagnostic> diagnostics, AgentToolInvocationAuditRecord? audit = null)
        => new() { Status = AgentToolResultStatus.Failed, Value = null, Diagnostics = diagnostics, AuditRecord = audit };

    public static AgentToolResult<T> NotFound(string message, AgentToolInvocationAuditRecord? audit = null)
        => new()
        {
            Status = AgentToolResultStatus.NotFound,
            Value = null,
            Diagnostics = [new AgentToolDiagnostic
            {
                Code = new DiagnosticCode("TOOL_TARGET_NOT_FOUND"),
                Severity = SeverityLevel.Warning,
                Message = message
            }],
            AuditRecord = audit
        };

    public static AgentToolResult<T> InvalidRequest(IReadOnlyList<AgentToolDiagnostic> diagnostics, AgentToolInvocationAuditRecord? audit = null)
        => new() { Status = AgentToolResultStatus.InvalidRequest, Value = null, Diagnostics = diagnostics, AuditRecord = audit };
}
