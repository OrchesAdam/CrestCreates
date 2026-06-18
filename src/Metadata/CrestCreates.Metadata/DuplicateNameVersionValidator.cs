using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DuplicateNameVersionValidator : IRegistryValidator<GeneratedEventDescriptor>
{
    public int Order => 200;

    public ValidationReport Validate(IReadOnlyList<GeneratedEventDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        var duplicates = descriptors
            .GroupBy(d => (d.Name, d.Version))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.Name} v{g.Key.Version}")
            .ToList();

        if (duplicates.Count > 0)
            issues.Add(new ValidationIssue(ValidationSeverity.Error,
                $"Duplicate (name, version) pairs: {string.Join(", ", duplicates)}."));

        return new ValidationReport(issues);
    }
}
