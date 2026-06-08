using CrestCreates.Capability.Abstractions;
using CrestCreates.Exposure.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Exposure.Tests;

public class AgentToolDescriptorTests
{
    [Fact]
    public void AgentTool_References_Capability_By_VersionedRef()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 3),
            Description = "Creates a new customer record"
        };

        tool.Capability.Id.Should().Be("cap_01");
        tool.Capability.Version.Should().Be(3);
    }

    [Fact]
    public void AgentTool_Defaults_ToolCallMode_To_Auto()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
        };

        tool.ToolCallMode.Should().Be(ToolCallMode.Auto);
    }

    [Fact]
    public void AgentTool_BudgetLimit_Is_Optional()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
        };

        tool.BudgetLimit.Should().BeNull();
    }

    [Fact]
    public void AgentTool_Tags_Defaults_To_Empty()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
        };

        tool.Tags.Should().BeEmpty();
    }

    [Fact]
    public void AgentTool_Tags_Can_Be_Set()
    {
        var tool = new AgentToolDescriptor
        {
            Id = "tool_01",
            Name = "create_customer",
            Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
            Tags = new List<string> { "customer", "crm", "create" }
        };

        tool.Tags.Should().HaveCount(3);
        tool.Tags.Should().Contain("crm");
    }
}
