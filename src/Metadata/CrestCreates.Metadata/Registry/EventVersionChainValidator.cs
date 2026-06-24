using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Registry;

public sealed class EventVersionChainValidator : IRegistryValidator<GeneratedEventDescriptor>
{
    public int Order => 100;

    public ValidationReport Validate(IReadOnlyList<GeneratedEventDescriptor> descriptors)
    {
        var issues = new List<ValidationIssue>();

        foreach (var group in descriptors.GroupBy(d => d.Name))
        {
            var active = group.Where(d => d.State == DescriptorState.Active).ToList();

            if (active.Count == 0)
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"Event '{group.Key}' has no Active version."));
            else if (active.Count > 1)
                issues.Add(new ValidationIssue(ValidationSeverity.Error,
                    $"Event '{group.Key}' has {active.Count} Active versions: " +
                    $"{string.Join(", ", active.Select(a => $"v{a.Version}"))}."));
            else
            {
                var highest = group.MaxBy(d => d.Version)!;
                if (active[0].Version != highest.Version)
                    issues.Add(new ValidationIssue(ValidationSeverity.Error,
                        $"Event '{group.Key}': highest version (v{highest.Version}) is {highest.State}, " +
                        $"but v{active[0].Version} is Active."));
            }
        }

        return new ValidationReport(issues);
    }
}
