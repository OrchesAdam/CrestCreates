using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Metadata.Abstractions;

public sealed class DescriptorSnapshot : ISnapshotable<DescriptorSnapshot>
{
    public string SnapshotId { get; init; } = string.Empty;
    public string PackageId { get; init; } = string.Empty;
    public string PackageVersion { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<SnapshotEntry> Descriptors { get; init; } = Array.Empty<SnapshotEntry>();
    public IReadOnlyList<DescriptorPackageRelationshipEntry> Relationships { get; init; }
        = Array.Empty<DescriptorPackageRelationshipEntry>();

    public DescriptorSnapshot Snapshot() => new()
    {
        SnapshotId = SnapshotId,
        PackageId = PackageId,
        PackageVersion = PackageVersion,
        CreatedAt = CreatedAt,
        Descriptors = Descriptors.ToArray(),
        Relationships = Relationships.ToArray()
    };
}

public sealed class SnapshotEntry : ISnapshotable<SnapshotEntry>
{
    public DescriptorRef Ref { get; init; }
    public string DescriptorName { get; init; } = string.Empty;
    public DescriptorKind Kind { get; init; }
    public DescriptorState State { get; init; }
    public string ContractHash { get; init; } = string.Empty;
    public string DefinitionHash { get; init; } = string.Empty;
    public string? SupersededById { get; init; }

    public SnapshotEntry Snapshot() => new()
    {
        Ref = Ref,
        DescriptorName = DescriptorName,
        Kind = Kind,
        State = State,
        ContractHash = ContractHash,
        DefinitionHash = DefinitionHash,
        SupersededById = SupersededById
    };
}
