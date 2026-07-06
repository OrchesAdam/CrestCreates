using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.DynamicApi;

public interface ICapabilityEndpointRegistry
    : IVersionedDescriptorRegistry<CapabilityEndpointDescriptor>
{
    IReadOnlyList<CapabilityEndpointDescriptor> GetByCapability(
        string capabilityId,
        int? capabilityVersion = null);
}
