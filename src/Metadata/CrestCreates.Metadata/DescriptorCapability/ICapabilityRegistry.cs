using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;

namespace CrestCreates.Metadata.DescriptorCapability;

/// <summary>
/// Unified capability registry interface. Lives in Metadata.DescriptorCapability (not Capability.Abstractions)
/// to avoid circular dependency (Metadata -> HumanTask.Abstractions -> Capability.Abstractions).
/// </summary>
public interface ICapabilityRegistry : IVersionedDescriptorRegistry<CapabilityDescriptor>
{
    IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind);
    IReadOnlyList<CapabilityDescriptor> GetByTag(string tag);
}
