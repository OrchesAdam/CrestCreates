using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Projections;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using DraftHashing = CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;
using Draft = CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Creates the complete immutable review artifact from an already-computed review.
/// This factory does not authorize callers or persist records; callers must supply
/// the actual review inventory and authorization snapshot established by their flow.
/// </summary>
public sealed class AgentReviewArtifactFactory
{
    private readonly AgentDraftArtifactVisibilityProjector _artifactProjector;
    private readonly DraftHashing.IDescriptorDraftReviewHashService _reviewHashService;

    public AgentReviewArtifactFactory(
        IDescriptorTopologyBuilder topologyBuilder,
        DraftHashing.IDescriptorDraftReviewHashService reviewHashService)
    {
        ArgumentNullException.ThrowIfNull(topologyBuilder);
        ArgumentNullException.ThrowIfNull(reviewHashService);
        _reviewHashService = reviewHashService;
        _artifactProjector = new AgentDraftArtifactVisibilityProjector(
            new AgentTopologyVisibilityProjector(), topologyBuilder);
    }

    /// <summary>
    /// Captures a review under an explicit authorization policy snapshot and the exact
    /// descriptor inventory used by the review. This is a data projection operation,
    /// not an authorization grant; trusted orchestration must authorize before calling.
    /// </summary>
    public AgentReviewArtifactEnvelope Create(
        string reviewResultId,
        DateTimeOffset createdAt,
        DescriptorDraftReviewResult originalReview,
        Draft owner,
        IReadOnlyList<IDescriptor> reviewInventory,
        AgentToolAuthorizationOptions authorizationOptions)
    {
        ArgumentNullException.ThrowIfNull(originalReview);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(reviewInventory);
        ArgumentNullException.ThrowIfNull(authorizationOptions);
        var scope = new AgentDescriptorVisibilityScope(
            originalReview.TenantId,
            new AgentDescriptorKindPolicyEvaluator(authorizationOptions),
            AgentDescriptorVisibilityScope.ComputeFingerprint(authorizationOptions));
        var universeResult = AgentVisibleDescriptorUniverse.TryCreate(reviewInventory, scope);
        if (!universeResult.IsSuccess)
            throw new InvalidOperationException("Could not create a review artifact from an invalid descriptor inventory.");

        if (!TryCreate(reviewResultId, createdAt, originalReview, owner, scope, universeResult.Universe!, out var artifact))
            throw new InvalidOperationException("Could not project the completed review into an immutable artifact.");
        return artifact;
    }

    internal bool TryCreate(
        string reviewResultId,
        DateTimeOffset createdAt,
        DescriptorDraftReviewResult originalReview,
        Draft owner,
        AgentDescriptorVisibilityScope scope,
        AgentVisibleDescriptorUniverse universe,
        out AgentReviewArtifactEnvelope artifact)
    {
        ArgumentNullException.ThrowIfNull(originalReview);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(universe);

        if (!StringComparer.Ordinal.Equals(scope.TenantId, originalReview.TenantId) ||
            !StringComparer.Ordinal.Equals(originalReview.TenantId, owner.TenantId) ||
            !StringComparer.Ordinal.Equals(originalReview.DraftId, owner.DraftId) ||
            !scope.IsVisible(owner.DescriptorKind))
        {
            artifact = null!;
            return false;
        }

        var projectedReview = _artifactProjector.ProjectReview(originalReview, scope, universe);
        if (projectedReview is null)
        {
            artifact = null!;
            return false;
        }

        var reportInput = DescriptorReviewReportInputSnapshot.Capture(new DescriptorReviewReportBuildRequest
        {
            ReviewResult = projectedReview,
            Draft = owner,
            VisibilityApplied = true
        });
        artifact = new AgentReviewArtifactEnvelope
        {
            Version = AgentReviewArtifactEnvelope.CurrentVersion,
            TenantId = originalReview.TenantId,
            ReviewResultId = reviewResultId,
            CreatedAt = createdAt,
            ScopeFingerprint = scope.ScopeFingerprint,
            ProjectedReview = AgentReviewResultDtoProjection.Project(projectedReview),
            ReportInput = reportInput,
            OriginalHashInput = _reviewHashService.CaptureInput(originalReview)
        };
        artifact.Validate();
        return true;
    }
}
