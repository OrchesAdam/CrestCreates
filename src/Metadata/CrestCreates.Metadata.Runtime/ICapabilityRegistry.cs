using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata;

/// <summary>
/// Unified capability registry interface. Lives in Metadata (not Capability.Abstractions)
/// to avoid circular dependency (Metadata -> HumanTask.Abstractions -> Capability.Abstractions).
/// </summary>
public interface ICapabilityRegistry : IVersionedDescriptorRegistry<CapabilityDescriptor>
{
    IReadOnlyList<CapabilityDescriptor> GetByKind(CapabilityKind kind);
    IReadOnlyList<CapabilityDescriptor> GetByTag(string tag);
}
