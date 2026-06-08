namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorManifest
{
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public IReadOnlyList<DescriptorManifestEntry> Schemas { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> Capabilities { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> Events { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> Workflows { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> Forms { get; init; } = Array.Empty<DescriptorManifestEntry>();
    public IReadOnlyList<DescriptorManifestEntry> HumanTasks { get; init; } = Array.Empty<DescriptorManifestEntry>();
}

public sealed class DescriptorManifestEntry
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
}