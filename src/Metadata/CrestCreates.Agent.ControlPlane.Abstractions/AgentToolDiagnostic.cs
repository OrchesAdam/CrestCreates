namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentToolDiagnostic
{
    public required string Code { get; init; }
    public required AgentToolDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
    public string? RelatedDiagnosticCode { get; init; }
}
