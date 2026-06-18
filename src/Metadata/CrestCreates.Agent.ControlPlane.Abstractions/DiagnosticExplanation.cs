namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DiagnosticExplanation
{
    public required IReadOnlyList<DiagnosticExplanationEntry> Explanations { get; init; }
}
