using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record ActivationReadinessBlocker
{
    public required DiagnosticCode Code { get; init; }
    public required string Message { get; init; }
    public required SeverityLevel Severity { get; init; }
    public string? Remedy { get; init; }
}
