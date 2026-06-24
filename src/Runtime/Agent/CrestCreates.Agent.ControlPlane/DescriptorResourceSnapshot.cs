using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane;

/// <summary>
/// Immutable snapshot of a resolved descriptor resource.
/// </summary>
internal sealed record DescriptorResourceSnapshot(IDescriptor Descriptor, DescriptorRef Ref);
