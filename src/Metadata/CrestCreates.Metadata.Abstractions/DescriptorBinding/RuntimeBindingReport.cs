namespace CrestCreates.Metadata.Abstractions.DescriptorBinding;

public sealed class RuntimeBindingReport
{
    public IReadOnlyList<DescriptorBindingReport> Descriptors { get; init; }
        = Array.Empty<DescriptorBindingReport>();

    public bool HasErrors => Descriptors.Any(d =>
        d.Status is DescriptorBindingStatus.Invalid
                   or DescriptorBindingStatus.Unbound
                   or DescriptorBindingStatus.Unsupported);

    public IReadOnlyList<DescriptorBindingReport> NotReady =>
        Descriptors.Where(d => !d.IsRuntimeReady).ToArray();
}
