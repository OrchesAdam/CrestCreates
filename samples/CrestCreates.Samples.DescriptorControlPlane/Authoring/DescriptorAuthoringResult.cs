using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

public sealed record DescriptorAuthoringResult : ISnapshotable<DescriptorAuthoringResult>
{
    public required DescriptorAuthoringPlan Plan { get; init; }
    public required DescriptorDraftSet DraftSet { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();

    public DescriptorAuthoringResult Snapshot() => this with
    {
        DraftSet = DraftSet.Snapshot(),
        Diagnostics = Diagnostics.ToArray()
    };
}
