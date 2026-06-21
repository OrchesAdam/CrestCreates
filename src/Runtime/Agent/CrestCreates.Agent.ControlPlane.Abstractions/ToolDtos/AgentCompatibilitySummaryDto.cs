namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe summary of compatibility report.
/// Replaces DescriptorCompatibilityReport internals.
/// </summary>
public sealed record AgentCompatibilitySummaryDto
{
    public required bool IsCompatible { get; init; }
    public required int IncompatibilityCount { get; init; }
    public string? Summary { get; init; }
}
