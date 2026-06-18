namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DiagnosticExplanationEntry
{
    public required string Code { get; init; }
    public required string Explanation { get; init; }
    public required string Remediation { get; init; }
    public required AgentToolDiagnosticSeverity Severity { get; init; }
    public IReadOnlyList<string>? SuggestedFixToolNames { get; init; }
}
