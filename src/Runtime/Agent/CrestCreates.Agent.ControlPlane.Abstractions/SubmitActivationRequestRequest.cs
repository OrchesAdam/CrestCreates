using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record SubmitActivationRequestRequest
{
    public required string DraftId { get; init; }
    public required ActivationBindingSnapshot BindingSnapshot { get; init; }
    public string? Rationale { get; init; }

    /// <summary>
    /// Pre-evaluated governance decision for this activation request.
    /// When provided, the RequestService uses this instead of evaluating governance internally.
    /// When null, the RequestService falls back to safe-default (ReviewRequired).
    /// The caller (ToolService) is responsible for evaluating governance via IDescriptorLifecycleGovernanceService
    /// before submitting the request.
    /// </summary>
    public DescriptorLifecycleDecisionKind? GovernanceDecision { get; init; }
}
