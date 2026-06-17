using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.ContextPack.Abstractions;

public interface IMetadataContextPackBuilder
{
    MetadataContextPack Build(
        MetadataContextPackRequest request,
        DescriptorTopologySnapshot topology,
        IReadOnlyList<IDescriptor> descriptors);
}
