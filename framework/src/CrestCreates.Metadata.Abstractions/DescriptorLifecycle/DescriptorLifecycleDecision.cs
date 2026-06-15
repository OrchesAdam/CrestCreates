namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleDecision
{
    public required DescriptorLifecycleTransition Transition { get; init; }
    public required DescriptorLifecycleDecisionKind Decision { get; init; }
    public required IReadOnlyList<DescriptorLifecycleFinding> Findings { get; init; }
}
