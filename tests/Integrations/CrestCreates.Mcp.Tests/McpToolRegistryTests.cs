using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Metadata.Registry;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpToolRegistryTests
{
    [Fact]
    public void Build_retains_all_versions_and_states()
    {
        var validation = new RegistryValidationEngine<McpToolDescriptor>(
            [new McpToolDescriptorValidator()]);
        var registry = new McpToolRegistry(validation);
        var active = Create(1, DescriptorState.Active, "orders.get");
        var deprecated = Create(2, DescriptorState.Deprecated, "orders.get.v2");

        registry.Build([new Provider([active, deprecated])]);

        registry.GetAll().Should().BeEquivalentTo([active, deprecated]);
        registry.GetByVersion(active.Id, 1).Should().BeSameAs(active);
        registry.GetByVersion(deprecated.Id, 2).Should().BeSameAs(deprecated);
    }

    private static McpToolDescriptor Create(
        int version,
        DescriptorState state,
        string toolName) => new()
    {
        Id = "mcp-tool:orders.get",
        Name = "Get order",
        Version = version,
        State = state,
        Capability = new McpCapabilityReference(
            "orders.get", version, VersionSelectionMode.Exact),
        ToolName = toolName,
        Description = "Gets one order."
    };

    private sealed class Provider(IReadOnlyList<McpToolDescriptor> descriptors)
        : IDescriptorProvider<McpToolDescriptor>
    {
        public IReadOnlyList<McpToolDescriptor> GetDescriptors() => descriptors;
    }
}
