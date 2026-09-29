using System.Collections.Concurrent;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Agent.ControlPlane.Abstractions;
using DraftHashing = CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.ControlPlane.Activation;

/// <summary>
/// Resolves review hashes from the configured review authority and keeps the
/// package/evidence preview hashes in memory until their separate cutovers.
/// Not for production use while package/evidence previews remain volatile.
/// </summary>
public sealed class InMemoryActivationBindingArtifactResolver : IActivationBindingArtifactResolver
{
    private readonly IAgentReviewArtifactStore _reviewStore;
    private readonly DraftHashing.IDescriptorDraftReviewHashService _reviewHashService;
    private readonly ConcurrentDictionary<(string TenantId, string PackagePreviewId), DescriptorPackageHashSet> _packageHashSets = new();
    private readonly ConcurrentDictionary<(string TenantId, string EvidencePreviewId), DescriptorPackageHashSet> _evidenceHashSets = new();

    public InMemoryActivationBindingArtifactResolver(
        IAgentReviewArtifactStore reviewStore,
        DraftHashing.IDescriptorDraftReviewHashService reviewHashService)
    {
        _reviewStore = reviewStore;
        _reviewHashService = reviewHashService;
    }

    public void StorePackageHashes(string tenantId, string packagePreviewId, DescriptorPackageHashSet packageHashes)
    {
        _packageHashSets[(tenantId, packagePreviewId)] = packageHashes;
    }

    public void StoreEvidenceHashes(string tenantId, string evidencePreviewId, DescriptorPackageHashSet evidenceHashes)
    {
        _evidenceHashSets[(tenantId, evidencePreviewId)] = evidenceHashes;
    }

    public async Task<ResolvedBindingArtifacts> ResolveAsync(
        string tenantId, ActivationBindingSnapshot bindingSnapshot, CancellationToken ct = default)
    {
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

        // Package hashes are keyed by PackagePreviewId
        var packageKey = (tenantId, bindingSnapshot.PackagePreviewId);
        _packageHashSets.TryGetValue(packageKey, out packageHashes);

        // Evidence hashes are keyed by EvidencePreviewId
        var evidenceKey = (tenantId, bindingSnapshot.EvidencePreviewId);
        _evidenceHashSets.TryGetValue(evidenceKey, out evidenceHashes);

        return new ResolvedBindingArtifacts
        {
            CurrentSourceReviewHash = sourceReviewHash,
            CurrentReviewManifestHash = reviewManifestHash,
            CurrentPackageHashes = packageHashes,
            CurrentEvidenceHashes = evidenceHashes,
            CurrentContractHash = null, // Computed separately by rechecker via IDescriptorStableHashBuilder
            CurrentDefinitionHash = null  // Computed separately by rechecker via IDescriptorStableHashBuilder
        };
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
