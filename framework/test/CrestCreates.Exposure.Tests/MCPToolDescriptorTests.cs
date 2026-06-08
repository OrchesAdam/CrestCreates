using CrestCreates.Capability.Abstractions;
using CrestCreates.Exposure.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Exposure.Tests;

public class MCPToolDescriptorTests
{
    [Fact]
    public void MCPTool_References_Capability_By_VersionedRef()
    {
        var tool = new MCPToolDescriptor
        {
            Id = "mcp_01",
            Name = "customer_create",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 2),
            Description = "MCP tool for customer creation"
        };

        tool.Capability.Id.Should().Be("cap_01");
        tool.Capability.Version.Should().Be(2);
    }

    [Fact]
    public void MCPTool_Defaults_ToolCallMode_To_Auto()
    {
        var tool = new MCPToolDescriptor
        {
            Id = "mcp_01",
            Name = "customer_create",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
        };

        tool.ToolCallMode.Should().Be(ToolCallMode.Auto);
    }

    [Fact]
    public void MCPTool_Description_Is_Stored()
    {
        var tool = new MCPToolDescriptor
        {
            Id = "mcp_01",
            Name = "customer_create",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            Description = "Creates a customer via MCP protocol"
        };

        tool.Description.Should().Be("Creates a customer via MCP protocol");
    }
}
