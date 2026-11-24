using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

internal sealed class AssetProductionEvolutionActivation(
    IDescriptorActivationRequestService inner,
    IDescriptorDraftStore drafts,
    IAgentPackageArtifactStore packages,
    IDescriptorPackageSerializer packageSerializer,
    IActivationEvidenceRechecker evidenceRechecker) : IDescriptorActivationRequestService
{
    public async Task<AgentToolResult<ActivationRequest>> CreateActivationRequestAsync(
        AgentToolInvocationContext context, SubmitActivationRequestRequest request, CancellationToken ct = default)
    {
        var draft = await drafts.GetAsync(context.TenantId, request.DraftId, ct);
        if (draft is null || !AssetProductionEvolutionGuard.TargetsProductionWorkflow(draft))
            return await inner.CreateActivationRequestAsync(context, request, ct);

        WorkflowDescriptor workflow;
        try
        {
            workflow = AssetProductionEvolutionGuard.RequireDraft(draft, context.TenantId);
        }
        catch (InvalidOperationException exception)
        {
            return Reject(exception.Message);
        }

        if (request.BindingSnapshot is { Hashes: { } hashes } binding &&
            !string.IsNullOrWhiteSpace(binding.PackagePreviewId))
        {
            var pin = AssetApprovedRelease.Pin(workflow);
            var artifact = await packages.GetPackageAsync(new AgentPackageArtifactKey(context.TenantId, binding.PackagePreviewId), ct);
            if (binding.TenantId != context.TenantId || binding.DraftId != draft.DraftId || binding.DraftVersion != workflow.Version ||
                artifact is null || artifact.Owner.DraftId != draft.DraftId ||
                pin.ContractHash != hashes.ContractHash || pin.DefinitionHash != hashes.DefinitionHash ||
                !AssetProductionEvolutionBaseline.MatchesProposedManifest(packageSerializer.Deserialize(artifact.PackageJson).Manifest, workflow))
            {
                return Reject("Asset production activation evidence must bind the validated workflow and exact compiled baseline.");
            }

            var evidence = await evidenceRechecker.RecheckAsync(context.TenantId, binding, ct);
            if (evidence.IsStale)
                return Reject("Asset production activation evidence changed after deterministic review.");
        }

        // Production evolution always goes through the existing human review route.
        var governance = request.GovernanceDecision == DescriptorLifecycleDecisionKind.Blocked
            ? DescriptorLifecycleDecisionKind.Blocked
            : DescriptorLifecycleDecisionKind.ReviewRequired;
        return await inner.CreateActivationRequestAsync(context, request with { GovernanceDecision = governance }, ct);
    }

    private static AgentToolResult<ActivationRequest> Reject(string message) =>
        AgentToolResult<ActivationRequest>.InvalidRequest([new AgentToolDiagnostic
        {
            Code = new DiagnosticCode(AssetProductionEvolutionGuard.DiagnosticCode),
            Severity = SeverityLevel.Error,
            Message = message
        }]);

    public Task<AgentToolResult<DescriptorActivationDecision>> EvaluateActivationEligibilityAsync(
        AgentToolInvocationContext context, string draftId, DescriptorLifecycleDecisionKind? governanceDecision = null,
        CancellationToken ct = default) => inner.EvaluateActivationEligibilityAsync(context, draftId, governanceDecision, ct);

    public Task<AgentToolResult<ActivationRequest>> ApproveActivationRequestAsync(
        AgentToolInvocationContext context, string requestId, DescriptorActivationReviewDecision reviewDecision,
        CancellationToken ct = default, string? completionEventId = null) =>
        inner.ApproveActivationRequestAsync(context, requestId, reviewDecision, ct, completionEventId);

    public Task<AgentToolResult<ActivationRequest>> RejectActivationRequestAsync(
        AgentToolInvocationContext context, string requestId, DescriptorActivationReviewDecision reviewDecision,
        CancellationToken ct = default, string? completionEventId = null) =>
        inner.RejectActivationRequestAsync(context, requestId, reviewDecision, ct, completionEventId);

    public Task<AgentToolResult<ActivationRequest>> RecheckEvidenceAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default) =>
        inner.RecheckEvidenceAsync(context, requestId, ct);

    public Task<AgentToolResult<ActivationRequest>> ExecuteActivationGateAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default) =>
        inner.ExecuteActivationGateAsync(context, requestId, ct);

    public Task<AgentToolResult<ActivationRequest>> CancelActivationRequestAsync(
        AgentToolInvocationContext context, string requestId, string reason, CancellationToken ct = default) =>
        inner.CancelActivationRequestAsync(context, requestId, reason, ct);

    public Task<AgentToolResult<ActivationRequest>> GetActivationRequestStatusAsync(
        AgentToolInvocationContext context, string requestId, CancellationToken ct = default) =>
        inner.GetActivationRequestStatusAsync(context, requestId, ct);
}
