namespace CrestCreates.DescriptorDraft.Abstractions;

public sealed record DescriptorDraftValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<DescriptorDraftDiagnostic> Diagnostics { get; init; }

    public static DescriptorDraftValidationResult Success()
        => new() { IsValid = true, Diagnostics = Array.Empty<DescriptorDraftDiagnostic>() };

    public static DescriptorDraftValidationResult Failure(params DescriptorDraftDiagnostic[] diagnostics)
        => new() { IsValid = false, Diagnostics = diagnostics };
}
