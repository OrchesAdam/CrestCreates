using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorDraftSet : ISnapshotable<DescriptorDraftSet>
{
    public required string DraftSetId { get; init; }
    public IReadOnlyList<CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft> Drafts { get; init; } = Array.Empty<CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft>();

    public DescriptorDraftSet Snapshot() => this with
    {
        Drafts = Drafts.Select(d => d.Snapshot()).ToArray()
    };
}
