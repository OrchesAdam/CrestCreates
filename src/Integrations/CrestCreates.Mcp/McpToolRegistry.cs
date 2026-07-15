using System.Collections.Frozen;
using System.Collections.Immutable;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.Registry;

namespace CrestCreates.Mcp;

public interface IMcpToolRegistry
{
    RegistryState State { get; }
    McpToolDescriptor? GetById(string id);

    McpToolDescriptor? GetByVersion(string id, int version);

    IReadOnlyList<McpToolDescriptor> GetAll();
}

public sealed class McpToolRegistry : RegistryBase<McpToolDescriptor>, IMcpToolRegistry
{
    protected override string RegistryNamespace => "mcp-tool";

    public McpToolRegistry(IRegistryValidationEngine<McpToolDescriptor> validationEngine)
        : base(validationEngine)
    {
    }

    protected override RegistrySnapshot<McpToolDescriptor> BuildSnapshot(
        List<McpToolDescriptor> descriptors)
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

        return new RegistrySnapshot<McpToolDescriptor>(
            byId,
            byName,
            byVersion,
            descriptors.ToImmutableArray(),
            ImmutableDictionary<Type, IRegistryIndex>.Empty);
    }
}
