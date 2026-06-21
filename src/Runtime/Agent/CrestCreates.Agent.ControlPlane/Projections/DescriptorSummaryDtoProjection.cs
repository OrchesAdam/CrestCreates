using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Agent.ControlPlane.Projections;

/// <summary>
/// Projects IDescriptor to adapter-safe DescriptorSummaryDto.
/// Lives in ControlPlane (not Abstractions) because IDescriptor
/// must not appear in Abstractions.
/// </summary>
internal static class DescriptorSummaryDtoProjection
{
    public static DescriptorSummaryDto? FromDescriptor(IDescriptor? descriptor)
    {
        if (descriptor is null) return null;
        return new DescriptorSummaryDto
        {
            Ref = new DescriptorRef(descriptor.Namespace, descriptor.Id),
            Kind = descriptor.Kind,
            Name = descriptor.Name,
            DisplayName = descriptor.Name, // IDescriptor has no DisplayName; Name is the best proxy
            LifecycleState = descriptor.State.ToString()
        };
    }
}
