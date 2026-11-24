using System.Globalization;
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Sample.AssetManagement.Contracts;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Sample.AssetManagement.Host.ApprovedReleases;

/// <summary>Issues a deployment receipt in the trusted Asset deployment process after Control Plane activation.</summary>
/// <remarks>The deployment process supplies the governance services and the configured signing key.</remarks>
public sealed class AssetApprovedReleaseIssuer(
    IAgentControlPlaneToolService controlPlane,
    IActivationBindingArtifactResolver artifactResolver,
    IAgentPackageArtifactStore artifactStore,
    IDescriptorPackageSerializer packageSerializer,
    IDescriptorDraftStore draftStore,
    IConfiguration configuration)
{
    /// <summary>Issues a receipt for the tenant in the authorized governance invocation context.</summary>
    public async Task<string> IssueAsync(
        AgentToolInvocationContext context,
        string initialTaskRequestId,
        string workflowRequestId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(initialTaskRequestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowRequestId);

        var baseline = AssetApprovedRelease.GetBaseline();
        var baseWorkflow = AssetDescriptorCatalog.MaintenanceWorkflow;
        var (taskApproval, task, taskManifest) = await ReadApprovalAsync<HumanTaskDescriptor>(
            context, initialTaskRequestId, DescriptorKind.HumanTask, AssetContractIds.MaintenanceInitialHumanTask,
            DescriptorDraftOperation.Create, baseVersion: null, ct);
        VerifyManifest(taskManifest, [..baseline, task]);

        var (workflowApproval, workflow, workflowManifest) = await ReadApprovalAsync<WorkflowDescriptor>(
            context, workflowRequestId, DescriptorKind.Workflow, AssetContractIds.MaintenanceWorkflow,
            DescriptorDraftOperation.Update, baseWorkflow.Version.ToString(CultureInfo.InvariantCulture), ct);
        // An Update preview replaces its base in the proposed inventory. Runtime composition retains it for existing pins.
        VerifyManifest(workflowManifest, [..baseline.Where(descriptor => descriptor != baseWorkflow), task, workflow]);

        var release = new AssetApprovedRelease(AssetApprovedRelease.CurrentFormatVersion, context.TenantId,
            AssetApprovedRelease.PinBaseline(baseline), taskApproval, task, workflowApproval, workflow);
        release.Validate(baseline);
        var signingKey = configuration[AssetApprovedRelease.SigningKeySetting];
        var receipt = AssetApprovedRelease.Sign(release, signingKey);
        // Check the typed wire round trip before issuing an artifact a runtime host must be able to open.
        AssetApprovedRelease.Load(receipt, signingKey, baseline);
        return receipt;
    }

    private async Task<(AssetDescriptorApproval Approval, T Descriptor, DescriptorManifest Manifest)> ReadApprovalAsync<T>(
        AgentToolInvocationContext context,
        string requestId,
        DescriptorKind kind,
        string descriptorId,
        DescriptorDraftOperation operation,
        string? baseVersion,
        CancellationToken ct) where T : class, IVersionedDescriptor
    {
        var tenantId = context.TenantId;
        var status = await controlPlane.GetActivationRequestStatusAsync(
            context with { ToolName = AgentToolName.GetActivationRequestStatus }, requestId, ct);
        if (status.Status != AgentToolResultStatus.Success
            || status.Value is not { Status: ActivationRequestStatus.Activated, Eligibility: DescriptorActivationEligibility.RequiresHumanReview } request
            || request.RequestId != requestId || request.TenantId != tenantId
            || request.BindingSnapshot.TenantId != tenantId || request.BindingSnapshot.DraftId != request.DraftId)
        {
            throw new InvalidOperationException("An Asset approved release requires an Activated human-reviewed request for the same tenant.");
        }

        var binding = request.BindingSnapshot;
        var version = binding.DraftVersion.ToString(CultureInfo.InvariantCulture);
        var draft = await draftStore.GetAsync(tenantId, binding.DraftId, ct);
        if (draft is null || draft.TenantId != tenantId || draft.DraftId != binding.DraftId
            || draft.DescriptorKind != kind || draft.DescriptorId != descriptorId || draft.Operation != operation
            || draft.BaseVersion != baseVersion || draft.ProposedVersion != version
            || draft.Payload.GetDescriptor() is not T descriptor || descriptor.Id != descriptorId || descriptor.Version != binding.DraftVersion)
        {
            throw new InvalidOperationException("The Asset approved release draft does not match the expected descriptor and base version.");
        }

        var preview = await controlPlane.GetPackagePreviewAsync(
            context with { ToolName = AgentToolName.GetPackagePreview }, binding.PackagePreviewId, ct);
        var artifact = await artifactStore.GetPackageAsync(new AgentPackageArtifactKey(tenantId, binding.PackagePreviewId), ct);
        if (preview.Status != AgentToolResultStatus.Success || preview.Value is not { } packagePreview
            || artifact is null || artifact.TenantId != tenantId || artifact.PackagePreviewId != binding.PackagePreviewId
            || artifact.Owner.DraftId != draft.DraftId || artifact.Owner.DescriptorId != descriptorId
            || artifact.Owner.DescriptorKind != kind || artifact.Owner.Operation != operation
            || artifact.Owner.BaseVersion != baseVersion || artifact.Owner.ProposedVersion != version)
        {
            throw new InvalidOperationException("The Asset approved release package does not belong to the activated draft.");
        }

        var artifacts = await artifactResolver.ResolveAsync(tenantId, binding, ct);
        var hashes = binding.Hashes;
        var package = packageSerializer.Deserialize(artifact.PackageJson);
        var pin = AssetApprovedRelease.Pin(descriptor);
        if (artifacts.CurrentSourceReviewHash != hashes.SourceReviewHash
            || artifacts.CurrentReviewManifestHash != hashes.ReviewManifestHash
            || artifacts.CurrentPackageHashes != hashes.PackageHashes || artifacts.CurrentEvidenceHashes != hashes.PackageHashes
            || packagePreview.PackageManifestHash != hashes.PackageManifestHash
            || packagePreview.PackageEvidenceHash != hashes.PackageEvidenceHash
            || packagePreview.PackageEvidenceEnvelopeHash != hashes.PackageEvidenceEnvelopeHash
            || package.Hashes != hashes.PackageHashes || package.PackageId != draft.DraftId || package.PackageVersion != version
            || pin.ContractHash != hashes.ContractHash || pin.DefinitionHash != hashes.DefinitionHash)
        {
            throw new InvalidOperationException("The Asset approved release evidence or descriptor hashes do not match the activation binding.");
        }

        return (new AssetDescriptorApproval(requestId, binding, pin), descriptor, package.Manifest);
    }

    private static void VerifyManifest(DescriptorManifest manifest, IReadOnlyList<IDescriptor> expectedInventory)
    {
        var expected = expectedInventory.Cast<IVersionedDescriptor>().ToDictionary(pin => AssetApprovedRelease.Pin(pin).Ref);
        if (manifest.DescriptorCount != expected.Count || manifest.DescriptorEntries.Count != expected.Count)
            throw new InvalidOperationException("The Asset approved release manifest does not match the deployed baseline and approved changes.");

        foreach (var entry in manifest.DescriptorEntries)
        {
            if (!expected.Remove(entry.Ref, out var descriptor))
                throw new InvalidOperationException("The Asset approved release manifest contains an unexpected descriptor.");

            var pin = AssetApprovedRelease.Pin(descriptor);
            if (entry.Kind != descriptor.Kind || entry.ContractHash != pin.ContractHash.Value || entry.DefinitionHash != pin.DefinitionHash.Value)
                throw new InvalidOperationException("The Asset approved release manifest descriptor differs from the deployed baseline or approved change.");
        }
    }
}
