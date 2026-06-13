using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorImpact;

public sealed record DescriptorImpactPath
{
    public required DescriptorRef SourceChange { get; init; }
    public required DescriptorRef Affected { get; init; }
    public required IReadOnlyList<DescriptorImpactPathSegment> Segments { get; init; }
}
