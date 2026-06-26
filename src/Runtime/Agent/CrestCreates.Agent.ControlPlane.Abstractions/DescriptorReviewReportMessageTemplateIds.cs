using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public static class DescriptorReviewReportMessageTemplateIds
{
    // Summary
    public const string SummaryValidValue = "report.summary.valid";
    public static MessageTemplateId SummaryValid { get; } = new(SummaryValidValue);

    public const string SummaryInvalidValue = "report.summary.invalid";
    public static MessageTemplateId SummaryInvalid { get; } = new(SummaryInvalidValue);

    public const string DiagnosticsCountValue = "report.diagnostics.count";
    public static MessageTemplateId DiagnosticsCount { get; } = new(DiagnosticsCountValue);

    // Draft identity
    public const string DraftIdentityInfoValue = "report.draft_identity.info";
    public static MessageTemplateId DraftIdentityInfo { get; } = new(DraftIdentityInfoValue);

    // Proposed changes
    public const string ProposedChangesMaterializedValue = "report.proposed_changes.materialized";
    public static MessageTemplateId ProposedChangesMaterialized { get; } = new(ProposedChangesMaterializedValue);

    public const string ProposedChangesFailedValue = "report.proposed_changes.failed";
    public static MessageTemplateId ProposedChangesFailed { get; } = new(ProposedChangesFailedValue);

    // Impact
    public const string ImpactAffectedValue = "report.impact.affected";
    public static MessageTemplateId ImpactAffected { get; } = new(ImpactAffectedValue);

    public const string ImpactNoneValue = "report.impact.none";
    public static MessageTemplateId ImpactNone { get; } = new(ImpactNoneValue);

    // Dependency
    public const string DependencySummaryValue = "report.dependency.summary";
    public static MessageTemplateId DependencySummary { get; } = new(DependencySummaryValue);

    // Compatibility
    public const string CompatibilityCompatibleValue = "report.compatibility.compatible";
    public static MessageTemplateId CompatibilityCompatible { get; } = new(CompatibilityCompatibleValue);

    public const string CompatibilityIncompatibleValue = "report.compatibility.incompatible";
    public static MessageTemplateId CompatibilityIncompatible { get; } = new(CompatibilityIncompatibleValue);

    // Governance
    public const string GovernanceApprovedValue = "report.governance.approved";
    public static MessageTemplateId GovernanceApproved { get; } = new(GovernanceApprovedValue);

    public const string GovernanceRejectedValue = "report.governance.rejected";
    public static MessageTemplateId GovernanceRejected { get; } = new(GovernanceRejectedValue);

    public const string GovernanceReviewRequiredValue = "report.governance.review_required";
    public static MessageTemplateId GovernanceReviewRequired { get; } = new(GovernanceReviewRequiredValue);

    // Human review
    public const string HumanReviewRequiredValue = "report.human_review.required";
    public static MessageTemplateId HumanReviewRequired { get; } = new(HumanReviewRequiredValue);

    // Recommendations
    public const string RecommendationNoActionValue = "report.recommendation.no_action";
    public static MessageTemplateId RecommendationNoAction { get; } = new(RecommendationNoActionValue);

    public const string RecommendationActivationHandoffValue = "report.recommendation.activation_handoff";
    public static MessageTemplateId RecommendationActivationHandoff { get; } = new(RecommendationActivationHandoffValue);

    public const string RecommendationHumanReviewValue = "report.recommendation.human_review";
    public static MessageTemplateId RecommendationHumanReview { get; } = new(RecommendationHumanReviewValue);

    public const string RecommendationApplyFixValue = "report.recommendation.apply_fix";
    public static MessageTemplateId RecommendationApplyFix { get; } = new(RecommendationApplyFixValue);

    public const string RecommendationReviseDraftValue = "report.recommendation.revise_draft";
    public static MessageTemplateId RecommendationReviseDraft { get; } = new(RecommendationReviseDraftValue);

    // Package preview
    public const string PackagePreviewPresentValue = "report.package_preview.present";
    public static MessageTemplateId PackagePreviewPresent { get; } = new(PackagePreviewPresentValue);

    public const string PackagePreviewNoneValue = "report.package_preview.none";
    public static MessageTemplateId PackagePreviewNone { get; } = new(PackagePreviewNoneValue);

    // Stable hashes
    public const string StableHashesPresentValue = "report.stable_hashes.present";
    public static MessageTemplateId StableHashesPresent { get; } = new(StableHashesPresentValue);

    public const string StableHashesNoneValue = "report.stable_hashes.none";
    public static MessageTemplateId StableHashesNone { get; } = new(StableHashesNoneValue);

    // Additional keys used in catalog
    public const string DiagnosticsMissingRefValue = "report.diagnostics.missing_ref";
    public static MessageTemplateId DiagnosticsMissingRef { get; } = new(DiagnosticsMissingRefValue);

    public const string CompatibilitySchemaValue = "report.compatibility.schema";
    public static MessageTemplateId CompatibilitySchema { get; } = new(CompatibilitySchemaValue);

    public const string RecommendationCancelDraftValue = "report.recommendation.cancel_draft";
    public static MessageTemplateId RecommendationCancelDraft { get; } = new(RecommendationCancelDraftValue);

    public const string PackageAvailableValue = "report.package.available";
    public static MessageTemplateId PackageAvailable { get; } = new(PackageAvailableValue);

    public const string HashesComputedValue = "report.hashes.computed";
    public static MessageTemplateId HashesComputed { get; } = new(HashesComputedValue);
}
