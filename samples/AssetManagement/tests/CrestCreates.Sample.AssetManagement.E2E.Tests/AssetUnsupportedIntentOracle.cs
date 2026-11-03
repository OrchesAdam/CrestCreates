using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// What the B12 live evaluation actually observed, before any verdict is attached to it.
/// </summary>
internal sealed record AssetUnsupportedIntentObservation(
    DescriptorAuthoringStatus AuthoringStatus,
    IReadOnlyList<string> DiagnosticCodes,
    int DraftCount,
    int ReviewPassedDraftCount,
    bool ProviderOutputBudgetExhausted);

/// <summary>
/// Attributes the outcome of an unsupported-intent evaluation. The single accepted label is
/// <see cref="ExplicitUnsupportedResult"/>, which requires an intent-level unsupported
/// diagnosis: a parse failure, a contract-version rejection, a prompt-hash mismatch or an
/// approvable candidate are different facts and must never be reported as B12 success.
/// Anything the current contract cannot attribute is returned as an explicit pending label so
/// the evaluation fails loudly and the artifact carries the evidence for manual adjudication.
/// </summary>
internal static class AssetUnsupportedIntentOracle
{
    public const string ExplicitUnsupportedResult = "ExplicitUnsupportedResult";
    public const string FabricatedCapabilityClaim = "FabricatedCapabilityClaim";
    public const string ArtifactsRejectedByReviewPending = "ArtifactsRejectedByReview_PendingManualAdjudication";
    public const string ProviderOutputRejectedPending = "ProviderOutputRejected_PendingManualAdjudication";
    public const string PromptIntegrityFailure = "PromptIntegrityFailure";
    public const string BlockedWithoutUnsupportedIntentDiagnoses = "BlockedWithoutUnsupportedIntentDiagnoses";
    public const string NoArtifactProducedPending = "NoArtifactProduced_PendingManualAdjudication";
    public const string ProviderUnavailable = "ProviderUnavailable";
    public const string OutputBudgetExhausted = "OutputBudgetExhausted";
    public const string InvalidOutputBudget = "InvalidOutputBudget";
    public const string AuthoringFailure = "AuthoringFailure";

    /// <summary>The only label that may mark a B12 live evaluation as passed.</summary>
    public const string AcceptedResult = ExplicitUnsupportedResult;

    private static readonly IReadOnlyList<string> IntentLevelUnsupportedCodes =
    [
        DescriptorAuthoringDiagnosticCodes.UnknownDescriptorKindValue,
        DescriptorAuthoringDiagnosticCodes.UnsupportedDraftOperationValue,
        DescriptorAuthoringDiagnosticCodes.GovernanceBoundaryViolationValue
    ];

    public static string Classify(AssetUnsupportedIntentObservation observation)
    {
        if (observation.ProviderOutputBudgetExhausted)
            return OutputBudgetExhausted;

        if (observation.DiagnosticCodes.Contains(
                DescriptorAuthoringDiagnosticCodes.PromptHashMismatchValue))
            return PromptIntegrityFailure;

        return observation.AuthoringStatus switch
        {
            DescriptorAuthoringStatus.ProviderUnavailable => ProviderUnavailable,
            DescriptorAuthoringStatus.Failed => AuthoringFailure,
            // InvalidProviderOutput covers malformed JSON, a rejected contract version, a
            // missing plan and an empty item list alike, so it proves nothing about the intent.
            DescriptorAuthoringStatus.InvalidProviderOutput => ProviderOutputRejectedPending,
            DescriptorAuthoringStatus.Blocked => observation.DiagnosticCodes
                .Any(code => IntentLevelUnsupportedCodes.Contains(code))
                ? ExplicitUnsupportedResult
                : BlockedWithoutUnsupportedIntentDiagnoses,
            _ => observation.ReviewPassedDraftCount > 0
                ? FabricatedCapabilityClaim
                : observation.DraftCount > 0
                    ? ArtifactsRejectedByReviewPending
                    : NoArtifactProducedPending
        };
    }

    /// <summary>
    /// The deterministic review verdict used by both the live evaluation and the offline
    /// regressions: structurally valid, materializable, no error/blocker diagnosis and not
    /// governance-blocked.
    /// </summary>
    public static bool IsReviewPassed(DescriptorDraftReviewResult review)
        => review.ValidationResult.IsValid
            && review.MaterializationResult?.IsMaterialized == true
            && !review.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == SeverityLevel.Blocker || diagnostic.Severity == SeverityLevel.Error)
            && review.GovernanceDecision?.MaxDecision != DescriptorLifecycleDecisionKind.Blocked;

    /// <summary>
    /// Bounded, whitespace-collapsed claim text: the artifact must retain what the candidate
    /// asserted (identity fields alone cannot show a fabricated capability claim) without
    /// growing without limit.
    /// </summary>
    public static string? SanitizeClaim(string? value, int maximumCharacters = 400)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var characters = new char[value.Length];
        var written = 0;
        foreach (var character in value)
            characters[written++] = char.IsControl(character) ? ' ' : character;

        var collapsed = string.Join(
            ' ',
            new string(characters, 0, written).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (collapsed.Length <= maximumCharacters)
            return collapsed;

        return string.Concat(collapsed.AsSpan(0, maximumCharacters), "…[truncated]");
    }
}
