namespace CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

/// <summary>
/// Integrity projection of DescriptorDraftReviewResult.
/// Contains fields that define the review manifest identity.
/// Must NOT contain SourceReviewHash — source-binding and integrity are sibling views.
/// </summary>
public sealed record ReviewResultIntegrityProjection
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required bool IsActivationEligible { get; init; }
    public required bool IsValid { get; init; }
    public required int DiagnosticCount { get; init; }
}
