using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record AffectedDescriptor
{
    public required DescriptorRef Ref { get; init; }
    public required DescriptorKind Kind { get; init; }
    public required string Name { get; init; }
    public required DescriptorImpactSeverity Severity { get; init; }
    public required IReadOnlyList<DescriptorImpactRuntimeArea> RuntimeAreas { get; init; }
    public required IReadOnlyList<DescriptorImpactPath> Paths { get; init; }
    public string? Reason { get; init; }
    public string? SuggestedAction { get; init; }
}
