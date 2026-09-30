using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Agent.ControlPlane.Abstractions.PackageArtifacts;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using DraftHashing = CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>Recomputes binding hashes from immutable review and package artifact authorities.</summary>
public sealed class DefaultActivationBindingArtifactResolver : IActivationBindingArtifactResolver
{
    private readonly IAgentReviewArtifactStore _reviewStore;
    private readonly IAgentPackageArtifactStore _packageStore;
    private readonly DraftHashing.IDescriptorDraftReviewHashService _reviewHashService;
    private readonly IDescriptorPackageSerializer _packageSerializer;
    private readonly IDescriptorPackageCanonicalHashComputer _packageHashComputer;

    public DefaultActivationBindingArtifactResolver(
        IAgentReviewArtifactStore reviewStore,
        IAgentPackageArtifactStore packageStore,
        DraftHashing.IDescriptorDraftReviewHashService reviewHashService,
        IDescriptorPackageSerializer packageSerializer,
        IDescriptorPackageCanonicalHashComputer packageHashComputer)
    {
        _reviewStore = reviewStore ?? throw new ArgumentNullException(nameof(reviewStore));
        _packageStore = packageStore ?? throw new ArgumentNullException(nameof(packageStore));
        _reviewHashService = reviewHashService ?? throw new ArgumentNullException(nameof(reviewHashService));
        _packageSerializer = packageSerializer ?? throw new ArgumentNullException(nameof(packageSerializer));
        _packageHashComputer = packageHashComputer ?? throw new ArgumentNullException(nameof(packageHashComputer));
    }

    public async Task<ResolvedBindingArtifacts> ResolveAsync(
        string tenantId,
        ActivationBindingSnapshot bindingSnapshot,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bindingSnapshot);
        CanonicalHash? sourceReviewHash = null;
        CanonicalHash? reviewManifestHash = null;
        DescriptorPackageHashSet? packageHashes = null;
        DescriptorPackageHashSet? evidenceHashes = null;

        var review = await _reviewStore.GetAsync(tenantId, bindingSnapshot.ReviewResultId, ct);
        if (review is not null)
        {
            sourceReviewHash = _reviewHashService.ComputeSourceReviewHash(review.OriginalHashInput);
            reviewManifestHash = _reviewHashService.ComputeReviewManifestHash(review.OriginalHashInput);
        }

        AgentPackageArtifactEnvelope? package = null;
        if (!string.IsNullOrWhiteSpace(bindingSnapshot.PackagePreviewId))
        {
            package = await _packageStore.GetPackageAsync(
                new AgentPackageArtifactKey(tenantId, bindingSnapshot.PackagePreviewId), ct);
            if (package is not null)
                packageHashes = RecomputePackageHashes(package);
        }

        if (!string.IsNullOrWhiteSpace(bindingSnapshot.EvidencePreviewId))
        {
            var evidence = await _packageStore.GetEvidenceAsync(
                new AgentEvidenceArtifactKey(tenantId, bindingSnapshot.EvidencePreviewId), ct);
            if (evidence is not null && package is not null &&
                StringComparer.Ordinal.Equals(evidence.PackagePreviewId, bindingSnapshot.PackagePreviewId))
            {
                var evidenceParent = await _packageStore.GetPackageAsync(
                    new AgentPackageArtifactKey(tenantId, evidence.PackagePreviewId), ct);
                if (evidenceParent is not null &&
                    StringComparer.Ordinal.Equals(evidenceParent.PackagePreviewId, package.PackagePreviewId) &&
                    StringComparer.Ordinal.Equals(evidenceParent.ScopeFingerprint, package.ScopeFingerprint) &&
                    StringComparer.Ordinal.Equals(evidenceParent.DraftVersion, package.DraftVersion) &&
                    evidenceParent.Owner == package.Owner)
                    evidenceHashes = RecomputePackageHashes(evidenceParent);
            }
        }

        return new ResolvedBindingArtifacts
        {
            CurrentSourceReviewHash = sourceReviewHash,
            CurrentReviewManifestHash = reviewManifestHash,
            CurrentPackageHashes = packageHashes,
            CurrentEvidenceHashes = evidenceHashes,
            CurrentContractHash = null,
            CurrentDefinitionHash = null
        };
    }

    private DescriptorPackageHashSet? RecomputePackageHashes(AgentPackageArtifactEnvelope artifact)
    {
        var content = _packageSerializer.Deserialize(artifact.PackageJson);
        if (content.Hashes is null || content.EvidenceEnvelope is null)
            return null;
        var envelope = content.EvidenceEnvelope;
        var computed = _packageHashComputer.ComputeHashSet(content.Manifest, content.Evidence, new DescriptorPackageEvidenceEnvelopeMetadata
        {
            PackageId = envelope.PackageId,
            PackageVersion = envelope.PackageVersion,
            CreatedAt = envelope.CreatedAt,
            CreatedBy = envelope.CreatedBy,
            Source = envelope.Source
        });
        return content.Hashes == computed &&
            envelope.PackageManifestHash == computed.PackageManifestHash &&
            envelope.PackageEvidenceHash == computed.PackageEvidenceHash &&
            computed == new DescriptorPackageHashSet
            {
                PackageManifestHash = artifact.ProjectedPreview.PackageManifestHash!,
                PackageEvidenceHash = artifact.ProjectedPreview.PackageEvidenceHash!,
                PackageEvidenceEnvelopeHash = artifact.ProjectedPreview.PackageEvidenceEnvelopeHash!
            }
            ? computed
            : null;
    }
}
