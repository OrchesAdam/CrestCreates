using CrestCreates.Agent.Authoring.Abstractions.Authoring;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

namespace CrestCreates.Sample.AssetManagement.E2E.Tests;

/// <summary>
/// What the B12 live evaluation actually observed, before any verdict is attached to it.
/// </summary>
/// <param name="ExplicitOutcomeEntryCount">
/// Structured refusal or clarification entries that carry real content: reasons with both a code and a
/// message for <see cref="DescriptorAuthoringStatus.UnsupportedIntent"/>, questions with an id, a reason
/// code and a question for <see cref="DescriptorAuthoringStatus.NeedsClarification"/>. Counting the
/// content here, rather than trusting the status alone, is what lets the oracle refuse an empty or
/// mis-diagnosed terminal result.
/// </param>
internal sealed record AssetUnsupportedIntentObservation(
    DescriptorAuthoringStatus AuthoringStatus,
    IReadOnlyList<string> DiagnosticCodes,
    int DraftCount,
    int ReviewPassedDraftCount,
    bool ProviderOutputBudgetExhausted,
    int ExplicitOutcomeEntryCount);

/// <summary>
/// Attributes the outcome of an unsupported-intent evaluation, in two layers that must not be conflated
/// (spec §10.4/§10.7, implementation audit S2-R1-01).
///
/// **New-contract coverage** — <see cref="IsAcceptedResult"/> — is true only for the two explicit terminal
/// outcomes of `8a.v1`: a refusal that names the unsupported boundary and a clarification request that names
/// the missing input, each with its own diagnostic code, at least one entry carrying real content, and no
/// candidate artifact alongside. That is what a B12 acceptance assertion may rest on.
///
/// **Historical attribution** — <see cref="IsHistoricalAttribution"/> — is the pre-`8a.v1` route: the model
/// proposed a candidate the framework's allowlist rejects, so authoring blocks with an intent-level diagnosis.
/// The rejection is real and stays labelled (it is the evidence that the boundary held and nothing deployed),
/// but it proves what *the framework* refused, not that *the model* used the explicit contract. Zero side
/// effects cannot substitute for the missing terminal outcome, so this label never counts as coverage.
///
/// Everything else — a parse failure, a rejected contract version, a prompt-hash mismatch, an approvable
/// candidate — keeps its own label and is never accepted. Anything the contract cannot attribute is returned
/// as an explicit pending label so the evaluation fails loudly and the artifact carries the evidence.
/// </summary>
internal static class AssetUnsupportedIntentOracle
{
    public const string ExplicitUnsupportedResult = "ExplicitUnsupportedResult";
    public const string ExplicitClarificationResult = "ExplicitClarificationResult";
    public const string FabricatedCapabilityClaim = "FabricatedCapabilityClaim";
    public const string ArtifactsRejectedByReviewPending = "ArtifactsRejectedByReview_PendingManualAdjudication";
    public const string ProviderOutputRejectedPending = "ProviderOutputRejected_PendingManualAdjudication";
    public const string PromptIntegrityFailure = "PromptIntegrityFailure";
    public const string BlockedWithoutUnsupportedIntentDiagnoses = "BlockedWithoutUnsupportedIntentDiagnoses";
    public const string LegacyIntentLevelBlockAttribution = "LegacyIntentLevelBlockAttribution";
    public const string UnsupportedOutcomeWithoutEntryPending = "UnsupportedOutcomeWithoutEntry_PendingManualAdjudication";
    public const string ClarificationOutcomeWithoutEntryPending = "ClarificationOutcomeWithoutEntry_PendingManualAdjudication";
    public const string NoArtifactProducedPending = "NoArtifactProduced_PendingManualAdjudication";
    public const string ProviderUnavailable = "ProviderUnavailable";
    public const string OutputBudgetExhausted = "OutputBudgetExhausted";
    public const string InvalidOutputBudget = "InvalidOutputBudget";
    public const string AuthoringFailure = "AuthoringFailure";

    /// <summary>The explicit unsupported refusal label; <see cref="IsAcceptedResult"/> is the predicate.</summary>
    public const string AcceptedResult = ExplicitUnsupportedResult;

    /// <summary>
    /// True only for the two explicit terminal outcomes of `8a.v1`. The historical intent-level block is
    /// deliberately excluded: it carries no structured entry, so it cannot show that the model used the
    /// explicit contract (§10.7; S2-R1-01).
    /// </summary>
    public static bool IsAcceptedResult(string label)
        => label is ExplicitUnsupportedResult or ExplicitClarificationResult;

    /// <summary>
    /// True for the pre-`8a.v1` attribution route. Kept reportable — it is real evidence that the framework
    /// boundary held and nothing deployed — but never usable as B12 coverage.
    /// </summary>
    public static bool IsHistoricalAttribution(string label)
        => label == LegacyIntentLevelBlockAttribution;

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
            // The historical route: the framework's allowlist rejected a proposed candidate. Attributable,
            // never coverage — a drafts answer to an unsupported intent is exactly what §10.4 keeps rejecting.
            DescriptorAuthoringStatus.Blocked => observation.DiagnosticCodes
                .Any(code => IntentLevelUnsupportedCodes.Contains(code))
                ? LegacyIntentLevelBlockAttribution
                : BlockedWithoutUnsupportedIntentDiagnoses,
            // An explicit terminal outcome is attributable only with its own diagnostic code, real entry
            // content and no candidate artifact. A status alone is not enough: it can be produced by a
            // parser that dropped the entries or by a chain that also saved a draft.
            DescriptorAuthoringStatus.UnsupportedIntent =>
                IsExplicitTerminal(observation, DescriptorAuthoringDiagnosticCodes.IntentUnsupportedValue)
                    ? ExplicitUnsupportedResult
                    : UnsupportedOutcomeWithoutEntryPending,
            DescriptorAuthoringStatus.NeedsClarification =>
                IsExplicitTerminal(observation, DescriptorAuthoringDiagnosticCodes.IntentNeedsClarificationValue)
                    ? ExplicitClarificationResult
                    : ClarificationOutcomeWithoutEntryPending,
            _ => observation.ReviewPassedDraftCount > 0
                ? FabricatedCapabilityClaim
                : observation.DraftCount > 0
                    ? ArtifactsRejectedByReviewPending
                    : NoArtifactProducedPending
        };
    }

    private static bool IsExplicitTerminal(AssetUnsupportedIntentObservation observation, string code)
        => observation.DiagnosticCodes.Contains(code)
            && observation.ExplicitOutcomeEntryCount > 0
            && observation.DraftCount == 0
            && observation.ReviewPassedDraftCount == 0;

    /// <summary>
    /// Counts the structured entries of an explicit terminal outcome that carry attributable content: reasons
    /// with both a code and a message, questions with an id, a reason code and a question. Consumers observe
    /// this instead of trusting the status, so an empty or dropped section cannot be accepted as B12 coverage.
    /// </summary>
    public static int CountExplicitOutcomeEntries(DescriptorAuthoringResult result)
        => result.Status switch
        {
            DescriptorAuthoringStatus.UnsupportedIntent => result.Unsupported?.Reasons.Count(reason =>
                !string.IsNullOrWhiteSpace(reason.Code) && !string.IsNullOrWhiteSpace(reason.Message)) ?? 0,
            DescriptorAuthoringStatus.NeedsClarification => result.Clarification?.Questions.Count(question =>
                !string.IsNullOrWhiteSpace(question.Id)
                && !string.IsNullOrWhiteSpace(question.ReasonCode)
                && !string.IsNullOrWhiteSpace(question.Question)) ?? 0,
            _ => 0
        };

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
