namespace CrestCreates.Metadata.Abstractions;

public sealed record ValidationReport(
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);
    public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);
    public static ValidationReport Empty => new(Array.Empty<ValidationIssue>());
    public static ValidationReport FromIssues(params ValidationIssue[] issues) => new(issues);
}
