using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record ActivationRequest
{
    public required string RequestId { get; init; }
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required ActivationRequestStatus Status { get; init; }
    public required DateTimeOffset SubmittedAt { get; init; }
    public required string SubmittedBy { get; init; }

    // Phase 7e: Actor identity for self-approval prevention
    public required string CreatedByActorId { get; init; }
    public required DescriptorActivationActorKind CreatedByActorKind { get; init; }

    // Phase 7e: Governance + eligibility
    public required DescriptorLifecycleDecisionKind GovernanceDecision { get; init; }
    public required DescriptorActivationEligibility Eligibility { get; init; }

    // Phase 7e: Resolved activation policy (optional — captured during creation)
    public DescriptorActivationPolicy? Policy { get; init; }

    // Phase 7e: Immutable binding snapshot replaces optional single references
    public required ActivationBindingSnapshot BindingSnapshot { get; init; }

    // Legacy compatibility — derived from BindingSnapshot
    public string? ReviewResultId => BindingSnapshot.ReviewResultId;
    public string? PackagePreviewId => BindingSnapshot.PackagePreviewId;
    public string? EvidencePreviewId => BindingSnapshot.EvidencePreviewId;
    public string? CorrelationId => BindingSnapshot.CorrelationId;

    public IReadOnlyList<AgentToolDiagnostic>? Diagnostics { get; init; }
}
