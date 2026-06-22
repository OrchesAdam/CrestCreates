using DraftAbstractions = CrestCreates.DescriptorDraft.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewReportBuildRequest
{
    public required DraftAbstractions.DescriptorDraftReviewResult ReviewResult { get; init; }
    public required DraftAbstractions.DescriptorDraft Draft { get; init; }
    public required bool VisibilityApplied { get; init; }
}
