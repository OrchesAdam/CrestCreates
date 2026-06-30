using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed class DescriptorPackage : ISnapshotable<DescriptorPackage>
{
    public DescriptorManifest Manifest { get; init; } = new();
    public DescriptorSnapshot SnapshotData { get; init; } = new();
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
    /// Canonical hash of the package manifest. Uses <see cref="Hashes"/> when available.
    /// </summary>
    public string ContentHash => Hashes?.PackageManifestHash.Value ?? string.Empty;

    public DescriptorPackage Snapshot() => new()
    {
        Manifest = Manifest.Snapshot(),
        SnapshotData = SnapshotData.Snapshot(),
        Evidence = Evidence.Snapshot(),
        Diagnostics = Diagnostics.ToArray(),
        Hashes = Hashes,
        EvidenceEnvelope = EvidenceEnvelope
    };
}
