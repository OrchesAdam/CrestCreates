using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorDraftListResult
{
    public required IReadOnlyList<DraftAbstractions.DescriptorDraft> Drafts { get; init; }
    public required int TotalCount { get; init; }
}
