using CrestCreates.Metadata.Abstractions.DescriptorRelationship;

namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorDependencyGraph
{
    IReadOnlyList<DependencyEdge> GetDependencies(string descriptorId);
    IReadOnlyList<DependencyEdge> GetDependents(string descriptorId);
    ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion);
    void AddEdge(string sourceId, string targetId, DescriptorDependencyKind kind);
}