namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

/// <summary>
/// Metadata for constructing a DescriptorPackageEvidenceEnvelope.
/// Separated from the envelope itself because the envelope contains computed hashes
/// that are only available after canonical hash computation.
/// </summary>
public sealed class DescriptorPackageEvidenceEnvelopeMetadata
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
}
