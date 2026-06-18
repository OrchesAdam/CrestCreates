using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;

namespace CrestCreates.Metadata.Abstractions.DescriptorCompatibility;

public sealed record DescriptorCompatibilityFinding
{
    public required DescriptorRef Subject { get; init; }
    public required DescriptorChangeKind ChangeKind { get; init; }
    public required DescriptorCompatibilityLevel Level { get; init; }
    public required DescriptorCompatibilityFindingKind Kind { get; init; }
    public required string RuleId { get; init; }
    public required string Message { get; init; }
    public IReadOnlyList<DescriptorRef> AffectedRefs { get; init; } = Array.Empty<DescriptorRef>();
    public IReadOnlyList<DescriptorImpactPath> RelatedImpactPaths { get; init; } = Array.Empty<DescriptorImpactPath>();
    public string? Path { get; init; }
    public string? BeforeValue { get; init; }
    public string? AfterValue { get; init; }
    public string? SuggestedAction { get; init; }
}
