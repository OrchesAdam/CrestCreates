namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorManifest
{
    public string FormatVersion { get; init; } = "1.0";
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public string? Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
    public int DescriptorCount { get; init; }
    public IReadOnlyList<DescriptorManifestEntry> DescriptorEntries { get; init; }
        = Array.Empty<DescriptorManifestEntry>();
    [Obsolete("Use DescriptorPackage.Hashes instead.")]
    public string ContentHash { get; set; } = string.Empty;
    [Obsolete("Use DescriptorPackage.Hashes instead.")]
    public string? EvidenceHash { get; set; }
    [Obsolete("Use DescriptorPackage.Hashes instead.")]
    public string? EnvelopeHash { get; set; }
}

public sealed class DescriptorManifestEntry
{
    public DescriptorRef Ref { get; init; }
    public DescriptorKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }
}
