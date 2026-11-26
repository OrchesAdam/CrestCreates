using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Structured activation decision combining eligibility, policy, and governance.
/// </summary>
public sealed record DescriptorActivationDecision
{
    public required DescriptorActivationEligibility Eligibility { get; init; }
    public required DescriptorActivationPolicy Policy { get; init; }
    public required DescriptorLifecycleDecisionKind GovernanceDecision { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }

    public bool IsActivatable => Eligibility != DescriptorActivationEligibility.NotActivatable;
}
