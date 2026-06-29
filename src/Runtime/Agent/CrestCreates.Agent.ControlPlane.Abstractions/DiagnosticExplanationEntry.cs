using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DiagnosticExplanationEntry
{
    public required DiagnosticCode Code { get; init; }
    public required string Explanation { get; init; }
    public required string Remediation { get; init; }
    public required SeverityLevel Severity { get; init; }
    public IReadOnlyList<string>? SuggestedFixToolNames { get; init; }
}
