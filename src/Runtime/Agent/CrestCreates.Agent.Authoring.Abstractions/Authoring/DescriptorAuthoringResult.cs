using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorAuthoringResult : ISnapshotable<DescriptorAuthoringResult>
{
    public required DescriptorAuthoringStatus Status { get; init; }
    public required DescriptorAuthoringPlan Plan { get; init; }
    public required DescriptorDraftSet DraftSet { get; init; }
    public IReadOnlyList<DescriptorAuthoringDiagnostic> Diagnostics { get; init; } = Array.Empty<DescriptorAuthoringDiagnostic>();

    public DescriptorAuthoringResult Snapshot() => this with
    {
        Plan = Plan.Snapshot(),
        DraftSet = DraftSet.Snapshot(),
        Diagnostics = Diagnostics.Select(d => d.Snapshot()).ToArray()
    };
}
