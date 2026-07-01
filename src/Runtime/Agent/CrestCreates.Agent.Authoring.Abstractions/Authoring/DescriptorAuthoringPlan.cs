using CrestCreates.Metadata.Abstractions;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Agent.Authoring.Abstractions.Authoring;

public sealed record DescriptorAuthoringPlan : ISnapshotable<DescriptorAuthoringPlan>
{
    public required string PlanId { get; init; }
    public required string IntentText { get; init; }
    public IReadOnlyList<DescriptorRef> PlannedDescriptorRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();

    public DescriptorAuthoringPlan Snapshot() => this with
    {
        PlannedDescriptorRefs = PlannedDescriptorRefs.ToArray(),
        Assumptions = Assumptions.ToArray()
    };
}
