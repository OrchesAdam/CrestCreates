using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Prompting;

public sealed record DescriptorAuthoringPromptInput : ISnapshotable<DescriptorAuthoringPromptInput>
{
    public required string ContractVersion { get; init; }
    public required string TenantId { get; init; }
    public required string IntentText { get; init; }
    public required DescriptorAuthoringMetadataContextProjection Metadata { get; init; }
    public required DescriptorAuthoringMemoryProjection Memory { get; init; }
    public IReadOnlyList<DescriptorRef> VisibleDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<DescriptorKind> SupportedDescriptorKinds { get; init; } = Array.Empty<DescriptorKind>();
    public CanonicalHash? PromptInputHash { get; init; }

    public DescriptorAuthoringPromptInput Snapshot() => this with
    {
        Metadata = Metadata.Snapshot(),
        Memory = Memory.Snapshot(),
        VisibleDescriptorRefs = VisibleDescriptorRefs.ToArray(),
        SupportedDescriptorKinds = SupportedDescriptorKinds.ToArray()
    };
}
