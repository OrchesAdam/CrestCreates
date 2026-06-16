namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorPackagePreview
{
    public required string ManifestHash { get; init; }
    public string? SnapshotHash { get; init; }
    public required string EvidenceHash { get; init; }
    public required string EnvelopeHash { get; init; }
    public required IReadOnlyList<string> DescriptorIds { get; init; }
}
