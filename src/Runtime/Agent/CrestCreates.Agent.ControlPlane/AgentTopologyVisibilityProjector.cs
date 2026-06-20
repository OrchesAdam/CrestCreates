using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Builds topology, relationship, and summary snapshots from a visible
/// descriptor universe only. Denied descriptors never enter the graph,
/// so their nodes, incident edges, and diagnostics cannot appear in
/// downstream results.
/// </summary>
internal sealed class AgentTopologyVisibilityProjector
{
    /// <summary>
    /// Builds a topology snapshot from the visible descriptors in the universe.
    /// </summary>
    public DescriptorTopologySnapshot BuildVisible(
        AgentVisibleDescriptorUniverse universe,
        IDescriptorTopologyBuilder builder) => builder.Build(universe.VisibleDescriptors);
}
