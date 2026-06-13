namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public sealed record DescriptorCompatibilityAnalysisOptions
{
    public bool TreatRemovedWithoutConsumersAsRisky { get; init; } = true;
    public bool TreatUnknownDescriptorKindAsUnsupported { get; init; } = true;
    public bool TreatImpactWarningsAsUnsupported { get; init; } = false;
    public bool IncludeCompatibleFindings { get; init; } = true;
}
