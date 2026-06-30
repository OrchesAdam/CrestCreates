using CrestCreates.Metadata.Abstractions.DescriptorCompatibility;
using CrestCreates.Metadata.Abstractions.DescriptorImpact;
using CrestCreates.Metadata.Abstractions.DescriptorLifecycle;
using CrestCreates.Metadata.Abstractions.Evidence;
using CrestCreates.Snapshot.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorPackage;

public sealed class DescriptorPackageEvidence : ISnapshotable<DescriptorPackageEvidence>
{
    // Topology
    public int TopologyNodeCount { get; init; }
    public int TopologyEdgeCount { get; init; }
    public IReadOnlyList<EvidenceFindingCount> TopologyDiagnosticCounts { get; init; }
        = Array.Empty<EvidenceFindingCount>();
    public bool HasTopologyErrors { get; init; }

    // Impact
    public DescriptorImpactSeverity MaxImpactSeverity { get; init; }
    public int AffectedDescriptorCount { get; init; }
    public int ImpactPathCount { get; init; }
    public IReadOnlyList<EvidenceFindingCount> ImpactDiagnosticCounts { get; init; }
        = Array.Empty<EvidenceFindingCount>();

    // Compatibility
    public DescriptorCompatibilityLevel MaxCompatibilityLevel { get; init; }
    public int BreakingFindingCount { get; init; }
    public int SecuritySensitiveFindingCount { get; init; }
    public int UnsupportedFindingCount { get; init; }

    // Lifecycle
    public DescriptorLifecycleDecisionKind MaxLifecycleDecision { get; init; }
    public bool RequiresReview { get; init; }
    public bool IsBlocked { get; init; }
    public int PackageFindingCount { get; init; }

    // Unified
    public IReadOnlyList<EvidenceFinding> NormalizedFindings { get; init; }
        = Array.Empty<EvidenceFinding>();

    public DescriptorPackageEvidence Snapshot() => new()
    {
        TopologyNodeCount = TopologyNodeCount,
        TopologyEdgeCount = TopologyEdgeCount,
        TopologyDiagnosticCounts = TopologyDiagnosticCounts.Select(item => item.Snapshot()).ToArray(),
        HasTopologyErrors = HasTopologyErrors,
        MaxImpactSeverity = MaxImpactSeverity,
        AffectedDescriptorCount = AffectedDescriptorCount,
        ImpactPathCount = ImpactPathCount,
        ImpactDiagnosticCounts = ImpactDiagnosticCounts.Select(item => item.Snapshot()).ToArray(),
        MaxCompatibilityLevel = MaxCompatibilityLevel,
        BreakingFindingCount = BreakingFindingCount,
        SecuritySensitiveFindingCount = SecuritySensitiveFindingCount,
        UnsupportedFindingCount = UnsupportedFindingCount,
        MaxLifecycleDecision = MaxLifecycleDecision,
        RequiresReview = RequiresReview,
        IsBlocked = IsBlocked,
        PackageFindingCount = PackageFindingCount,
        NormalizedFindings = NormalizedFindings.Select(item => item.Snapshot()).ToArray()
    };
}
