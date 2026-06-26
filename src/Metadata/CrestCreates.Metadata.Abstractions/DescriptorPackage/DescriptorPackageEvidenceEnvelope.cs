using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

/// <summary>
/// Evidence envelope binding package identity, evidence, and manifest hashes
/// into the auditable activation handoff record.
/// </summary>
public sealed class DescriptorPackageEvidenceEnvelope
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
    public required CanonicalHash PackageManifestHash { get; init; }
    public required CanonicalHash PackageEvidenceHash { get; init; }
}
