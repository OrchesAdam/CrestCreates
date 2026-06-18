using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DraftComparisonResult
{
    public required DraftAbstractions.DescriptorDraft Draft { get; init; }
    public required IDescriptor? CurrentActiveDescriptor { get; init; }
    public required IReadOnlyList<DraftDifference> Differences { get; init; }
}
