using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.DescriptorTopology;

internal sealed class DescriptorTopologyBuilder : IDescriptorTopologyBuilder
{
    private readonly IDescriptorRelationshipProvider _relationshipProvider;
    private readonly IDescriptorStableHashBuilder _hashBuilder;

    public DescriptorTopologyBuilder(
        IDescriptorRelationshipProvider relationshipProvider,
        IDescriptorStableHashBuilder hashBuilder)
    {
        _relationshipProvider = relationshipProvider;
        _hashBuilder = hashBuilder;
    }

    public DescriptorTopologySnapshot Build(IReadOnlyList<IDescriptor> descriptors)
    {
        var nodes = new Dictionary<DescriptorRef, DescriptorNode>();
        var edges = new List<DescriptorEdge>();

        // Phase 1: Create nodes (mutable edge indices)
        foreach (var descriptor in descriptors)
        {
            int? version = (descriptor as IVersionedDescriptor)?.Version;
            var nodeRef = new DescriptorRef(descriptor.Namespace, descriptor.Id, version);

            nodes[nodeRef] = new DescriptorNode
            {
                Ref = nodeRef,
                Kind = descriptor.Kind,
                Name = descriptor.Name,
                State = descriptor.State,
                ContractHash = _hashBuilder.Build(descriptor).ContractHash.Value,
                SupersededById = descriptor.SupersededById,
                OutgoingEdgeIndices = new HashSet<int>(),
                IncomingEdgeIndices = new HashSet<int>()
            };
        }

        // Phase 2: Extract edges (mutate node edge indices)
        foreach (var descriptor in descriptors)
        {
            var relationships = _relationshipProvider.GetRelationships(descriptor);
            foreach (var rel in relationships)
            {
                var edge = new DescriptorEdge
                {
                    Index = edges.Count,
                    From = rel.From,
                    To = rel.To,
                    Kind = rel.Kind,
                    Role = rel.Role,
                    SourcePath = rel.SourcePath,
                    Strength = rel.Strength,
                    IsRuntimeBinding = rel.IsRuntimeBinding
                };
                edges.Add(edge);

                if (TryResolveNode(rel.From, nodes, out var fromNode))
                    ((HashSet<int>)fromNode!.OutgoingEdgeIndices).Add(edge.Index);

                if (TryResolveNode(rel.To, nodes, out var toNode))
                    ((HashSet<int>)toNode!.IncomingEdgeIndices).Add(edge.Index);
            }
        }

        // Phase 4: Build consumer index
        var consumersByIdentity = new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>();
        var consumersByExactVersion = new Dictionary<(DescriptorIdentity, int), List<(DescriptorRef, DescriptorEdge)>>();
        var consumersByUnpinnedVersion = new Dictionary<DescriptorIdentity, List<(DescriptorRef, DescriptorEdge)>>();

        foreach (var edge in edges)
        {
            var identity = new DescriptorIdentity(edge.To.Namespace, edge.To.Id);

            if (!consumersByIdentity.ContainsKey(identity))
                consumersByIdentity[identity] = new();
            consumersByIdentity[identity].Add((edge.From, edge));

            if (edge.To.Version.HasValue)
            {
                var key = (identity, edge.To.Version.Value);
                if (!consumersByExactVersion.ContainsKey(key))
                    consumersByExactVersion[key] = new();
                consumersByExactVersion[key].Add((edge.From, edge));
            }
            else
            {
                if (!consumersByUnpinnedVersion.ContainsKey(identity))
                    consumersByUnpinnedVersion[identity] = new();
                consumersByUnpinnedVersion[identity].Add((edge.From, edge));
            }
        }

        // Phase 3: Freeze node edge indices
        foreach (var key in nodes.Keys.ToList())
        {
            nodes[key] = nodes[key] with
            {
                OutgoingEdgeIndices = nodes[key].OutgoingEdgeIndices.ToHashSet(),
                IncomingEdgeIndices = nodes[key].IncomingEdgeIndices.ToHashSet()
            };
        }

        // Phase 5: Diagnostics
        var diagnosticList = new List<DescriptorTopologyDiagnostic>();

        // 5a. MISSING_TARGET
        foreach (var edge in edges)
        {
            if (!ContainsNode(edge.To, nodes))
            {
                var severity = edge.Strength == RelationshipStrength.Strong
                    ? DiagnosticSeverity.Error
                    : DiagnosticSeverity.Warning;
                diagnosticList.Add(new DescriptorTopologyDiagnostic(
                    severity,
                    "MISSING_TARGET",
                    $"Edge {edge.From.FullId} --[{edge.Kind}]--> {edge.To.FullId}: target descriptor not found. " +
                    $"Role='{edge.Role}', SourcePath='{edge.SourcePath}', Strength={edge.Strength}.",
                    edge.From,
                    new[] { edge.To }));
            }
        }

        // 5b. STRONG_CYCLE — DFS on Strong edges where both From and To exist
        var visited = new HashSet<DescriptorRef>();
        var inStack = new HashSet<DescriptorRef>();
        var parent = new Dictionary<DescriptorRef, DescriptorRef>();

        foreach (var nodeRef in nodes.Keys)
        {
            if (!visited.Contains(nodeRef))
                DfsCycleDetect(nodeRef, nodes, edges, visited, inStack, parent, diagnosticList);
        }

        // 5c. ORPHAN
        foreach (var node in nodes.Values)
        {
            if (node.IncomingEdgeIndices.Count == 0
                && node.State != DescriptorState.Draft
                && node.State != DescriptorState.Removed)
            {
                diagnosticList.Add(new DescriptorTopologyDiagnostic(
                    DiagnosticSeverity.Warning,
                    "ORPHAN",
                    $"Descriptor '{node.Ref.FullId}' ({node.Kind}) has no consumers.",
                    node.Ref,
                    null));
            }
        }

        // 5d. EXACT_DUPLICATE — full semantic key
        var seen = new HashSet<(DescriptorRef, DescriptorRef, RelationshipKind, string?, string?, RelationshipStrength, bool)>();
        foreach (var edge in edges)
        {
            var key = (edge.From, edge.To, edge.Kind, edge.Role, edge.SourcePath, edge.Strength, edge.IsRuntimeBinding);
            if (!seen.Add(key))
            {
                diagnosticList.Add(new DescriptorTopologyDiagnostic(
                    DiagnosticSeverity.Warning,
                    "EXACT_DUPLICATE",
                    $"Duplicate edge: {edge.From.FullId} --[{edge.Kind}]--> {edge.To.FullId} " +
                    $"(Role='{edge.Role}', SourcePath='{edge.SourcePath}', Strength={edge.Strength})",
                    edge.From,
                    new[] { edge.To }));
            }
        }

        // 5e. UNSUPPORTED_REFERENCE — explicit whitelist
        var knownUnsupported = new HashSet<(string Role, RelationshipKind Kind)>
        {
            (RelationshipRoles.SubWorkflowStep, RelationshipKind.References),
        };
        foreach (var edge in edges)
        {
            if (edge.Role is not null && knownUnsupported.Contains((edge.Role, edge.Kind)))
            {
                diagnosticList.Add(new DescriptorTopologyDiagnostic(
                    DiagnosticSeverity.Warning,
                    "UNSUPPORTED_REFERENCE",
                    $"Edge '{edge.Role}' ({edge.Kind}) from {edge.From.FullId} to {edge.To.FullId} " +
                    $"is not supported at runtime.",
                    edge.From,
                    new[] { edge.To }));
            }
        }

        var diagnostics = new DescriptorTopologyDiagnostics
        {
            All = diagnosticList.AsReadOnly()
        };

        return new DescriptorTopologySnapshot(
            nodes, edges, diagnostics,
            consumersByIdentity, consumersByExactVersion, consumersByUnpinnedVersion,
            DateTimeOffset.UtcNow);
    }

    private static bool TryResolveNode(
        DescriptorRef target,
        Dictionary<DescriptorRef, DescriptorNode> nodes,
        out DescriptorNode? resolvedNode)
    {
        if (nodes.TryGetValue(target, out var node))
        {
            resolvedNode = node;
            return true;
        }

        if (target.Version == null)
        {
            var match = nodes.Values.FirstOrDefault(n =>
                n.Ref.Namespace == target.Namespace && n.Ref.Id == target.Id);
            if (match is not null)
            {
                resolvedNode = match;
                return true;
            }
        }

        resolvedNode = null;
        return false;
    }

    private static bool ContainsNode(
        DescriptorRef target,
        Dictionary<DescriptorRef, DescriptorNode> nodes)
    {
        return TryResolveNode(target, nodes, out _);
    }

    private static void DfsCycleDetect(
        DescriptorRef current,
        Dictionary<DescriptorRef, DescriptorNode> nodes,
        List<DescriptorEdge> edges,
        HashSet<DescriptorRef> visited,
        HashSet<DescriptorRef> inStack,
        Dictionary<DescriptorRef, DescriptorRef> parent,
        List<DescriptorTopologyDiagnostic> diagnostics)
    {
        visited.Add(current);
        inStack.Add(current);

        if (TryResolveNode(current, nodes, out var node))
        {
            foreach (var edgeIdx in node!.OutgoingEdgeIndices)
            {
                var edge = edges[edgeIdx];

                if (edge.Strength != RelationshipStrength.Strong)
                    continue;
                if (!ContainsNode(edge.To, nodes))
                    continue;

                if (!visited.Contains(edge.To))
                {
                    parent[edge.To] = current;
                    DfsCycleDetect(edge.To, nodes, edges, visited, inStack, parent, diagnostics);
                }
                else if (inStack.Contains(edge.To))
                {
                    var path = new List<DescriptorRef> { edge.To };
                    var p = current;
                    while (!p.Equals(edge.To))
                    {
                        path.Add(p);
                        if (!parent.TryGetValue(p, out p))
                            break;
                    }
                    path.Add(edge.To);
                    path.Reverse();

                    diagnostics.Add(new DescriptorTopologyDiagnostic(
                        DiagnosticSeverity.Error,
                        "STRONG_CYCLE",
                        $"Strong dependency cycle detected: {string.Join(" → ", path.Select(r => r.FullId))}",
                        current,
                        path.AsReadOnly()));
                }
            }
        }

        inStack.Remove(current);
    }
}
