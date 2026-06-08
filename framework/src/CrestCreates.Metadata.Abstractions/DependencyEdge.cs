namespace CrestCreates.Metadata.Abstractions;

public sealed class DependencyEdge
{
    public string SourceId { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public DescriptorDependencyKind Kind { get; init; }
}