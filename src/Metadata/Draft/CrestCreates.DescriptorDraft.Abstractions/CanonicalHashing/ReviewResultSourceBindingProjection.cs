namespace CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

/// <summary>
/// Source-binding projection of DescriptorDraftReviewResult.
/// Contains all fields that bind a review result to its activation source context.
/// The integrity projection must NOT contain SourceReviewHash — these are sibling views.
/// </summary>
public sealed record ReviewResultSourceBindingProjection
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required bool IsActivationEligible { get; init; }
    public required bool IsValid { get; init; }
    public required IReadOnlyList<ReviewDiagnosticProjection> Diagnostics { get; init; }
    public string? GovernanceDecision { get; init; }
    public string? ImpactSeverity { get; init; }
}
