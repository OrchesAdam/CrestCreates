using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.Registry;

public sealed record ValidationIssue(
    SeverityLevel Severity,
    string Message)
{
    public DiagnosticCode? Code { get; init; }
}
