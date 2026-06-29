using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record DescriptorReviewReportItemDto
{
    public required string ItemId { get; init; }
    public required string ReasonCode { get; init; }
    public required string MessageTemplateId { get; init; }
    public required string Message { get; init; }
    public required SeverityLevel Severity { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<string> RelatedDiagnosticIds { get; init; } = [];
    public IReadOnlyList<string> RelatedDescriptorIds { get; init; } = [];
}
