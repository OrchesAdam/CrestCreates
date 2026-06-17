using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorTopology;

namespace CrestCreates.Metadata.ContextPack;

internal sealed record ResolvedDescriptor(
    DescriptorRef RequestedRef,
    DescriptorRef CanonicalRef,
    DescriptorNode? TopologyNode,
    IDescriptor? Descriptor,
    bool IsAmbiguousUnpinned);
