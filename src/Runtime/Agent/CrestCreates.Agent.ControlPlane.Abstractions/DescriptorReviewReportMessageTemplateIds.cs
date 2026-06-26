using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public static class DescriptorReviewReportMessageTemplateIds
{
    // Summary
    private const string SummaryValidValue = "report.summary.valid";
    public static MessageTemplateId SummaryValid { get; } = new(SummaryValidValue);

    private const string SummaryInvalidValue = "report.summary.invalid";
    public static MessageTemplateId SummaryInvalid { get; } = new(SummaryInvalidValue);

    private const string DiagnosticsCountValue = "report.diagnostics.count";
    public static MessageTemplateId DiagnosticsCount { get; } = new(DiagnosticsCountValue);

    // Draft identity
    private const string DraftIdentityInfoValue = "report.draft_identity.info";
    public static MessageTemplateId DraftIdentityInfo { get; } = new(DraftIdentityInfoValue);

    // Proposed changes
    private const string ProposedChangesMaterializedValue = "report.proposed_changes.materialized";
    public static MessageTemplateId ProposedChangesMaterialized { get; } = new(ProposedChangesMaterializedValue);

    private const string ProposedChangesFailedValue = "report.proposed_changes.failed";
    public static MessageTemplateId ProposedChangesFailed { get; } = new(ProposedChangesFailedValue);

    // Impact
    private const string ImpactAffectedValue = "report.impact.affected";
    public static MessageTemplateId ImpactAffected { get; } = new(ImpactAffectedValue);

    private const string ImpactNoneValue = "report.impact.none";
    public static MessageTemplateId ImpactNone { get; } = new(ImpactNoneValue);

    // Dependency
    private const string DependencySummaryValue = "report.dependency.summary";
    public static MessageTemplateId DependencySummary { get; } = new(DependencySummaryValue);

    // Compatibility
    private const string CompatibilityCompatibleValue = "report.compatibility.compatible";
    public static MessageTemplateId CompatibilityCompatible { get; } = new(CompatibilityCompatibleValue);

    private const string CompatibilityIncompatibleValue = "report.compatibility.incompatible";
    public static MessageTemplateId CompatibilityIncompatible { get; } = new(CompatibilityIncompatibleValue);

    // Governance
    private const string GovernanceApprovedValue = "report.governance.approved";
    public static MessageTemplateId GovernanceApproved { get; } = new(GovernanceApprovedValue);

    private const string GovernanceRejectedValue = "report.governance.rejected";
    public static MessageTemplateId GovernanceRejected { get; } = new(GovernanceRejectedValue);

    private const string GovernanceReviewRequiredValue = "report.governance.review_required";
    public static MessageTemplateId GovernanceReviewRequired { get; } = new(GovernanceReviewRequiredValue);

    // Human review
    private const string HumanReviewRequiredValue = "report.human_review.required";
    public static MessageTemplateId HumanReviewRequired { get; } = new(HumanReviewRequiredValue);

    // Recommendations
    private const string RecommendationNoActionValue = "report.recommendation.no_action";
    public static MessageTemplateId RecommendationNoAction { get; } = new(RecommendationNoActionValue);

    private const string RecommendationActivationHandoffValue = "report.recommendation.activation_handoff";
    public static MessageTemplateId RecommendationActivationHandoff { get; } = new(RecommendationActivationHandoffValue);

    private const string RecommendationHumanReviewValue = "report.recommendation.human_review";
    public static MessageTemplateId RecommendationHumanReview { get; } = new(RecommendationHumanReviewValue);

    private const string RecommendationApplyFixValue = "report.recommendation.apply_fix";
    public static MessageTemplateId RecommendationApplyFix { get; } = new(RecommendationApplyFixValue);

    private const string RecommendationReviseDraftValue = "report.recommendation.revise_draft";
    public static MessageTemplateId RecommendationReviseDraft { get; } = new(RecommendationReviseDraftValue);

    // Package preview
    private const string PackagePreviewPresentValue = "report.package_preview.present";
    public static MessageTemplateId PackagePreviewPresent { get; } = new(PackagePreviewPresentValue);

    private const string PackagePreviewNoneValue = "report.package_preview.none";
    public static MessageTemplateId PackagePreviewNone { get; } = new(PackagePreviewNoneValue);

    // Stable hashes
    private const string StableHashesPresentValue = "report.stable_hashes.present";
    public static MessageTemplateId StableHashesPresent { get; } = new(StableHashesPresentValue);

    private const string StableHashesNoneValue = "report.stable_hashes.none";
    public static MessageTemplateId StableHashesNone { get; } = new(StableHashesNoneValue);

    // Additional keys used in catalog
    private const string DiagnosticsMissingRefValue = "report.diagnostics.missing_ref";
    public static MessageTemplateId DiagnosticsMissingRef { get; } = new(DiagnosticsMissingRefValue);

    private const string CompatibilitySchemaValue = "report.compatibility.schema";
    public static MessageTemplateId CompatibilitySchema { get; } = new(CompatibilitySchemaValue);

    private const string RecommendationCancelDraftValue = "report.recommendation.cancel_draft";
    public static MessageTemplateId RecommendationCancelDraft { get; } = new(RecommendationCancelDraftValue);

    private const string PackageAvailableValue = "report.package.available";
    public static MessageTemplateId PackageAvailable { get; } = new(PackageAvailableValue);

    private const string HashesComputedValue = "report.hashes.computed";
    public static MessageTemplateId HashesComputed { get; } = new(HashesComputedValue);
}
