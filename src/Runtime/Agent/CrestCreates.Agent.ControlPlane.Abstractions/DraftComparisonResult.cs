namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DraftComparisonResult
{
    public required AgentDescriptorDraftDto Draft { get; init; }
    public DescriptorSummaryDto? CurrentActiveDescriptor { get; init; }
    public required IReadOnlyList<DraftDifference> Differences { get; init; }
}
