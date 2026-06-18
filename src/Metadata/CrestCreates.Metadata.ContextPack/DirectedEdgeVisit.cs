using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.ContextPack;

internal readonly record struct DirectedEdgeVisit(
    DescriptorEdge Edge,
    DescriptorRef Source,
    DescriptorRef Target,
    DirectedEdgeVisitDirection Direction)
{
    internal static DirectedEdgeVisit FromOutgoing(DescriptorEdge edge)
        => new(edge, edge.From, edge.To, DirectedEdgeVisitDirection.Outgoing);

    internal static DirectedEdgeVisit FromIncoming(DescriptorEdge edge)
        => new(edge, edge.To, edge.From, DirectedEdgeVisitDirection.Incoming);
}
