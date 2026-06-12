using CrestCreates.Form.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Form;

public static class FormDescriptorDependencyExtractor
{
    public static IReadOnlyList<DependencyEdge> Extract(FormDescriptor descriptor)
    {
        var edges = new List<DependencyEdge>
        {
            new()
            {
                SourceId = descriptor.Id,
                TargetId = descriptor.Schema.Id,
                Kind = DescriptorDependencyKind.Uses
            }
        };
        return edges;
    }
}
