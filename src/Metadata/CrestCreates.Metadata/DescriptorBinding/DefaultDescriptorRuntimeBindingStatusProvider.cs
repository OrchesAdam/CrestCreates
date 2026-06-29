using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.DescriptorBinding;

public sealed class DefaultDescriptorRuntimeBindingStatusProvider
    : IDescriptorRuntimeBindingStatusProvider
{
    private readonly IReadOnlyList<IDescriptorBindingStatusContributor> _contributors;

    public DefaultDescriptorRuntimeBindingStatusProvider(
        IEnumerable<IDescriptorBindingStatusContributor> contributors)
    {
        _contributors = contributors.OrderBy(c => c.Order).ToList();
    }

    public DescriptorBindingReport GetStatus(IDescriptor descriptor)
    {
        var contributor = _contributors.FirstOrDefault(c => c.SupportedKind == descriptor.Kind);
        return contributor?.Evaluate(descriptor)
            ?? new DescriptorBindingReport
            {
                DescriptorId = descriptor.FullId,
                DescriptorKind = descriptor.Kind,
                Status = DescriptorBindingStatus.PartiallyBound,
                Issues = new[]
                {
                    new DescriptorBindingIssue(
                        Severity: SeverityLevel.Warning,
                        Code: new DiagnosticCode("WARN_NO_BINDING_CONTRIBUTOR"),
                        Message: $"No binding status contributor registered for {descriptor.Kind}.",
                        DescriptorId: descriptor.FullId,
                        DescriptorKind: descriptor.Kind)
                }
            };
    }

    public RuntimeBindingReport GetAllStatuses()
    {
        var reports = new List<DescriptorBindingReport>();
        foreach (var contributor in _contributors)
        {
            foreach (var descriptor in contributor.GetDescriptors())
            {
                reports.Add(contributor.Evaluate(descriptor));
            }
        }
        return new RuntimeBindingReport { Descriptors = reports };
    }
}
