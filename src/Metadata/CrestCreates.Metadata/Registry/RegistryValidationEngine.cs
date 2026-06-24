using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Registry;

public sealed class RegistryValidationEngine<TDescriptor> : IRegistryValidationEngine<TDescriptor>
    where TDescriptor : IDescriptor
{
    private readonly IReadOnlyList<IRegistryValidator<TDescriptor>> _validators;

    public RegistryValidationEngine(IEnumerable<IRegistryValidator<TDescriptor>> validators)
    {
        _validators = validators.OrderBy(v => v.Order).ToList();
    }

    public ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors)
    {
        var allIssues = new List<ValidationIssue>();
        foreach (var validator in _validators)
        {
            var report = validator.Validate(descriptors);
            allIssues.AddRange(report.Issues);
        }
        return new ValidationReport(allIssues);
    }
}
