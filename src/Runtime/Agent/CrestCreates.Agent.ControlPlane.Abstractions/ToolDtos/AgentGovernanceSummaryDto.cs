namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe summary of governance decision.
/// Replaces DescriptorLifecycleGovernanceReport internals.
/// </summary>
public sealed record AgentGovernanceSummaryDto
{
    public required bool IsApproved { get; init; }
    public required string Decision { get; init; }
    public string? Rationale { get; init; }
}
