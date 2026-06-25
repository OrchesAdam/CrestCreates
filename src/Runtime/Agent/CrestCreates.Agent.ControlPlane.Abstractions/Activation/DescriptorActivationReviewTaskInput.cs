namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Typed input for the activation review HumanTask.
/// Carries all information the human reviewer needs to make a decision.
/// </summary>
public sealed record DescriptorActivationReviewTaskInput
{
    public required string ActivationRequestId { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required DescriptorActivationEligibility Eligibility { get; init; }
    public required string GovernanceDecision { get; init; }
    public required string PolicySummary { get; init; }
    public string? Rationale { get; init; }
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Summary of the review result that led to this activation request.
    /// Human reviewers need this context to make informed decisions.
    /// </summary>
    public string? ReviewSummary { get; init; }

    /// <summary>
    /// Summary of the evidence binding (package + evidence hashes).
    /// </summary>
    public string? EvidenceSummary { get; init; }

    /// <summary>
    /// Full binding hashes from the activation request, giving the reviewer
    /// structured hash context for the review/evidence/package they are approving.
    /// </summary>
    public BindingHashes? BoundHashes { get; init; }

    /// <summary>
    /// Summary of the package manifest contents (dependencies, version constraints, compatibility info).
    /// Populated from the package preview when available.
    /// </summary>
    public string? PackageManifestSummary { get; init; }

    /// <summary>
    /// Impact assessment context for the activation (affected descriptors, breaking changes, migration notes).
    /// Populated from the review result's impact analysis when available.
    /// </summary>
    public string? ImpactContext { get; init; }

    /// <summary>
    /// Raw package manifest JSON, if available from the package preview.
    /// Provides the reviewer with full manifest content (dependencies, version constraints,
    /// compatibility declarations) without requiring a separate lookup.
    /// Null when the package preview does not expose manifest JSON.
    /// </summary>
    public string? PackageManifestJson { get; init; }
}
