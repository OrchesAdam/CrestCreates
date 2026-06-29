using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorDraftDiagnostic
{
    public required DiagnosticCode Code { get; init; }
    public required SeverityLevel Severity { get; init; }
    public required string Message { get; init; }
    public DescriptorKind? DescriptorKind { get; init; }
    public string? DescriptorId { get; init; }
    public string? DraftId { get; init; }
    public string? Path { get; init; }
    public string? RelatedDiagnosticCode { get; init; }
}
