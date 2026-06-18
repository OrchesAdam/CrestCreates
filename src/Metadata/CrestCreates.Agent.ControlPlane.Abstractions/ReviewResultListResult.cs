using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record ReviewResultListResult
{
    public required IReadOnlyList<DraftAbstractions.DescriptorDraftReviewResult> Results { get; init; }
}
