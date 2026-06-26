namespace CrestCreates.DescriptorDraft.Abstractions.CanonicalHashing;

/// <summary>
/// Projection of DescriptorDraftDiagnostic for canonical hash computation.
/// Only includes fields that participate in source-binding hash.
/// </summary>
public sealed record ReviewDiagnosticProjection
{
    public required string Code { get; init; }
    public required string Severity { get; init; }
}
