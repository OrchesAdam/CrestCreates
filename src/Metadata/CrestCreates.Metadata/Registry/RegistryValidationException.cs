using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;

namespace CrestCreates.Metadata.Registry;

public sealed class RegistryValidationException : Exception
{
    public IReadOnlyList<ValidationIssue> Issues { get; }

    public RegistryValidationException(IReadOnlyList<ValidationIssue> issues)
        : base($"Registry validation failed with {issues.Count(i => i.Severity == ValidationSeverity.Error)} error(s):\n" +
               string.Join("\n", issues.Where(i => i.Severity == ValidationSeverity.Error).Select(i => $"  - {i.Message}")))
    {
        Issues = issues;
    }
}
