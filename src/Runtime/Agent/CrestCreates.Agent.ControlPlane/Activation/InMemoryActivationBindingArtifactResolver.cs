using System.Collections.Concurrent;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// In-memory artifact hash resolver. Updated by the ToolService when
/// review results, package previews, and evidence previews are created.
/// Not for production use — persistent stores should replace this.
/// </summary>
public sealed class InMemoryActivationBindingArtifactResolver : IActivationBindingArtifactResolver
{
    private readonly ConcurrentDictionary<(string TenantId, string ReviewResultId), CanonicalHash> _sourceReviewHashes = new();
    private readonly ConcurrentDictionary<(string TenantId, string ReviewResultId), CanonicalHash> _reviewManifestHashes = new();
    private readonly ConcurrentDictionary<(string TenantId, string PreviewId), DescriptorPackageHashSet> _packageHashSets = new();

    public void StoreReviewHashes(string tenantId, string reviewResultId, CanonicalHash sourceReviewHash, CanonicalHash reviewManifestHash)
    {
        _sourceReviewHashes[(tenantId, reviewResultId)] = sourceReviewHash;
        _reviewManifestHashes[(tenantId, reviewResultId)] = reviewManifestHash;
    }

    public void StorePackageHashSet(string tenantId, string previewId, DescriptorPackageHashSet packageHashes)
    {
        _packageHashSets[(tenantId, previewId)] = packageHashes;
    }

    public Task<ResolvedBindingArtifacts> ResolveAsync(
        string tenantId, ActivationBindingSnapshot bindingSnapshot, CancellationToken ct = default)
    {
        CanonicalHash? sourceReviewHash = null;
        CanonicalHash? reviewManifestHash = null;
        DescriptorPackageHashSet? packageHashes = null;

        // Review hashes are keyed by ReviewResultId
        var reviewKey = (tenantId, bindingSnapshot.ReviewResultId);
        _sourceReviewHashes.TryGetValue(reviewKey, out sourceReviewHash);
        _reviewManifestHashes.TryGetValue(reviewKey, out reviewManifestHash);

        // Package hashes are keyed by PackagePreviewId (set by ToolService at preview time)
        var packageKey = (tenantId, bindingSnapshot.PackagePreviewId);
        _packageHashSets.TryGetValue(packageKey, out packageHashes);

        return Task.FromResult(new ResolvedBindingArtifacts
        {
            CurrentSourceReviewHash = sourceReviewHash,
            CurrentReviewManifestHash = reviewManifestHash,
            CurrentPackageHashes = packageHashes,
            CurrentContractHash = null, // Computed separately by rechecker via IDescriptorStableHashBuilder
            CurrentDefinitionHash = null  // Computed separately by rechecker via IDescriptorStableHashBuilder
        });
    }
}
