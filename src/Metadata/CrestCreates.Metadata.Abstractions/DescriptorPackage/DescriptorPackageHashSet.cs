using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

/// <summary>
/// Atomic hash set for package manifest, evidence, and evidence envelope.
/// Stored and resolved as a unit — never split across unrelated methods.
/// </summary>
public sealed record DescriptorPackageHashSet
{
    public required CanonicalHash PackageManifestHash { get; init; }
    public required CanonicalHash PackageEvidenceHash { get; init; }
    public required CanonicalHash PackageEvidenceEnvelopeHash { get; init; }
}
