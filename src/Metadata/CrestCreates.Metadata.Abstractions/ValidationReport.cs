using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions.Registry;

namespace CrestCreates.Metadata.Abstractions;

public sealed record ValidationReport(
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.Severity == SeverityLevel.Error);
    public bool HasWarnings => Issues.Any(i => i.Severity == SeverityLevel.Warning);
    public static ValidationReport Empty => new(Array.Empty<ValidationIssue>());
    public static ValidationReport FromIssues(params ValidationIssue[] issues) => new(issues);
}
