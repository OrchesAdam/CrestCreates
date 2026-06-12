namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Independent from ValidationIssue. Binding status is a different domain
/// from structural validation — different fields, different consumers.
/// Reuses ValidationSeverity to avoid creating a parallel severity enum.
/// </summary>
public sealed record DescriptorBindingIssue(
    ValidationSeverity Severity,
    string Code,          // Stable error code for tests (e.g., "REF_MISSING_SCHEMA")
    string Message,       // Human-readable description
    string? DescriptorId = null,
    DescriptorKind? DescriptorKind = null,
    string? Path = null); // Property path (e.g., "InputSchema.Id")
