using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorBinding;

/// <summary>
/// Independent from ValidationIssue. Binding status is a different domain
/// from structural validation — different fields, different consumers.
/// Uses SeverityLevel from Core.Abstractions.Identity as the canonical severity type.
/// </summary>
public sealed record DescriptorBindingIssue(
    SeverityLevel Severity,
    DiagnosticCode Code,  // Stable error code for tests (e.g., "REF_MISSING_SCHEMA")
    string Message,       // Human-readable description
    string? DescriptorId = null,
    DescriptorKind? DescriptorKind = null,
    string? Path = null); // Property path (e.g., "InputSchema.Id")
