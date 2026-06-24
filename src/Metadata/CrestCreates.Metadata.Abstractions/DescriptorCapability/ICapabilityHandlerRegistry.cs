namespace CrestCreates.Metadata.Abstractions.DescriptorCapability;

/// <summary>
/// Source Generator implements this interface, providing a static mapping
/// of capability id → handler type.
/// key = CapabilityId (stable identifier), not Name (display name).
/// </summary>
public interface ICapabilityHandlerRegistry
{
    IReadOnlyDictionary<string, Type> GetHandlerMappings();
}
