using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Local audit record for descriptor activation lifecycle events.
/// Phase 7e scope only — not the full Accountability Runtime (#39).
/// </summary>
public sealed record DescriptorActivationAuditRecord
{
    public required string AuditRecordId { get; init; }
    public required string ActivationRequestId { get; init; }
    public required string TenantId { get; init; }
    public required DescriptorActivationAuditAction Action { get; init; }
    public required DescriptorActivationActorKind ActorKind { get; init; }
    public required string ActorId { get; init; }
    public string? TargetDescriptorRef { get; init; }
    public required string Outcome { get; init; }
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public CanonicalHash? EvidenceHash { get; init; }
    public CanonicalHash? EnvelopeHash { get; init; }
    public string? GateDecision { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// Actions recorded in activation audit records.
/// </summary>
public enum DescriptorActivationAuditAction
{
    Submit,
    Approve,
    Reject,
    Activate,
    Block,
    Stale,
    Cancel,
    GateDenied
}
