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
    private readonly ConcurrentDictionary<(string TenantId, string PackagePreviewId), DescriptorPackageHashSet> _packageHashSets = new();
    private readonly ConcurrentDictionary<(string TenantId, string EvidencePreviewId), DescriptorPackageHashSet> _evidenceHashSets = new();

    public void StoreReviewHashes(string tenantId, string reviewResultId, CanonicalHash sourceReviewHash, CanonicalHash reviewManifestHash)
    {
        _sourceReviewHashes[(tenantId, reviewResultId)] = sourceReviewHash;
        _reviewManifestHashes[(tenantId, reviewResultId)] = reviewManifestHash;
    }

    public void StorePackageHashes(string tenantId, string packagePreviewId, DescriptorPackageHashSet packageHashes)
    {
        _packageHashSets[(tenantId, packagePreviewId)] = packageHashes;
    }

    public void StoreEvidenceHashes(string tenantId, string evidencePreviewId, DescriptorPackageHashSet evidenceHashes)
    {
        _evidenceHashSets[(tenantId, evidencePreviewId)] = evidenceHashes;
    }

    public Task<ResolvedBindingArtifacts> ResolveAsync(
        string tenantId, ActivationBindingSnapshot bindingSnapshot, CancellationToken ct = default)
    {
        CanonicalHash? sourceReviewHash = null;
        CanonicalHash? reviewManifestHash = null;
        DescriptorPackageHashSet? packageHashes = null;
        DescriptorPackageHashSet? evidenceHashes = null;

        // Review hashes are keyed by ReviewResultId
        var reviewKey = (tenantId, bindingSnapshot.ReviewResultId);
        _sourceReviewHashes.TryGetValue(reviewKey, out sourceReviewHash);
        _reviewManifestHashes.TryGetValue(reviewKey, out reviewManifestHash);

        // Package hashes are keyed by PackagePreviewId
        var packageKey = (tenantId, bindingSnapshot.PackagePreviewId);
        _packageHashSets.TryGetValue(packageKey, out packageHashes);

        // Evidence hashes are keyed by EvidencePreviewId
        var evidenceKey = (tenantId, bindingSnapshot.EvidencePreviewId);
        _evidenceHashSets.TryGetValue(evidenceKey, out evidenceHashes);

        return Task.FromResult(new ResolvedBindingArtifacts
        {
            CurrentSourceReviewHash = sourceReviewHash,
            CurrentReviewManifestHash = reviewManifestHash,
            CurrentPackageHashes = packageHashes,
            CurrentEvidenceHashes = evidenceHashes,
            CurrentContractHash = null, // Computed separately by rechecker via IDescriptorStableHashBuilder
            CurrentDefinitionHash = null  // Computed separately by rechecker via IDescriptorStableHashBuilder
        });
    }

    // ── Test-accessible read-only views ──

    /// <summary>
    /// Total number of stored package hash sets.
    /// </summary>
    public int PackageHashSetCount => _packageHashSets.Count;

    /// <summary>
    /// Total number of stored evidence hash sets.
    /// </summary>
    public int EvidenceHashSetCount => _evidenceHashSets.Count;

    /// <summary>
    /// Retrieves a stored package hash set by tenant and package preview id.
    /// Returns null when no entry is found.
    /// </summary>
    public DescriptorPackageHashSet? GetPackageHashSet(string tenantId, string packagePreviewId)
    {
        _packageHashSets.TryGetValue((tenantId, packagePreviewId), out var hs);
        return hs;
    }

    /// <summary>
    /// Retrieves a stored evidence hash set by tenant and evidence preview id.
    /// Returns null when no entry is found.
    /// </summary>
    public DescriptorPackageHashSet? GetEvidenceHashSet(string tenantId, string evidencePreviewId)
    {
        _evidenceHashSets.TryGetValue((tenantId, evidencePreviewId), out var hs);
        return hs;
    }
}
