using System.Collections.Concurrent;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// In-memory artifact hash resolver. Updated by the ToolService when
/// review results, package previews, and evidence previews are created.
/// Not for production use — persistent stores should replace this.
/// </summary>
public sealed class InMemoryActivationBindingArtifactResolver : IActivationBindingArtifactResolver
{
    private readonly ConcurrentDictionary<(string TenantId, string ReviewResultId), (CanonicalHash SourceReviewHash, CanonicalHash ManifestHash)> _reviewHashes = new();
    private readonly ConcurrentDictionary<(string TenantId, string PackagePreviewId), CanonicalHash> _packageHashes = new();
    private readonly ConcurrentDictionary<(string TenantId, string EvidencePreviewId), CanonicalHash> _evidenceHashes = new();

    public void StoreReviewHashes(string tenantId, string reviewResultId, CanonicalHash sourceReviewHash, CanonicalHash manifestHash)
    {
        _reviewHashes[(tenantId, reviewResultId)] = (sourceReviewHash, manifestHash);
    }

    public void StorePackageHash(string tenantId, string packagePreviewId, CanonicalHash evidenceHash)
    {
        _packageHashes[(tenantId, packagePreviewId)] = evidenceHash;
    }

    public void StoreEvidenceHash(string tenantId, string evidencePreviewId, CanonicalHash envelopeHash)
    {
        _evidenceHashes[(tenantId, evidencePreviewId)] = envelopeHash;
    }

    public Task<ResolvedBindingArtifacts> ResolveAsync(
        string tenantId, ActivationBindingSnapshot bindingSnapshot, CancellationToken ct = default)
    {
        CanonicalHash? sourceReviewHash = null;
        CanonicalHash? manifestHash = null;
        CanonicalHash? evidenceHash = null;
        CanonicalHash? envelopeHash = null;

        if (_reviewHashes.TryGetValue((tenantId, bindingSnapshot.ReviewResultId), out var reviewEntry))
        {
            sourceReviewHash = reviewEntry.SourceReviewHash;
            manifestHash = reviewEntry.ManifestHash;
        }

        if (bindingSnapshot.PackagePreviewId is not null
            && _packageHashes.TryGetValue((tenantId, bindingSnapshot.PackagePreviewId), out var packageEntry))
        {
            evidenceHash = packageEntry;
        }

        if (bindingSnapshot.EvidencePreviewId is not null
            && _evidenceHashes.TryGetValue((tenantId, bindingSnapshot.EvidencePreviewId), out var evidenceEntry))
        {
            envelopeHash = evidenceEntry;
        }

        return Task.FromResult(new ResolvedBindingArtifacts
        {
            CurrentSourceReviewHash = sourceReviewHash,
            CurrentManifestHash = manifestHash,
            CurrentEvidenceHash = evidenceHash,
            CurrentEnvelopeHash = envelopeHash,
            CurrentContractHash = null, // Computed separately by rechecker via IDescriptorStableHashBuilder
            CurrentDefinitionHash = null  // Computed separately by rechecker via IDescriptorStableHashBuilder
        });
    }
}
