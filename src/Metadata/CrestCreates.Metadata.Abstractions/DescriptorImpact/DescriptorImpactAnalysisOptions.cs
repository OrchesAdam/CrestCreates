namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorImpactAnalysisOptions
{
    public bool IncludeWeakRelationships { get; init; } = true;
    public bool IncludeAdvisoryRelationships { get; init; } = true;
    public int? MaxDepth { get; init; }
}
