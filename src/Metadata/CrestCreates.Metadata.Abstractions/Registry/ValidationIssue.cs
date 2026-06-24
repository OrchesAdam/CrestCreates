using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.Registry;

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message);
