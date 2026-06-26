using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Resolves current artifact hashes for activation evidence recheck.
/// Implementations store hash snapshots that are updated when artifacts
/// are created or modified in the control plane.
/// </summary>
public interface IActivationBindingArtifactResolver
{
    /// <summary>
    /// Resolves the current hashes for all artifacts referenced in the binding snapshot.
    /// Returns null for any hash where the artifact no longer exists (counts as drift).
    /// </summary>
    Task<ResolvedBindingArtifacts> ResolveAsync(
        string tenantId,
        ActivationBindingSnapshot bindingSnapshot,
        CancellationToken ct = default);

    /// <summary>
    /// Stores source review hash and review manifest hash for a review result.
    /// Called by the ToolService when a review result is created.
    /// </summary>
    void StoreReviewHashes(string tenantId, string reviewResultId, CanonicalHash sourceReviewHash, CanonicalHash reviewManifestHash);

    /// <summary>
    /// Stores the package hash set (manifest, evidence, envelope) for a package preview.
    /// Called by the ToolService when a package preview is created.
    /// </summary>
    void StorePackageHashes(string tenantId, string packagePreviewId, DescriptorPackageHashSet packageHashes);

    /// <summary>
    /// Stores the evidence hash set (manifest, evidence, envelope) for an evidence preview.
    /// Called by the ToolService when an evidence preview is created.
    /// </summary>
    void StoreEvidenceHashes(string tenantId, string evidencePreviewId, DescriptorPackageHashSet evidenceHashes);
}

/// <summary>
/// Current hash state of artifacts referenced in a binding snapshot.
/// Null values indicate the artifact no longer exists (drift).
/// </summary>
public sealed record ResolvedBindingArtifacts
{
    public CanonicalHash? CurrentSourceReviewHash { get; init; }
    public CanonicalHash? CurrentReviewManifestHash { get; init; }
    /// <summary>
    /// Package hashes resolved from the package preview (keyed by PackagePreviewId).
    /// Used to verify PackageManifestHash in the binding snapshot.
    /// </summary>
    public DescriptorPackageHashSet? CurrentPackageHashes { get; init; }
    /// <summary>
    /// Evidence hashes resolved from the evidence preview (keyed by EvidencePreviewId).
    /// Used to verify PackageEvidenceHash and PackageEvidenceEnvelopeHash in the binding snapshot.
    /// </summary>
    public DescriptorPackageHashSet? CurrentEvidenceHashes { get; init; }
    public CanonicalHash? CurrentContractHash { get; init; }
    public CanonicalHash? CurrentDefinitionHash { get; init; }
}
