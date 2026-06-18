using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

public sealed record TopologySummaryResult
{
    public required int TotalNodeCount { get; init; }
    public required int TotalEdgeCount { get; init; }
    public required IReadOnlyDictionary<DescriptorKind, int> NodeCountsByKind { get; init; }
    public required IReadOnlyDictionary<RelationshipKind, int> EdgeCountsByKind { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> TopologyDiagnostics { get; init; }
}
