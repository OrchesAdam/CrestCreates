using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record AgentToolDiagnostic
{
    public required DiagnosticCode Code { get; init; }
    public required SeverityLevel Severity { get; init; }
    public required string Message { get; init; }
    public string? Path { get; init; }
    public string? RelatedDiagnosticCode { get; init; }
}
