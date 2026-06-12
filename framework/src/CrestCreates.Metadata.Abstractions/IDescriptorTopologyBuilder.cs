using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.Abstractions;

public interface IDescriptorTopologyBuilder
{
    DescriptorTopologySnapshot Build(IReadOnlyList<IDescriptor> descriptors);
}
