namespace CrestCreates.Samples.DescriptorControlPlane.Authoring;

public sealed record DescriptorDraftSet
{
    public required string DraftSetId { get; init; }
    public required IReadOnlyList<CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft> Drafts { get; init; }
}
