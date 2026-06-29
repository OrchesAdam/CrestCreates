using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Registry;

public sealed class UniquePayloadTypeValidator : IRegistryValidator<GeneratedEventDescriptor>
{
    public int Order => 300;

    public ValidationReport Validate(IReadOnlyList<GeneratedEventDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        var violations = descriptors
            .GroupBy(d => d.PayloadType)
            .Where(g => g.Count(d => d.State == DescriptorState.Active) > 1)
            .ToList();

        if (violations.Count > 0)
            issues.Add(new ValidationIssue(SeverityLevel.Error,
                $"PayloadType uniqueness violation: {violations.Count} CLR types map to multiple Active events."));

        return new ValidationReport(issues);
    }
}
