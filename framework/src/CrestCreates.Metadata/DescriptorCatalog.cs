using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public sealed class DescriptorCatalog : IDescriptorCatalog
{
    private readonly IGlobalDescriptorRegistry _globalRegistry;
    private readonly IDescriptorDependencyGraph _dependencyGraph;

    public DescriptorCatalog(
        IGlobalDescriptorRegistry globalRegistry,
        IDescriptorDependencyGraph dependencyGraph)
    {
        _globalRegistry = globalRegistry;
        _dependencyGraph = dependencyGraph;
    }

    public IDescriptor? Get(string id) => _globalRegistry.GetById(id);

    public IEnumerable<IDescriptor> GetAll() => _globalRegistry.GetAll();

    public IEnumerable<IDescriptor> FindByKind(DescriptorKind kind) =>
        _globalRegistry.GetByKind(kind);

    public IEnumerable<IDescriptor> FindByPackage(string packageId) =>
        _globalRegistry.GetByPackage(packageId);

    public IEnumerable<IDescriptor> FindDependents(string descriptorId)
    {
        var edges = _dependencyGraph.GetDependents(descriptorId);
        return edges.Select(e => _globalRegistry.GetById(e.SourceId)).Where(d => d is not null)!;
    }

    public IEnumerable<IDescriptor> FindDependencies(string descriptorId)
    {
        var edges = _dependencyGraph.GetDependencies(descriptorId);
        return edges.Select(e => _globalRegistry.GetById(e.TargetId)).Where(d => d is not null)!;
    }

    public ImpactReport AnalyzeImpact(string descriptorId, int fromVersion, int toVersion) =>
        _dependencyGraph.AnalyzeImpact(descriptorId, fromVersion, toVersion);
}