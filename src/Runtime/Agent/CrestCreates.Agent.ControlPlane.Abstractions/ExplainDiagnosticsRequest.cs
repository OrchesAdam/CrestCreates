namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record ExplainDiagnosticsRequest
{
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public string? DraftId { get; init; }
}
