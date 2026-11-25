namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Policy governing descriptor activation behavior.
/// Evaluated by IDescriptorActivationPolicyProvider per tenant/descriptor-kind.
/// </summary>
public sealed record DescriptorActivationPolicy
{
    /// <summary>
    /// If true, all activations require human review regardless of governance decision.
    /// </summary>
    public required bool RequireHumanReviewForAll { get; init; }

    /// <summary>
    /// If true, the same actor who created the request cannot approve it.
    /// Prevents agent self-approval for ReviewRequired drafts.
    /// </summary>
    public required bool ForbidSelfApproval { get; init; }

    /// <summary>
    /// [Obsolete] Evidence binding is always required per Issue #17 boundary.
    /// The BindingSnapshot.PackagePreviewId and EvidencePreviewId are now required fields.
    /// This property is retained for backward compatibility but is no longer evaluated.
    /// </summary>
    [Obsolete("Evidence binding is always required. PackagePreviewId and EvidencePreviewId are required fields on ActivationBindingSnapshot.")]
    public bool RequireEvidenceBinding { get; init; } = true;

    /// <summary>
    /// If true, Allowed governance decisions can auto-activate without human review.
    /// If false, even Allowed decisions require explicit approval.
    /// </summary>
    public required bool AutoActivateAllowedWhenPolicyPermits { get; init; }
}
