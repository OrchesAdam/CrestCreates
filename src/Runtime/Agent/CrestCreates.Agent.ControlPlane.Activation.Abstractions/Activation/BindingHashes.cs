using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;

namespace CrestCreates.Agent.ControlPlane.Abstractions.Activation;

/// <summary>
/// Hashes bound at activation submission time.
/// Atomic unit for binding validation and drift detection.
/// Seven flat CanonicalHash slots for per-slot semantic validation,
/// plus a convenience PackageHashes accessor for atomic retrieval.
/// </summary>
public sealed record BindingHashes
{
    /// <summary>Source-binding hash of the review result (activation binding view).</summary>
    public required CanonicalHash SourceReviewHash { get; init; }
    
    /// <summary>Integrity hash of the review result (manifest view).</summary>
    public required CanonicalHash ReviewManifestHash { get; init; }
    
    /// <summary>Canonical hash of the package manifest.</summary>
    public required CanonicalHash PackageManifestHash { get; init; }
    
    /// <summary>Canonical hash of the package evidence payload.</summary>
    public required CanonicalHash PackageEvidenceHash { get; init; }
    
    /// <summary>Canonical hash of the package evidence envelope.</summary>
    public required CanonicalHash PackageEvidenceEnvelopeHash { get; init; }
    
    /// <summary>Contract hash of the descriptor.</summary>
    public required CanonicalHash ContractHash { get; init; }
    
    /// <summary>Definition hash of the descriptor.</summary>
    public required CanonicalHash DefinitionHash { get; init; }

    /// <summary>
    /// Convenience accessor that constructs a <see cref="DescriptorPackageHashSet"/>
    /// from the three package hash slots. Enables atomic package hash retrieval
    /// for comparison and display purposes.
    /// </summary>
    public DescriptorPackageHashSet PackageHashes => new()
    {
        PackageManifestHash = PackageManifestHash,
        PackageEvidenceHash = PackageEvidenceHash,
        PackageEvidenceEnvelopeHash = PackageEvidenceEnvelopeHash
    };
}
