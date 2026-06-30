namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

public sealed record DescriptorAuthoringPlan
{
    public required string PlanId { get; init; }
    public required string IntentText { get; init; }
    public required IReadOnlyList<string> PlannedDescriptorIds { get; init; }
}
