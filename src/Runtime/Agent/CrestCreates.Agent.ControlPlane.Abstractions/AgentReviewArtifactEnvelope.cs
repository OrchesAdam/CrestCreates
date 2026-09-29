using CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Complete immutable review record. The original hash input is retained independently
/// from projected report facts because they represent different review-time views.
/// </summary>
public sealed record AgentReviewArtifactEnvelope
{
    public const int CurrentVersion = 1;

    public required int Version { get; init; }
    public required string TenantId { get; init; }
    public required string ReviewResultId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string ScopeFingerprint { get; init; }
    public required AgentReviewResultDto ProjectedReview { get; init; }
    public required DescriptorReviewReportInputSnapshot ReportInput { get; init; }
    public required DescriptorDraftReviewHashInput OriginalHashInput { get; init; }

    public string DraftId => ProjectedReview.DraftId;

    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new NotSupportedException($"Review artifact version '{Version}' is not supported.");
        Require(TenantId, nameof(TenantId));
        Require(ReviewResultId, nameof(ReviewResultId));
        Require(ScopeFingerprint, nameof(ScopeFingerprint));
        ArgumentNullException.ThrowIfNull(ProjectedReview);
        ArgumentNullException.ThrowIfNull(ProjectedReview.ValidationResult);
        ArgumentNullException.ThrowIfNull(ReportInput);
        ArgumentNullException.ThrowIfNull(OriginalHashInput);
        ArgumentNullException.ThrowIfNull(ProjectedReview.Diagnostics);
        ArgumentNullException.ThrowIfNull(ProjectedReview.ValidationResult.Diagnostics);
        if (ProjectedReview.MaterializationSummary is { } materialization)
        {
            ArgumentNullException.ThrowIfNull(materialization.ProposedInventoryRefs);
            ArgumentNullException.ThrowIfNull(materialization.Diagnostics);
        }
        if (ProjectedReview.ProposedInventorySummary is { } inventory)
        {
            ArgumentNullException.ThrowIfNull(inventory.DescriptorRefs);
            ArgumentNullException.ThrowIfNull(inventory.CountsByKind);
        }
        if (ProjectedReview.TopologySummary is { } topology)
        {
            ArgumentNullException.ThrowIfNull(topology.NodeCountsByKind);
            ArgumentNullException.ThrowIfNull(topology.EdgeCountsByKind);
        }
        if (ProjectedReview.ImpactAnalysisSummary is { } impact)
            ArgumentNullException.ThrowIfNull(impact.AffectedDescriptors);
        if (ProjectedReview.GovernanceSummary is { } governance)
            Require(governance.Decision, "ProjectedReview.GovernanceSummary.Decision");

        ReportInput.Validate();
        OriginalHashInput.Validate();
        if (!StringComparer.Ordinal.Equals(TenantId, ProjectedReview.TenantId) ||
            !StringComparer.Ordinal.Equals(TenantId, ReportInput.TenantId) ||
            !StringComparer.Ordinal.Equals(TenantId, ReportInput.Owner.TenantId) ||
            !StringComparer.Ordinal.Equals(TenantId, OriginalHashInput.SourceBinding.TenantId))
            throw new ArgumentException("Review artifact tenant identity is inconsistent.");

        if (!StringComparer.Ordinal.Equals(ProjectedReview.DraftId, ReportInput.DraftId) ||
            !StringComparer.Ordinal.Equals(ProjectedReview.DraftId, ReportInput.Owner.DraftId) ||
            !StringComparer.Ordinal.Equals(ProjectedReview.DraftId, OriginalHashInput.SourceBinding.DraftId))
            throw new ArgumentException("Review artifact draft identity is inconsistent.");

        if (ProjectedReview.ValidationResult.IsValid != ReportInput.IsValid ||
            ProjectedReview.IsActivationEligible != ReportInput.IsActivationEligible)
            throw new ArgumentException("Review artifact validity or eligibility facts are inconsistent.");

        var reportDecision = ReportInput.GovernanceMaxDecision?.ToString();
        var projectedDecision = ProjectedReview.GovernanceSummary?.Decision;
        var projectedApproved = ProjectedReview.GovernanceSummary?.IsApproved;
        if (!StringComparer.Ordinal.Equals(reportDecision, projectedDecision) ||
            (reportDecision is null ? projectedApproved is not null : projectedApproved != (ReportInput.GovernanceMaxDecision == DescriptorLifecycleDecisionKind.Allowed)))
            throw new ArgumentException("Review artifact governance facts are inconsistent.");
    }

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Review artifact {name} is required.", name);
    }
}
