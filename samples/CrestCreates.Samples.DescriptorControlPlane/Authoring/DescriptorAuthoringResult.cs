namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

public sealed record DescriptorAuthoringResult
{
    public required DescriptorAuthoringPlan Plan { get; init; }
    public required DescriptorDraftSet DraftSet { get; init; }
    public IReadOnlyList<string> Diagnostics { get; init; } = Array.Empty<string>();
}
