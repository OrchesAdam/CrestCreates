using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewReportSectionDto
{
    public required DescriptorReviewReportSectionKind Kind { get; init; }
    public required string SectionId { get; init; }
    public required string Title { get; init; }
    public required int Order { get; init; }
    public required bool IsEmpty { get; init; }
    public required SeverityLevel OverallSeverity { get; init; }
    public required IReadOnlyList<DescriptorReviewReportItemDto> Items { get; init; }
}
