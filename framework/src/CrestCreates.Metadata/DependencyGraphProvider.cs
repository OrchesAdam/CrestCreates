using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

public static class DependencyGraphProvider
{
    private static IDescriptorDependencyGraph? _graph;

    public static void SetGraph(IDescriptorDependencyGraph graph)
    {
        _graph = graph;
    }

    public static void RegisterEdge(string sourceId, string targetId, DescriptorDependencyKind kind)
    {
        if (_graph is DescriptorDependencyGraph concrete)
        {
            concrete.AddEdge(sourceId, targetId, kind);
        }
    }
}