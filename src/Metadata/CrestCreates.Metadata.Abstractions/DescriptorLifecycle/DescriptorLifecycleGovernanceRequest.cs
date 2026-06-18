using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions.DescriptorLifecycle;

public sealed record DescriptorLifecycleGovernanceRequest
{
    public required IReadOnlyList<DescriptorLifecycleTransition> Transitions { get; init; }
    public required ValidationReport ValidationReport { get; init; }
    public required RuntimeBindingReport BindingReport { get; init; }
    public required DescriptorTopologyDiagnostics TopologyDiagnostics { get; init; }
    public required DescriptorImpactAnalysisReport ImpactReport { get; init; }
    public required DescriptorCompatibilityReport CompatibilityReport { get; init; }
    public DescriptorLifecycleGovernanceOptions Options { get; init; } = new();
}
