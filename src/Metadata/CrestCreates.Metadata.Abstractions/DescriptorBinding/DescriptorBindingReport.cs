namespace CrestCreates.Metadata.Abstractions.DescriptorBinding;

public sealed class DescriptorBindingReport
{
    public string DescriptorId { get; init; } = default!;
    public DescriptorKind DescriptorKind { get; init; }
    public DescriptorBindingStatus Status { get; init; }
    public IReadOnlyList<DescriptorBindingIssue> Issues { get; init; } = Array.Empty<DescriptorBindingIssue>();

    public bool IsRuntimeReady => Status == DescriptorBindingStatus.RuntimeReady;
}
