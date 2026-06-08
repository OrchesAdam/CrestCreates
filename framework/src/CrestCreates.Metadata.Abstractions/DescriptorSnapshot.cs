namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorSnapshot
{
    public string SnapshotId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public IReadOnlyList<SnapshotEntry> Descriptors { get; init; } = Array.Empty<SnapshotEntry>();
}

public sealed class SnapshotEntry
{
    public string DescriptorId { get; init; } = string.Empty;
    public string DescriptorName { get; init; } = string.Empty;
    public DescriptorKind Kind { get; init; }
    public int Version { get; init; }
}