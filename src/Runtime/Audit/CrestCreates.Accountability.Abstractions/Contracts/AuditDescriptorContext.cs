using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Accountability.Abstractions.Contracts;

public sealed record AuditDescriptorContext
{
    public ImmutableArray<AuditDescriptorReference> Items { get; init; } = [];
    public string? SnapshotId { get; init; }
    public CanonicalHash? SnapshotHash { get; init; }

    public static AuditDescriptorContext Empty { get; } = new();
}

public sealed record AuditDescriptorReference
{
    public required string Kind { get; init; }
    public required string Id { get; init; }
    public required int Version { get; init; }
    public CanonicalHash? ContractHash { get; init; }
}
