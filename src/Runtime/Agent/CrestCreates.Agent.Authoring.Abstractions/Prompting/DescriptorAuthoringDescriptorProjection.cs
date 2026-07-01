using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Prompting;

public sealed record DescriptorAuthoringDescriptorProjection : ISnapshotable<DescriptorAuthoringDescriptorProjection>
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public string? Name { get; init; }
    public CanonicalHash? ContractHash { get; init; }
    public CanonicalHash? DefinitionHash { get; init; }

    public DescriptorAuthoringDescriptorProjection Snapshot() => this;
}

public sealed record DescriptorAuthoringMetadataContextProjection : ISnapshotable<DescriptorAuthoringMetadataContextProjection>
{
    public IReadOnlyList<DescriptorAuthoringDescriptorProjection> Descriptors { get; init; } = Array.Empty<DescriptorAuthoringDescriptorProjection>();
    public IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();

    public DescriptorAuthoringMetadataContextProjection Snapshot() => this with
    {
        Descriptors = Descriptors.Select(d => d.Snapshot()).ToArray(),
        VisibleDescriptorRefs = VisibleDescriptorRefs.ToArray()
    };
}
