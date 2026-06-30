using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

public sealed record DescriptorDraftSet : ISnapshotable<DescriptorDraftSet>
{
    public required string DraftSetId { get; init; }
    public required IReadOnlyList<CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft> Drafts { get; init; }

    public DescriptorDraftSet Snapshot() => this with
    {
        Drafts = Drafts.Select(d => d.Snapshot()).ToArray()
    };
}
