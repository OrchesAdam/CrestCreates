using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorPackagePreview
{
    public CanonicalHash? PackageManifestHash { get; init; }
    public CanonicalHash? PackageEvidenceHash { get; init; }
    public CanonicalHash? PackageEvidenceEnvelopeHash { get; init; }
    public required IReadOnlyList<string> DescriptorIds { get; init; }
}
