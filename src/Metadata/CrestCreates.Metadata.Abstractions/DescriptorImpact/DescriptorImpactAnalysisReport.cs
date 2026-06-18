namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorImpactAnalysisReport
{
    public required DescriptorChangeSet ChangeSet { get; init; }
    public required IReadOnlyList<AffectedDescriptor> AffectedDescriptors { get; init; }
    public required IReadOnlyList<DescriptorImpactPath> Paths { get; init; }
    public required DescriptorImpactSeverity MaxSeverity { get; init; }
    public required IReadOnlyList<DescriptorImpactDiagnostic> Diagnostics { get; init; }
}
