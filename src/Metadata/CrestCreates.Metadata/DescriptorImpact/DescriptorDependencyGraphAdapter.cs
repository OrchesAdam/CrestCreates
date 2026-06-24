using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.DescriptorImpact;

public sealed class DescriptorDependencyGraphAdapter : IDescriptorDependencyGraph
{
    private readonly IDescriptorTopologyBuilder _builder;
    private readonly IReadOnlyList<IDescriptor> _descriptors;
    private DescriptorTopologySnapshot? _snapshot;

    public DescriptorDependencyGraphAdapter(
        IDescriptorTopologyBuilder builder,
        IReadOnlyList<IDescriptor> descriptors)
    {
        _builder = builder;
        _descriptors = descriptors;
    }

    private DescriptorTopologySnapshot Snapshot =>
        _snapshot ??= _builder.Build(_descriptors);

    public IReadOnlyList<DependencyEdge> GetDependencies(string descriptorId)
    {
        var node = FindNodeById(descriptorId);
        if (node is null) return Array.Empty<DependencyEdge>();

        var deps = Snapshot.GetDirectDependencies(node.Ref);
        return deps.Select(n =>
        {
            var matchingEdge = Snapshot.Edges.FirstOrDefault(e =>
                RefsMatch(e.From, node.Ref) && RefsMatch(e.To, n.Ref));
            return new DependencyEdge
            {
                SourceId = node.Ref.Id,
                TargetId = n.Ref.Id,
                Kind = matchingEdge is not null ? MapKind(matchingEdge.Kind) : DescriptorDependencyKind.References
            };
        }).ToList().AsReadOnly();
    }

    public IReadOnlyList<DependencyEdge> GetDependents(string descriptorId)
    {
        var node = FindNodeById(descriptorId);
        if (node is null) return Array.Empty<DependencyEdge>();

        var deps = Snapshot.GetDirectDependents(node.Ref);
        return deps.Select(n =>
        {
            var matchingEdge = Snapshot.Edges.FirstOrDefault(e =>
                RefsMatch(e.To, node.Ref) && RefsMatch(e.From, n.Ref));
            return new DependencyEdge
            {
                SourceId = n.Ref.Id,
                TargetId = node.Ref.Id,
                Kind = matchingEdge is not null ? MapKind(matchingEdge.Kind) : DescriptorDependencyKind.References
            };
        }).ToList().AsReadOnly();
    }

    public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion)
    {
        var node = FindNodeById(descriptorId);
        if (node is null)
        {
            return new ImpactReport
            {
                DescriptorId = descriptorId,
                FromVersion = fromVersion,
                ToVersion = toVersion,
                AffectedDependents = Array.Empty<DependencyEdge>()
            };
        }

        var directDependents = Snapshot.GetDirectDependents(node.Ref);
        var affected = directDependents.Select(n =>
        {
            var matchingEdge = Snapshot.Edges.FirstOrDefault(e =>
                RefsMatch(e.To, node.Ref) && RefsMatch(e.From, n.Ref));
            return new DependencyEdge
            {
                SourceId = n.Ref.Id,
                TargetId = node.Ref.Id,
                Kind = matchingEdge is not null ? MapKind(matchingEdge.Kind) : DescriptorDependencyKind.References
            };
        }).ToList();

        return new ImpactReport
        {
            DescriptorId = descriptorId,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            AffectedDependents = affected
        };
    }

    public void AddEdge(string sourceId, string targetId, DescriptorDependencyKind kind)
        => throw new NotSupportedException(
            "AddEdge is no longer supported. " +
            "Edges are computed from descriptor relationships via IDescriptorTopologyBuilder.");

    // Id-only lookup — adapter-internal, not on public snapshot API
    private DescriptorNode? FindNodeById(string descriptorId)
    {
        return Snapshot.Nodes.Values.FirstOrDefault(n => n.Ref.Id == descriptorId);
    }

    /// <summary>
    /// Check if an edge ref (edgeRef, may be unpinned) matches a resolved node ref (nodeRef, has version).
    /// True if exact match, or edgeRef.Version=null and same (Namespace, Id).
    /// </summary>
    private static bool RefsMatch(DescriptorRef edgeRef, DescriptorRef nodeRef)
    {
        return edgeRef.Equals(nodeRef) ||
               (edgeRef.Version == null &&
                edgeRef.Namespace == nodeRef.Namespace &&
                edgeRef.Id == nodeRef.Id);
    }

    private static DescriptorDependencyKind MapKind(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Produces   => DescriptorDependencyKind.Produces,
        RelationshipKind.Consumes   => DescriptorDependencyKind.Consumes,
        RelationshipKind.DependsOn  => DescriptorDependencyKind.References,
        RelationshipKind.References => DescriptorDependencyKind.References,
        RelationshipKind.Uses       => DescriptorDependencyKind.Uses,
        RelationshipKind.Triggers   => DescriptorDependencyKind.Triggers,
        _ => DescriptorDependencyKind.References
    };
}
