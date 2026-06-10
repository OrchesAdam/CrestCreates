using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Capability.Abstractions;

/// <summary>
/// Unified resolution entry point. All runtimes (Workflow, Agent, HTTP, MCP)
/// must resolve capabilities through this interface.
/// </summary>
public interface ICapabilityResolver
{
    IVersionedDescriptor Resolve(CapabilityRef capabilityRef);

    IVersionedDescriptor Resolve(string capabilityIdOrVersion)
        => Resolve(CapabilityRef.Parse(capabilityIdOrVersion));
}
