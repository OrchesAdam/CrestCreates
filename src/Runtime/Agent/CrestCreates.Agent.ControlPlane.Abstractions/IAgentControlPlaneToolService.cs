using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Read-only Control Plane tools — context building, descriptor lookup, search, relationships, topology.
/// Aligned with <see cref="AgentToolAuthorizationMode"/>'s read-only tier.
/// </summary>
public interface IReadOnlyControlPlaneTools
{
    // ── Wave 1 — Context / Read ──

    Task<AgentToolResult<MetadataContextPack>> BuildMetadataContextPackAsync(
        AgentToolInvocationContext context,
        MetadataContextPackRequest request,
        CancellationToken ct = default);

    Task<AgentToolResult<MetadataContextPack>> BuildRuntimeScenarioContextPackAsync(
        AgentToolInvocationContext context,
        MetadataContextPackRequest request,
        CancellationToken ct = default);

    Task<AgentToolResult<DescriptorInfo>> GetDescriptorByRefAsync(
        AgentToolInvocationContext context,
        DescriptorRef descriptorRef,
        CancellationToken ct = default);

    Task<AgentToolResult<DescriptorSearchResult>> SearchDescriptorsAsync(
        AgentToolInvocationContext context,
        DescriptorSearchRequest request,
        CancellationToken ct = default);

    Task<AgentToolResult<DescriptorRelationshipsResult>> ListDescriptorRelationshipsAsync(
        AgentToolInvocationContext context,
        DescriptorRef descriptorRef,
        CancellationToken ct = default);

    Task<AgentToolResult<TopologySummaryResult>> GetTopologySummaryAsync(
        AgentToolInvocationContext context,
        CancellationToken ct = default);
}

/// <summary>
/// Mutation Control Plane tools — draft CRUD, review, fix proposals, package previews.
/// Aligned with <see cref="AgentToolAuthorizationMode"/>'s mutation tier.
/// </summary>
public interface IMutationControlPlaneTools
{
    // ── Wave 2 — Draft ──

    Task<AgentToolResult<AgentDescriptorDraftDto>> CreateDescriptorDraftAsync(
        AgentToolInvocationContext context,
        CreateDescriptorDraftRequest request,
        CancellationToken ct = default);

    Task<AgentToolResult<AgentDescriptorDraftDto>> UpdateDescriptorDraftAsync(
        AgentToolInvocationContext context,
        UpdateDescriptorDraftRequest request,
        CancellationToken ct = default);

    Task<AgentToolResult<AgentDescriptorDraftDto>> GetDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<DescriptorDraftListResult>> ListDescriptorDraftsAsync(
        AgentToolInvocationContext context,
        DraftAbstractions.DraftQuery? query,
        CancellationToken ct = default);

    Task<AgentToolResult<AgentDescriptorDraftDto>> CancelDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<DraftComparisonResult>> CompareDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    // ── Wave 3 — Review ──

    Task<AgentToolResult<DraftAbstractions.DescriptorDraftValidationResult>> ValidateDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<AgentReviewResultDto>> ReviewDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<AgentReviewResultDto>> GetDraftReviewResultAsync(
        AgentToolInvocationContext context,
        string reviewResultId,
        CancellationToken ct = default);

    Task<AgentToolResult<ReviewResultListResult>> ListDraftReviewResultsAsync(
        AgentToolInvocationContext context,
        string? draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<DiagnosticExplanation>> ExplainDiagnosticsAsync(
        AgentToolInvocationContext context,
        ExplainDiagnosticsRequest request,
        CancellationToken ct = default);

    // ── Wave 3d — Review Report (Phase 7d) ──

    Task<AgentToolResult<DescriptorReviewReportDto>> BuildDescriptorReviewReportAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<string>> RenderDescriptorReviewReportAsync(
        AgentToolInvocationContext context,
        DescriptorReviewReportDto report,
        DescriptorReviewReportFormat format,
        CancellationToken ct = default);

    // ── Wave 4 — Fix Proposal ──

    Task<AgentToolResult<FixProposalListResult>> SuggestDescriptorDraftFixesAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<FixProposal>> GetFixProposalAsync(
        AgentToolInvocationContext context,
        string proposalId,
        CancellationToken ct = default);

    Task<AgentToolResult<FixProposalListResult>> ListFixProposalsAsync(
        AgentToolInvocationContext context,
        string? draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<AgentDescriptorDraftDto>> ApplyFixProposalToDraftAsync(
        AgentToolInvocationContext context,
        ApplyFixProposalRequest request,
        CancellationToken ct = default);

    // ── Wave 5 — Package Preview ──

    Task<AgentToolResult<DraftAbstractions.DescriptorPackagePreview>> PreviewDescriptorPackageAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<PackageEvidencePreview>> BuildPackageEvidencePreviewAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<ActivationReadinessPreview>> BuildActivationReadinessPreviewAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default);

    Task<AgentToolResult<DraftAbstractions.DescriptorPackagePreview>> GetPackagePreviewAsync(
        AgentToolInvocationContext context,
        string previewId,
        CancellationToken ct = default);
}

/// <summary>
/// Activation handoff Control Plane tools — submit, query, cancel activation requests.
/// Aligned with <see cref="AgentToolAuthorizationMode"/>'s activation handoff tier.
/// </summary>
public interface IActivationControlPlaneTools
{
    // ── Wave 6 — Activation Handoff ──

    Task<AgentToolResult<ActivationRequest>> SubmitActivationRequestAsync(
        AgentToolInvocationContext context,
        SubmitActivationRequestRequest request,
        CancellationToken ct = default);

    Task<AgentToolResult<ActivationRequest>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default);

    Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default);
}

/// <summary>
/// The main Control Plane tool surface facade.
/// Composes read, mutation, and activation sub-interfaces aligned with the
/// three-tier authorization model.
/// Every method enforces permission boundary, audit recording,
/// and runtime mutation boundary invariants.
/// </summary>
public interface IAgentControlPlaneToolService
    : IReadOnlyControlPlaneTools,
      IMutationControlPlaneTools,
      IActivationControlPlaneTools
{
}
