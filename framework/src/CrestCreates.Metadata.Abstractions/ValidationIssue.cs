namespace CrestCreates.Metadata.Abstractions;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ValidationIssue(
    ValidationSeverity Severity,
    string Message);
