using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Prompting;

public sealed record DescriptorAuthoringMemoryItemProjection : ISnapshotable<DescriptorAuthoringMemoryItemProjection>
{
    public required string MemoryId { get; init; }
    public required AgentMemoryKind Kind { get; init; }
    public required string Content { get; init; }
    public AgentMemoryConfidence Confidence { get; init; } = AgentMemoryConfidence.Unknown;
    public CanonicalHash? CanonicalContentHash { get; init; }
    public IReadOnlyList<DescriptorRef> DescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();

    public DescriptorAuthoringMemoryItemProjection Snapshot() => this with
    {
        DescriptorRefs = DescriptorRefs.ToArray()
    };
}

public sealed record DescriptorAuthoringMemoryProjection : ISnapshotable<DescriptorAuthoringMemoryProjection>
{
    public required bool IsAuthoritative { get; init; }
    public CanonicalHash? ScopeFingerprint { get; init; }
    public CanonicalHash? VisibleMemorySetHash { get; init; }
    public CanonicalHash? CanonicalPackHash { get; init; }
    public IReadOnlyList<DescriptorAuthoringMemoryItemProjection> Memories { get; init; } = Array.Empty<DescriptorAuthoringMemoryItemProjection>();

    public DescriptorAuthoringMemoryProjection Snapshot() => this with
    {
        Memories = Memories.Select(m => m.Snapshot()).ToArray()
    };
}
