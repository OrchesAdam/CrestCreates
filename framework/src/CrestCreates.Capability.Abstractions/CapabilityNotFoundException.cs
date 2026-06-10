namespace CrestCreates.Capability.Abstractions;

public sealed class CapabilityNotFoundException : Exception
{
    public CapabilityNotFoundException(string capabilityId)
        : base($"Capability '{capabilityId}' not found.")
    {
    }

    public CapabilityNotFoundException(CapabilityRef capabilityRef)
        : base($"Capability '{capabilityRef.Id}' (v{capabilityRef.Version?.ToString() ?? "latest"}) not found.")
    {
    }
}
