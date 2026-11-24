using System.Globalization;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;

namespace CrestCreates.Sample.AssetManagement.Host.ProductionRelease;

public sealed class AssetProductionReleaseIssuer(
    IAgentControlPlaneToolService controlPlane,
    IActivationBindingArtifactResolver artifactResolver,
    IAgentPackageArtifactStore artifactStore,
    IDescriptorPackageSerializer packageSerializer,
    IDescriptorDraftStore draftStore,
    IConfiguration configuration)
{
    public async Task<string> IssueAsync(AgentToolInvocationContext context, string workflowRequestId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowRequestId);

        var baseline = AssetProductionEvolutionBaseline.GetDescriptors();
        var status = await controlPlane.GetActivationRequestStatusAsync(
            context with { ToolName = AgentToolName.GetActivationRequestStatus }, workflowRequestId, ct);
        if (status.Status != AgentToolResultStatus.Success ||
            status.Value is not { Status: ActivationRequestStatus.Activated, Eligibility: DescriptorActivationEligibility.RequiresHumanReview } request ||
            request.RequestId != workflowRequestId || request.TenantId != context.TenantId ||
            request.BindingSnapshot.TenantId != context.TenantId || request.BindingSnapshot.DraftId != request.DraftId)
        {
            throw new InvalidOperationException("An Asset production release requires an Activated human-reviewed workflow request for the same tenant.");
        }

        var binding = request.BindingSnapshot;
        var version = binding.DraftVersion.ToString(CultureInfo.InvariantCulture);
        var draft = await draftStore.GetAsync(context.TenantId, binding.DraftId, ct);
        if (draft is null || draft.DraftId != binding.DraftId || draft.ProposedVersion != version)
            throw new InvalidOperationException("The Asset production release draft does not match the activated binding.");
        var workflow = AssetProductionEvolutionGuard.RequireDraft(draft, context.TenantId);

        var preview = await controlPlane.GetPackagePreviewAsync(
            context with { ToolName = AgentToolName.GetPackagePreview }, binding.PackagePreviewId, ct);
        var artifact = await artifactStore.GetPackageAsync(new AgentPackageArtifactKey(context.TenantId, binding.PackagePreviewId), ct);
        if (preview.Status != AgentToolResultStatus.Success || preview.Value is not { } packagePreview ||
            artifact is null || artifact.TenantId != context.TenantId || artifact.PackagePreviewId != binding.PackagePreviewId ||
            artifact.Owner.DraftId != draft.DraftId || artifact.Owner.DescriptorId != workflow.Id ||
            artifact.Owner.DescriptorKind != DescriptorKind.Workflow || artifact.Owner.Operation != DescriptorDraftOperation.Update ||
            artifact.Owner.BaseVersion != draft.BaseVersion || artifact.Owner.ProposedVersion != version)
        {
            throw new InvalidOperationException("The Asset production release package does not belong to the activated draft.");
        }

        var artifacts = await artifactResolver.ResolveAsync(context.TenantId, binding, ct);
        var hashes = binding.Hashes;
        var package = packageSerializer.Deserialize(artifact.PackageJson);
        var pin = ApprovedReleases.AssetApprovedRelease.Pin(workflow);
        if (artifacts.CurrentSourceReviewHash != hashes.SourceReviewHash ||
            artifacts.CurrentReviewManifestHash != hashes.ReviewManifestHash ||
            artifacts.CurrentPackageHashes != hashes.PackageHashes || artifacts.CurrentEvidenceHashes != hashes.PackageHashes ||
            packagePreview.PackageManifestHash != hashes.PackageManifestHash ||
            packagePreview.PackageEvidenceHash != hashes.PackageEvidenceHash ||
            packagePreview.PackageEvidenceEnvelopeHash != hashes.PackageEvidenceEnvelopeHash ||
            package.Hashes != hashes.PackageHashes || package.PackageId != draft.DraftId || package.PackageVersion != version ||
            hashes.ContractHash != pin.ContractHash || hashes.DefinitionHash != pin.DefinitionHash)
        {
            throw new InvalidOperationException("The Asset production release package hashes do not match the activated binding.");
        }

        if (!AssetProductionEvolutionBaseline.MatchesProposedManifest(package.Manifest, workflow))
            throw new InvalidOperationException("The Asset production release manifest does not match the deployed baseline and approved workflow.");

        var approval = new AssetDescriptorApproval(request.RequestId, binding, pin);
        var release = new AssetProductionRelease(AssetProductionRelease.CurrentFormatVersion, context.TenantId,
            ApprovedReleases.AssetApprovedRelease.PinBaseline(baseline), approval, workflow);
        release.Validate(baseline);
        var signingKey = configuration[AssetProductionRelease.SigningKeySetting];
        var receipt = AssetProductionRelease.Sign(release, signingKey);
        AssetProductionRelease.Load(receipt, signingKey, baseline);
        return receipt;
    }
}
