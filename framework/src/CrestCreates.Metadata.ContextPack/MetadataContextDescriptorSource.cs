using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;
using CrestCreates.Metadata.ContextPack.Abstractions;

namespace CrestCreates.Metadata.ContextPack;

internal sealed class MetadataContextDescriptorSource
{
    private readonly DescriptorTopologySnapshot _topology;
    private readonly Dictionary<DescriptorRef, IDescriptor> _versionedIndex;
    private readonly Dictionary<DescriptorIdentity, List<IDescriptor>> _unpinnedIndex;

    public MetadataContextDescriptorSource(
        DescriptorTopologySnapshot topology,
        IReadOnlyList<IDescriptor> descriptors)
    {
        _topology = topology;
        _versionedIndex = new Dictionary<DescriptorRef, IDescriptor>();
        _unpinnedIndex = new Dictionary<DescriptorIdentity, List<IDescriptor>>();

        foreach (var d in descriptors)
        {
            var version = d is IVersionedDescriptor vd ? vd.Version : (int?)null;
            var exactKey = new DescriptorRef(d.Namespace, d.Id, version);
            _versionedIndex[exactKey] = d;

            var unpinnedKey = new DescriptorIdentity(d.Namespace, d.Id);
            if (!_unpinnedIndex.TryGetValue(unpinnedKey, out var list))
            {
                list = new List<IDescriptor>();
                _unpinnedIndex[unpinnedKey] = list;
            }
            list.Add(d);
        }
    }

    public ResolvedDescriptor Resolve(DescriptorRef reference)
    {
        var topologyNode = _topology.FindNode(reference);
        var (descriptor, isAmbiguous) = ResolveDescriptorWithAmbiguity(reference);

        // Canonical ref: prefer descriptor's versioned identity, fall back to topology node ref, then requested ref
        var canonicalRef = reference;
        if (descriptor is not null)
        {
            var version = descriptor is IVersionedDescriptor vd ? vd.Version : (int?)null;
            canonicalRef = new DescriptorRef(descriptor.Namespace, descriptor.Id, version);
        }
        else if (topologyNode is not null)
        {
            canonicalRef = topologyNode.Ref;
        }

        return new ResolvedDescriptor(reference, canonicalRef, topologyNode, descriptor, isAmbiguous);
    }

    public IEnumerable<DirectedEdgeVisit> GetDirectedEdges(
        DescriptorRef nodeRef,
        ScenarioTraversalDirection direction)
    {
        var node = _topology.FindNode(nodeRef);
        if (node is null)
            yield break;

        switch (direction)
        {
            case ScenarioTraversalDirection.Dependencies:
                foreach (var edgeIdx in node.OutgoingEdgeIndices)
                    yield return DirectedEdgeVisit.FromOutgoing(_topology.Edges[edgeIdx]);
                break;

            case ScenarioTraversalDirection.Dependents:
                foreach (var edgeIdx in node.IncomingEdgeIndices)
                    yield return DirectedEdgeVisit.FromIncoming(_topology.Edges[edgeIdx]);
                break;

            case ScenarioTraversalDirection.Both:
                foreach (var edgeIdx in node.OutgoingEdgeIndices)
                    yield return DirectedEdgeVisit.FromOutgoing(_topology.Edges[edgeIdx]);
                foreach (var edgeIdx in node.IncomingEdgeIndices)
                    yield return DirectedEdgeVisit.FromIncoming(_topology.Edges[edgeIdx]);
                break;
        }
    }

    private (IDescriptor? Descriptor, bool IsAmbiguous) ResolveDescriptorWithAmbiguity(DescriptorRef reference)
    {
        // Version-pinned lookup: exact match
        if (reference.Version.HasValue && _versionedIndex.TryGetValue(reference, out var exact))
            return (exact, false);

        // Unpinned lookup
        var identity = new DescriptorIdentity(reference.Namespace, reference.Id);
        if (_unpinnedIndex.TryGetValue(identity, out var candidates))
        {
            if (candidates.Count == 1)
                return (candidates[0], false);

            // Multiple versions — ambiguous
            return (null, true);
        }

        // No match
        return (null, false);
    }
}
