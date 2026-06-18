namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record ActivationReadinessBlocker
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required ActivationReadinessBlockerSeverity Severity { get; init; }
    public string? Remedy { get; init; }
}
