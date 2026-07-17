using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.DescriptorCapability;

namespace CrestCreates.Agent.Tools;

public sealed class AgentToolCapabilityResolver
{
    private readonly ICapabilityRegistry _capabilities;

    public AgentToolCapabilityResolver(ICapabilityRegistry capabilities)
        => _capabilities = capabilities;

    public CapabilityDescriptor Resolve(CapabilityProjectionReference reference)
    {
        var capability = reference.SelectionMode switch
        {
            VersionSelectionMode.Exact when reference.Version > 0 =>
                _capabilities.GetByVersion(reference.Id, reference.Version),
            VersionSelectionMode.Latest when reference.Version == 0 =>
                _capabilities.GetAll()
                    .Where(candidate => candidate.Id == reference.Id
                        && candidate.State == DescriptorState.Active)
                    .MaxBy(candidate => candidate.Version),
            _ => throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.UnsupportedCapabilitySelection,
                "Agent Tool Capability selection is unsupported.")
        };

        if (capability is null)
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.CapabilityResolutionFailure,
                "Agent Tool Capability could not be resolved.");

        if (capability.State != DescriptorState.Active)
            throw new AgentToolConfigurationException(
                AgentToolStartupDiagnosticCodes.CapabilityResolutionFailure,
                "An active Agent Tool must resolve an active Capability.");

        return capability;
    }
}
