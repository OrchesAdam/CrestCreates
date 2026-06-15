using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions;

public sealed record DescriptorPackageBuildRequest
{
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public string? Name { get; init; }
    public string? CreatedBy { get; init; }
    public string? Source { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public required IReadOnlyList<IDescriptor> Descriptors { get; init; }

    public DescriptorTopologySnapshot? TopologySnapshot { get; init; }
    public DescriptorImpactAnalysisReport? ImpactReport { get; init; }
    public DescriptorCompatibilityReport? CompatibilityReport { get; init; }
    public DescriptorLifecycleGovernanceReport? GovernanceReport { get; init; }

    public DescriptorPackageBuildOptions Options { get; init; } = new();
}
