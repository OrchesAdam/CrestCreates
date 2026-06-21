using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// The main Control Plane tool surface facade.
/// Every method enforces permission boundary, audit recording,
/// and runtime mutation boundary invariants.
/// </summary>
public interface IAgentControlPlaneToolService
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
