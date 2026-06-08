using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityRegistry : IVersionedDescriptorRegistry<CapabilityDescriptor>
{
    IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind);
    IReadOnlyList<CapabilityDescriptor> GetByTag(string tag);
}
