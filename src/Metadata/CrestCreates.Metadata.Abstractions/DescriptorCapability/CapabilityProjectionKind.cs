namespace CrestCreates.Metadata.Abstractions.DescriptorCapability;

/// <summary>
/// Marks the origin of a CapabilityDescriptor.
/// Compatibility projections are migration artifacts with an exit path to native capabilities.
/// </summary>
public enum CapabilityProjectionKind
{
    Native = 0,                    // Hand-designed native capability
    AppServiceCompatibility = 1,   // Auto-projected from AppService
}
