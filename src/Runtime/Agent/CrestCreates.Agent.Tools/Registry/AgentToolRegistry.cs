using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.AgentTool;
using CrestCreates.Metadata.Registry;

namespace CrestCreates.Agent.Tools;

public interface IAgentToolRegistry
{
    RegistryState State { get; }

    AgentCapabilityToolDescriptor? GetById(string id);

    AgentCapabilityToolDescriptor? GetByVersion(string id, int version);

    IReadOnlyList<AgentCapabilityToolDescriptor> GetAll();
}

public sealed class AgentToolRegistry : RegistryBase<AgentCapabilityToolDescriptor>, IAgentToolRegistry
{
    protected override string RegistryNamespace => "agent-tool";

    public AgentToolRegistry(IRegistryValidationEngine<AgentCapabilityToolDescriptor> validationEngine)
        : base(validationEngine)
    {
    }

    protected override RegistrySnapshot<AgentCapabilityToolDescriptor> BuildSnapshot(
        List<AgentCapabilityToolDescriptor> descriptors)
    {
        var byId = descriptors
            .GroupBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .ToFrozenDictionary(
                group => group.Key,
                group => group.OrderByDescending(descriptor => descriptor.Version).First(),
                StringComparer.Ordinal);

        var byName = descriptors
            .GroupBy(descriptor => descriptor.Name, StringComparer.Ordinal)
            .ToFrozenDictionary(
                group => group.Key,
                group => group.OrderByDescending(descriptor => descriptor.Version).ToImmutableArray(),
                StringComparer.Ordinal);

        var byVersion = descriptors.ToFrozenDictionary(
            descriptor => new DescriptorKey(descriptor.Namespace, descriptor.Id, descriptor.Version),
            descriptor => descriptor);

        return new RegistrySnapshot<AgentCapabilityToolDescriptor>(
            byId,
            byName,
            byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
