using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Typed activation review decision from a human reviewer.
/// Carries the actor identity, decision, and bound evidence hashes.
/// Used as the typed output from HumanTask completion for activation review.
/// </summary>
public sealed record DescriptorActivationReviewDecision
{
    public required string ActivationRequestId { get; init; }
    public required string TenantId { get; init; }
    public required string CorrelationId { get; init; }
    public required DescriptorActivationReviewOutcome Decision { get; init; }
    public required DescriptorActivationActorKind ActorKind { get; init; }
    public required string ActorId { get; init; }
    public required string Reason { get; init; }
    public required DateTimeOffset DecidedAt { get; init; }
    public required CanonicalHash BoundEvidenceHash { get; init; }
    public required CanonicalHash BoundEnvelopeHash { get; init; }
}

/// <summary>
/// Outcome of an activation review decision.
/// </summary>
public enum DescriptorActivationReviewOutcome
{
    Approved,
    Rejected
}
