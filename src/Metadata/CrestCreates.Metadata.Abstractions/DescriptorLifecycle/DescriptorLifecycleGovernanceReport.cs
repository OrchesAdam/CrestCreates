namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleGovernanceReport
{
    public required IReadOnlyList<DescriptorLifecycleDecision> Decisions { get; init; }
    public required DescriptorLifecycleDecisionKind MaxDecision { get; init; }
    public required IReadOnlyList<DescriptorLifecycleFinding> PackageFindings { get; init; }

    public bool IsAllowed => MaxDecision == DescriptorLifecycleDecisionKind.Allowed;
    public bool RequiresReview => MaxDecision == DescriptorLifecycleDecisionKind.ReviewRequired;
    public bool IsBlocked => MaxDecision == DescriptorLifecycleDecisionKind.Blocked;
}
