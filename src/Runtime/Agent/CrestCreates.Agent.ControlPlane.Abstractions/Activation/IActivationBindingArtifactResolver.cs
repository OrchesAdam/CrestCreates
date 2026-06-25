using CrestCreates.Metadata.Abstractions.CanonicalHashing;

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
    /// Stores source review hash and manifest hash for a review result.
    /// Called by the ToolService when a review result is created.
    /// </summary>
    void StoreReviewHashes(string tenantId, string reviewResultId, CanonicalHash sourceReviewHash, CanonicalHash manifestHash);

    /// <summary>
    /// Stores the evidence hash for a package preview.
    /// Called by the ToolService when a package preview is created.
    /// </summary>
    void StorePackageHash(string tenantId, string packagePreviewId, CanonicalHash evidenceHash);

    /// <summary>
    /// Stores the envelope hash for an evidence preview.
    /// Called by the ToolService when an evidence preview is created.
    /// </summary>
    void StoreEvidenceHash(string tenantId, string evidencePreviewId, CanonicalHash envelopeHash);
}

/// <summary>
/// Current hash state of artifacts referenced in a binding snapshot.
/// Null values indicate the artifact no longer exists (drift).
/// </summary>
public sealed record ResolvedBindingArtifacts
{
    public CanonicalHash? CurrentSourceReviewHash { get; init; }
    public CanonicalHash? CurrentManifestHash { get; init; }
    public CanonicalHash? CurrentEvidenceHash { get; init; }
    public CanonicalHash? CurrentEnvelopeHash { get; init; }
    public CanonicalHash? CurrentContractHash { get; init; }
    public CanonicalHash? CurrentDefinitionHash { get; init; }
}
