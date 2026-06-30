using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorManifest : ISnapshotable<DescriptorManifest>
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

    public DescriptorManifest Snapshot() => new()
    {
        FormatVersion = FormatVersion,
        PackageId = PackageId,
        PackageVersion = PackageVersion,
        Name = Name,
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy,
        Source = Source,
        DescriptorCount = DescriptorCount,
        DescriptorEntries = DescriptorEntries.ToArray()
    };
}

public sealed class DescriptorManifestEntry : ISnapshotable<DescriptorManifestEntry>
{
    public DescriptorRef Ref { get; init; }
    public DescriptorKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public DescriptorState State { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }

    public DescriptorManifestEntry Snapshot() => new()
    {
        Ref = Ref,
        Kind = Kind,
        Name = Name,
        State = State,
        ContractHash = ContractHash,
        DefinitionHash = DefinitionHash,
        SupersededById = SupersededById
    };
}
