using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Abstractions.DescriptorTopology;

public sealed class DescriptorTopologySnapshot
{
    private readonly Dictionary<DescriptorRef, DescriptorNode> _nodes;
    private readonly List<DescriptorEdge> _edges;
    private readonly Dictionary<DescriptorIdentity, List<(DescriptorRef Consumer, DescriptorEdge Edge)>> _consumersByIdentity;
    private readonly Dictionary<(DescriptorIdentity Id, int Version), List<(DescriptorRef Consumer, DescriptorEdge Edge)>> _consumersByExactVersion;
    private readonly Dictionary<DescriptorIdentity, List<(DescriptorRef Consumer, DescriptorEdge Edge)>> _consumersByUnpinnedVersion;

    public DateTimeOffset BuiltAt { get; }
    public int NodeCount => _nodes.Count;
    public int EdgeCount => _edges.Count;

    public IReadOnlyDictionary<DescriptorRef, DescriptorNode> Nodes { get; }
    public IReadOnlyList<DescriptorEdge> Edges { get; }
    public DescriptorTopologyDiagnostics Diagnostics { get; }

    internal DescriptorTopologySnapshot(
        Dictionary<DescriptorRef, DescriptorNode> nodes,
        List<DescriptorEdge> edges,
        DescriptorTopologyDiagnostics diagnostics,
        Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>> consumersByIdentity,
        Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>> consumersByExactVersion,
        Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>> consumersByUnpinnedVersion,
        DateTimeOffset builtAt)
    {
        _nodes = nodes;
        _edges = edges;
        Diagnostics = diagnostics;
        _consumersByIdentity = consumersByIdentity;
        _consumersByExactVersion = consumersByExactVersion;
        _consumersByUnpinnedVersion = consumersByUnpinnedVersion;
        BuiltAt = builtAt;

        Nodes = nodes.ToImmutableDictionary();
        Edges = edges.ToImmutableList();
    }

    /// <summary>
    /// Version-aware node resolution. Exact match first, then (Namespace, Id) fallback
    /// when the ref has Version=null (unpinned reference).
    /// </summary>
    private DescriptorNode? TryResolveRef(DescriptorRef r)
    {
        if (_nodes.TryGetValue(r, out var node))
            return node;

        if (r.Version == null)
        {
            return _nodes.Values.FirstOrDefault(n =>
                n.Ref.Namespace == r.Namespace && n.Ref.Id == r.Id);
        }

        return null;
    }

    /// <summary>
    /// Check if an edge ref (edgeRef, may be unpinned) matches a resolved node ref (nodeRef, has version).
    /// True if exact match, or edgeRef.Version=null and same (Namespace, Id).
    /// Made internal so the adapter can use it via InternalsVisibleTo.
    /// </summary>
    internal static bool RefsMatch(DescriptorRef edgeRef, DescriptorRef nodeRef)
    {
        return edgeRef.Equals(nodeRef) ||
               (edgeRef.Version == null &&
                edgeRef.Namespace == nodeRef.Namespace &&
                edgeRef.Id == nodeRef.Id);
    }

    public bool Contains(DescriptorRef r) => TryResolveRef(r) is not null;

    public DescriptorNode? FindNode(DescriptorRef r) => TryResolveRef(r);

    public IReadOnlyList<DescriptorNode> GetDirectDependencies(DescriptorRef of)
    {
        var node = TryResolveRef(of);
        if (node is null)
            return Array.Empty<DescriptorNode>();

        return node.OutgoingEdgeIndices
            .Select(i => _edges[i])
            .Select(e => TryResolveRef(e.To))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList().AsReadOnly();
    }

    public IReadOnlyList<DescriptorNode> GetDirectDependents(DescriptorRef of)
    {
        var node = TryResolveRef(of);
        if (node is null)
            return Array.Empty<DescriptorNode>();

        return node.IncomingEdgeIndices
            .Select(i => _edges[i])
            .Select(e => TryResolveRef(e.From))
            .Where(n => n is not null)
            .Select(n => n!)
            .ToList().AsReadOnly();
    }

    public IReadOnlySet<DescriptorNode> GetTransitiveDependencies(
        DescriptorRef of, bool includeWeak = false)
    {
        return BfsTraverse(of, followOutgoing: true, includeWeak);
    }

    public IReadOnlySet<DescriptorNode> GetTransitiveDependents(
        DescriptorRef of, bool includeWeak = false)
    {
        return BfsTraverse(of, followOutgoing: false, includeWeak);
    }

    private HashSet<DescriptorNode> BfsTraverse(
        DescriptorRef start, bool followOutgoing, bool includeWeak)
    {
        var visited = new HashSet<DescriptorRef>();
        var result = new HashSet<DescriptorNode>();
        var queue = new Queue<DescriptorRef>();

        var startNode = TryResolveRef(start);
        if (startNode is null)
            return result;

        var startExactRef = startNode.Ref;
        queue.Enqueue(startExactRef);
        visited.Add(startExactRef);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!_nodes.TryGetValue(current, out var currentNode))
                continue;

            if (!current.Equals(startExactRef))
                result.Add(currentNode);

            var edgeIndices = followOutgoing
                ? currentNode.OutgoingEdgeIndices
                : currentNode.IncomingEdgeIndices;

            foreach (var idx in edgeIndices)
            {
                var edge = _edges[idx];

                if (!includeWeak && edge.Strength == RelationshipStrength.Weak)
                    continue;

                var edgeRef = followOutgoing ? edge.To : edge.From;
                var resolved = TryResolveRef(edgeRef);
                if (resolved is null)
                    continue;

                var resolvedRef = resolved.Ref;
                if (visited.Add(resolvedRef))
                    queue.Enqueue(resolvedRef);
            }
        }

        return result;
    }

    public IReadOnlyList<DescriptorNode> GetConsumers(
        string ns, string id, int? version = null)
    {
        var identity = new DescriptorIdentity(ns, id);

        List<(DescriptorRef Consumer, DescriptorEdge Edge)> entries;

        if (version == null)
        {
            if (!_consumersByIdentity.TryGetValue(identity, out var all))
                return Array.Empty<DescriptorNode>();
            entries = all;
        }
        else
        {
            entries = new();
            if (_consumersByExactVersion.TryGetValue((identity, version.Value), out var exact))
                entries.AddRange(exact);
            if (_consumersByUnpinnedVersion.TryGetValue(identity, out var unpinned))
                entries.AddRange(unpinned);
        }

        return entries
            .Select(e => TryResolveRef(e.Consumer))
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct()
            .ToList().AsReadOnly();
    }
}
