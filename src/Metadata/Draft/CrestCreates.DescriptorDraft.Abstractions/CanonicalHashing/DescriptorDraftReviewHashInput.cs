namespace CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

/// <summary>
/// Versioned snapshot of the values that feed the canonical review hashes.
/// This is hash input data and does not authorize approval or activation.
/// </summary>
public sealed record DescriptorDraftReviewHashInput
{
    public const int CurrentVersion = 1;

    public required int Version { get; init; }
    public required ReviewResultSourceBindingProjection SourceBinding { get; init; }

    /// <summary>
    /// Captures only canonical hash inputs and copies the diagnostics collection so
    /// later mutations to the source review cannot change this snapshot.
    /// </summary>
    public static DescriptorDraftReviewHashInput Capture(DescriptorDraftReviewResult reviewResult)
    {
        ArgumentNullException.ThrowIfNull(reviewResult);
        ArgumentNullException.ThrowIfNull(reviewResult.Diagnostics);
        ArgumentNullException.ThrowIfNull(reviewResult.ValidationResult);

        string? governanceDecision = reviewResult.GovernanceDecision?.MaxDecision.ToString();
        string? impactSeverity = reviewResult.ImpactAnalysisResult?.MaxSeverity.ToString();
        var diagnostics = reviewResult.Diagnostics
            .Select(diagnostic =>
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
                return new ReviewDiagnosticProjection
                {
                    Code = diagnostic.Code.ToString(),
                    Severity = diagnostic.Severity.ToString()
                };
            })
            .ToArray();

        var input = new DescriptorDraftReviewHashInput
        {
            Version = CurrentVersion,
            SourceBinding = new ReviewResultSourceBindingProjection
            {
                TenantId = reviewResult.TenantId,
                DraftId = reviewResult.DraftId,
                IsActivationEligible = reviewResult.IsActivationEligible,
                IsValid = reviewResult.ValidationResult.IsValid,
                Diagnostics = Array.AsReadOnly(diagnostics),
                GovernanceDecision = governanceDecision,
                ImpactSeverity = impactSeverity
            }
        };

        input.Validate();
        return input;
    }

    /// <summary>Rejects unsupported or malformed serialized input before it is hashed.</summary>
    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new NotSupportedException($"Review hash input version '{Version}' is not supported.");

        ArgumentNullException.ThrowIfNull(SourceBinding);
        if (string.IsNullOrWhiteSpace(SourceBinding.TenantId))
            throw new ArgumentException("Review hash input TenantId is required.", nameof(SourceBinding));
        if (string.IsNullOrWhiteSpace(SourceBinding.DraftId))
            throw new ArgumentException("Review hash input DraftId is required.", nameof(SourceBinding));
        ArgumentNullException.ThrowIfNull(SourceBinding.Diagnostics);

        foreach (var diagnostic in SourceBinding.Diagnostics)
        {
            ArgumentNullException.ThrowIfNull(diagnostic);
            if (string.IsNullOrWhiteSpace(diagnostic.Code))
                throw new ArgumentException("Review hash input diagnostic Code is required.", nameof(SourceBinding));
            if (string.IsNullOrWhiteSpace(diagnostic.Severity))
                throw new ArgumentException("Review hash input diagnostic Severity is required.", nameof(SourceBinding));
        }
    }
}
