using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Resolves current artifact hashes for activation evidence recheck from immutable stores.
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
