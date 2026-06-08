using System.Collections.Concurrent;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DescriptorDependencyGraph : IDescriptorDependencyGraph
{
    private readonly ConcurrentBag<DependencyEdge> _edges = new();

    public void AddEdge(string sourceId, string targetId, DescriptorDependencyKind kind)
    {
        _edges.Add(new DependencyEdge
        {
            SourceId = sourceId,
            TargetId = targetId,
            Kind = kind
        });
    }

    public IReadOnlyList<DependencyEdge> GetDependencies(string descriptorId)
    {
        return _edges.Where(e => e.SourceId == descriptorId).ToList().AsReadOnly();
    }

    public IReadOnlyList<DependencyEdge> GetDependents(string descriptorId)
    {
        return _edges.Where(e => e.TargetId == descriptorId).ToList().AsReadOnly();
    }

    public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion)
    {
        var dependents = GetDependents(descriptorId);
        return new ImpactReport
        {
            DescriptorId = descriptorId,
            FromVersion = fromVersion,
            ToVersion = toVersion,
            AffectedDependents = dependents
        };
    }
}