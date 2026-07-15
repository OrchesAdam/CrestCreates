using System.Text.Json;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpToolRuntimeSnapshotBuilderTests
{
    [Fact]
    public void Build_resolves_latest_once_and_excludes_inactive_tools_from_runtime_requirements()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var active = Tool("mcp-tool:active." + suffix, "active." + suffix, DescriptorState.Active);
        var deprecated = Tool("mcp-tool:old." + suffix, "old." + suffix, DescriptorState.Deprecated);
        var capabilityV1 = Capability(1);
        var capabilityV2 = Capability(2);
        var tools = new Mock<IMcpToolRegistry>();
        var capabilities = new Mock<ICapabilityRegistry>();
        var schemas = new Mock<ISchemaRegistry>();
        tools.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        capabilities.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        schemas.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        tools.Setup(registry => registry.GetAll()).Returns([active, deprecated]);
        capabilities.Setup(registry => registry.GetAll()).Returns([capabilityV1, capabilityV2]);
        McpToolBindingRegistry.Register(VoidBinding(active));

        var builder = new McpToolRuntimeSnapshotBuilder(
            tools.Object,
            capabilities.Object,
            schemas.Object,
            new McpJsonSchemaProjector(),
            new McpToolSchemaParityValidator(),
            new DefaultCanonicalHashComputer(),
            new McpJsonOptions
            {
                SerializerOptions = new JsonSerializerOptions
                {
                    TypeInfoResolver = McpTestJsonContext.Default
                }
            });

        var snapshot = builder.Build();

        snapshot.Entries.Should().ContainSingle();
        snapshot.Find(active.ToolName)!.Capability.Version.Should().Be(2);
        snapshot.Find(deprecated.ToolName).Should().BeNull();
        capabilities.Verify(registry => registry.GetAll(), Times.Once);
    }

    [Fact]
    public void Non_source_generated_json_resolver_fails_closed()
    {
        var tools = new Mock<IMcpToolRegistry>();
        tools.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        var capabilities = new Mock<ICapabilityRegistry>();
        capabilities.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        var schemas = new Mock<ISchemaRegistry>();
        schemas.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        tools.Setup(registry => registry.GetAll()).Returns([]);
        var builder = new McpToolRuntimeSnapshotBuilder(
            tools.Object,
            capabilities.Object,
            schemas.Object,
            new McpJsonSchemaProjector(),
            new McpToolSchemaParityValidator(),
            new DefaultCanonicalHashComputer(),
            new McpJsonOptions { SerializerOptions = new JsonSerializerOptions() });

        var action = () => builder.Build();

        action.Should().Throw<McpToolConfigurationException>().Which.Code.Should().Be("MCP114");
    }

    [Fact]
    public void Multiple_source_generated_contexts_in_resolver_chain_are_supported()
    {
        var tools = new Mock<IMcpToolRegistry>();
        tools.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        var capabilities = new Mock<ICapabilityRegistry>();
        capabilities.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        var schemas = new Mock<ISchemaRegistry>();
        schemas.SetupGet(registry => registry.State).Returns(RegistryState.Built);
        tools.Setup(registry => registry.GetAll()).Returns([]);
        var options = new JsonSerializerOptions();
        options.TypeInfoResolverChain.Add(McpTestJsonContext.Default);
        options.TypeInfoResolverChain.Add(SecondaryMcpTestJsonContext.Default);
        var builder = new McpToolRuntimeSnapshotBuilder(
            tools.Object,
            capabilities.Object,
            schemas.Object,
            new McpJsonSchemaProjector(),
            new McpToolSchemaParityValidator(),
            new DefaultCanonicalHashComputer(),
            new McpJsonOptions { SerializerOptions = options });

        var action = () => builder.Build();

        action.Should().NotThrow();
    }

    private static McpToolDescriptor Tool(string id, string toolName, DescriptorState state) => new()
    {
        Id = id,
        Name = toolName,
        Version = 1,
        State = state,
        Capability = new McpCapabilityReference(
            "orders.get", 0, VersionSelectionMode.Latest),
        ToolName = toolName,
        Description = "Gets an order."
    };

    private static CapabilityDescriptor Capability(int version) => new()
    {
        Id = "orders.get",
        Name = "Get order",
        Version = version,
        State = DescriptorState.Active,
        CapabilityKind = CapabilityKind.Query
    };

    private static McpToolBindingContract VoidBinding(McpToolDescriptor descriptor) => new()
    {
        ToolDescriptorId = descriptor.Id,
        ToolDescriptorVersion = descriptor.Version,
        BindInputAsync = (json, typeInfo, cancellationToken) => ValueTask.FromResult<object?>(null),
        SerializeOutputAsync = (output, typeInfo, cancellationToken) => ValueTask.FromResult<JsonElement?>(null)
    };
}

[System.Text.Json.Serialization.JsonSerializable(typeof(int))]
internal partial class SecondaryMcpTestJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
