using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed class DescriptorPackage
{
    public DescriptorManifest Manifest { get; init; } = new();
    public DescriptorSnapshot Snapshot { get; init; } = new();
    public DescriptorPackageEvidence Evidence { get; init; } = new();
    public IReadOnlyList<DescriptorPackageDiagnostic> Diagnostics { get; init; }
        = Array.Empty<DescriptorPackageDiagnostic>();

    /// <summary>
    /// Atomic hash set for the package — populated by the builder after canonical hash computation.
    /// Null until the builder populates it.
    /// </summary>
    public DescriptorPackageHashSet? Hashes { get; set; }

    /// <summary>
    /// Evidence envelope binding package identity, evidence, and manifest hashes.
    /// Null until the builder populates it.
    /// </summary>
    public DescriptorPackageEvidenceEnvelope? EvidenceEnvelope { get; set; }

    public string PackageId => Manifest.PackageId;
    public string PackageVersion => Manifest.PackageVersion;

    /// <summary>
    /// Canonical hash of the package manifest. Uses <see cref="Hashes"/> when available,
    /// falls back to <see cref="DescriptorManifest.ContentHash"/> (obsolete) otherwise.
    /// </summary>
    public string ContentHash => Hashes?.PackageManifestHash.Value ?? Manifest.ContentHash;
}
