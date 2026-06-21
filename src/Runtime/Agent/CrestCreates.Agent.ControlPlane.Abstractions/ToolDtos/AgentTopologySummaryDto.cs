using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Abstractions;

/// <summary>
/// Adapter-safe summary of topology snapshot.
/// Replaces DescriptorTopologySnapshot internals.
/// </summary>
public sealed record AgentTopologySummaryDto
{
    public required int TotalNodeCount { get; init; }
    public required int TotalEdgeCount { get; init; }
    public required IReadOnlyDictionary<DescriptorKind, int> NodeCountsByKind { get; init; }
    public required IReadOnlyDictionary<RelationshipKind, int> EdgeCountsByKind { get; init; }
}
