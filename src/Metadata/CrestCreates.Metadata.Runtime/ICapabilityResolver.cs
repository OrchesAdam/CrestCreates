using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Metadata;

/// <summary>
/// Unified resolution entry point. All runtimes (Workflow, Agent, HTTP, MCP)
/// must resolve capabilities through this interface.
/// </summary>
public interface ICapabilityResolver
{
    CapabilityDescriptor Resolve(CapabilityRef capabilityRef);

    CapabilityDescriptor Resolve(string capabilityIdOrVersion)
        => Resolve(CapabilityRef.Parse(capabilityIdOrVersion));
}
