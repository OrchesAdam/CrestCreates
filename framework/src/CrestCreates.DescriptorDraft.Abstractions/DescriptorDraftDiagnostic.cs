using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DescriptorDraft.Abstractions;

public enum DescriptorDraftDiagnosticSeverity
{
    Info,
    Warning,
    Error,
    Blocker
}

public sealed record DescriptorDraftDiagnostic
{
    public required string Code { get; init; }
    public required DescriptorDraftDiagnosticSeverity Severity { get; init; }
    public required string Message { get; init; }
    public DescriptorKind? DescriptorKind { get; init; }
    public string? DescriptorId { get; init; }
    public string? DraftId { get; init; }
    public string? Path { get; init; }
    public string? RelatedDiagnosticCode { get; init; }
}
