using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.ContextPack.Abstractions;
using Microsoft.Extensions.DependencyInjection;

using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

internal sealed class ControlPlaneToolInterfaceForwarder :
    IReadOnlyControlPlaneTools,
    IMutationControlPlaneTools,
    IActivationControlPlaneTools
{
    private readonly IAgentControlPlaneToolService _facade;

    public ControlPlaneToolInterfaceForwarder(IServiceProvider serviceProvider)
    {
        _facade = serviceProvider.GetRequiredService<IAgentControlPlaneToolService>();
    }

    public Task<AgentToolResult<MetadataContextPack>> BuildMetadataContextPackAsync(
        AgentToolInvocationContext context,
        MetadataContextPackRequest request,
        CancellationToken ct = default) => _facade.BuildMetadataContextPackAsync(context, request, ct);

    public Task<AgentToolResult<MetadataContextPack>> BuildRuntimeScenarioContextPackAsync(
        AgentToolInvocationContext context,
        MetadataContextPackRequest request,
        CancellationToken ct = default) => _facade.BuildRuntimeScenarioContextPackAsync(context, request, ct);

    public Task<AgentToolResult<DescriptorInfo>> GetDescriptorByRefAsync(
        AgentToolInvocationContext context,
        DescriptorRef descriptorRef,
        CancellationToken ct = default) => _facade.GetDescriptorByRefAsync(context, descriptorRef, ct);

    public Task<AgentToolResult<DescriptorSearchResult>> SearchDescriptorsAsync(
        AgentToolInvocationContext context,
        DescriptorSearchRequest request,
        CancellationToken ct = default) => _facade.SearchDescriptorsAsync(context, request, ct);

    public Task<AgentToolResult<DescriptorRelationshipsResult>> ListDescriptorRelationshipsAsync(
        AgentToolInvocationContext context,
        DescriptorRef descriptorRef,
        CancellationToken ct = default) => _facade.ListDescriptorRelationshipsAsync(context, descriptorRef, ct);

    public Task<AgentToolResult<TopologySummaryResult>> GetTopologySummaryAsync(
        AgentToolInvocationContext context,
        CancellationToken ct = default) => _facade.GetTopologySummaryAsync(context, ct);

    public Task<AgentToolResult<AgentDescriptorDraftDto>> CreateDescriptorDraftAsync(
        AgentToolInvocationContext context,
        CreateDescriptorDraftRequest request,
        CancellationToken ct = default) => _facade.CreateDescriptorDraftAsync(context, request, ct);

    public Task<AgentToolResult<AgentDescriptorDraftDto>> UpdateDescriptorDraftAsync(
        AgentToolInvocationContext context,
        UpdateDescriptorDraftRequest request,
        CancellationToken ct = default) => _facade.UpdateDescriptorDraftAsync(context, request, ct);

    public Task<AgentToolResult<AgentDescriptorDraftDto>> GetDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.GetDescriptorDraftAsync(context, draftId, ct);

    public Task<AgentToolResult<DescriptorDraftListResult>> ListDescriptorDraftsAsync(
        AgentToolInvocationContext context,
        DraftAbstractions.DraftQuery? query,
        CancellationToken ct = default) => _facade.ListDescriptorDraftsAsync(context, query, ct);

    public Task<AgentToolResult<AgentDescriptorDraftDto>> CancelDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.CancelDescriptorDraftAsync(context, draftId, ct);

    public Task<AgentToolResult<DraftComparisonResult>> CompareDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.CompareDescriptorDraftAsync(context, draftId, ct);

    public Task<AgentToolResult<DraftAbstractions.DescriptorDraftValidationResult>> ValidateDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.ValidateDescriptorDraftAsync(context, draftId, ct);

    public Task<AgentToolResult<AgentReviewResultDto>> ReviewDescriptorDraftAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.ReviewDescriptorDraftAsync(context, draftId, ct);

    public Task<AgentToolResult<AgentReviewResultDto>> GetDraftReviewResultAsync(
        AgentToolInvocationContext context,
        string reviewResultId,
        CancellationToken ct = default) => _facade.GetDraftReviewResultAsync(context, reviewResultId, ct);

    public Task<AgentToolResult<ReviewResultListResult>> ListDraftReviewResultsAsync(
        AgentToolInvocationContext context,
        string? draftId,
        CancellationToken ct = default) => _facade.ListDraftReviewResultsAsync(context, draftId, ct);

    public Task<AgentToolResult<DiagnosticExplanation>> ExplainDiagnosticsAsync(
        AgentToolInvocationContext context,
        ExplainDiagnosticsRequest request,
        CancellationToken ct = default) => _facade.ExplainDiagnosticsAsync(context, request, ct);

    public Task<AgentToolResult<DescriptorReviewReportDto>> BuildDescriptorReviewReportAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.BuildDescriptorReviewReportAsync(context, draftId, ct);

    public Task<AgentToolResult<string>> RenderDescriptorReviewReportAsync(
        AgentToolInvocationContext context,
        DescriptorReviewReportDto report,
        DescriptorReviewReportFormat format,
        CancellationToken ct = default) => _facade.RenderDescriptorReviewReportAsync(context, report, format, ct);

    public Task<AgentToolResult<FixProposalListResult>> SuggestDescriptorDraftFixesAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.SuggestDescriptorDraftFixesAsync(context, draftId, ct);

    public Task<AgentToolResult<FixProposal>> GetFixProposalAsync(
        AgentToolInvocationContext context,
        string proposalId,
        CancellationToken ct = default) => _facade.GetFixProposalAsync(context, proposalId, ct);

    public Task<AgentToolResult<FixProposalListResult>> ListFixProposalsAsync(
        AgentToolInvocationContext context,
        string? draftId,
        CancellationToken ct = default) => _facade.ListFixProposalsAsync(context, draftId, ct);

    public Task<AgentToolResult<AgentDescriptorDraftDto>> ApplyFixProposalToDraftAsync(
        AgentToolInvocationContext context,
        ApplyFixProposalRequest request,
        CancellationToken ct = default) => _facade.ApplyFixProposalToDraftAsync(context, request, ct);

    public Task<AgentToolResult<DraftAbstractions.DescriptorPackagePreview>> PreviewDescriptorPackageAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.PreviewDescriptorPackageAsync(context, draftId, ct);

    public Task<AgentToolResult<PackageEvidencePreview>> BuildPackageEvidencePreviewAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.BuildPackageEvidencePreviewAsync(context, draftId, ct);

    public Task<AgentToolResult<DraftAbstractions.DescriptorPackagePreview>> GetPackagePreviewAsync(
        AgentToolInvocationContext context,
        string previewId,
        CancellationToken ct = default) => _facade.GetPackagePreviewAsync(context, previewId, ct);

    public Task<AgentToolResult<ActivationReadinessPreview>> BuildActivationReadinessPreviewAsync(
        AgentToolInvocationContext context,
        string draftId,
        CancellationToken ct = default) => _facade.BuildActivationReadinessPreviewAsync(context, draftId, ct);

    public Task<AgentToolResult<ActivationRequest>> SubmitActivationRequestAsync(
        AgentToolInvocationContext context,
        SubmitActivationRequestRequest request,
        CancellationToken ct = default) => _facade.SubmitActivationRequestAsync(context, request, ct);

    public Task<AgentToolResult<ActivationRequest>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default) => _facade.GetActivationRequestStatusAsync(context, requestId, ct);

    public Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context,
        string requestId,
        CancellationToken ct = default) => _facade.CancelActivationRequestAsync(context, requestId, ct);
}
