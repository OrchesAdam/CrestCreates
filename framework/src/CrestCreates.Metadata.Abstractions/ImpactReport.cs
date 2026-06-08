namespace CrestCreates.Metadata.Abstractions;

public sealed class ImpactReport
{
    public string DescriptorId { get; init; } = string.Empty;
    public string DescriptorName { get; init; } = string.Empty;
    public int FromVersion { get; init; }
    public int ToVersion { get; init; }
    public IReadOnlyList<DependencyEdge> AffectedDependents { get; init; } = Array.Empty<DependencyEdge>();
    public bool IsBreaking => AffectedDependents.Any(e => e.Kind == DescriptorDependencyKind.Uses
                                                       || e.Kind == DescriptorDependencyKind.Triggers
                                                       || e.Kind == DescriptorDependencyKind.Consumes);
}