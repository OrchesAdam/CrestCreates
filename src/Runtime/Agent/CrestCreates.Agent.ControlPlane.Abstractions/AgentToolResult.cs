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
                Code = "TOOL_TARGET_NOT_FOUND",
                Severity = AgentToolDiagnosticSeverity.Warning,
                Message = message
            }],
            AuditRecord = audit
        };

    public static AgentToolResult<T> InvalidRequest(IReadOnlyList<AgentToolDiagnostic> diagnostics, AgentToolInvocationAuditRecord? audit = null)
        => new() { Status = AgentToolResultStatus.InvalidRequest, Value = null, Diagnostics = diagnostics, AuditRecord = audit };
}
