using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class SystemEventDescriptorsTests
{
    [Fact]
    public void All_System_Events_Have_Unique_Ids()
    {
        var ids = new[]
        {
            SystemEventDescriptors.CapabilityExecuting.Id,
            SystemEventDescriptors.CapabilitySucceeded.Id,
            SystemEventDescriptors.CapabilityFailed.Id,
            SystemEventDescriptors.CapabilityCompensated.Id
        };

        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_System_Events_Have_Capability_Category()
    {
        SystemEventDescriptors.CapabilityExecuting.Category.Should().Be(EventCategory.Capability);
        SystemEventDescriptors.CapabilitySucceeded.Category.Should().Be(EventCategory.Capability);
        SystemEventDescriptors.CapabilityFailed.Category.Should().Be(EventCategory.Capability);
        SystemEventDescriptors.CapabilityCompensated.Category.Should().Be(EventCategory.Capability);
    }

    [Fact]
    public void Executing_Has_StateTransition_Semantic()
    {
        SystemEventDescriptors.CapabilityExecuting.Semantic.Should().Be(EventSemantic.StateTransition);
        SystemEventDescriptors.CapabilityCompensated.Semantic.Should().Be(EventSemantic.StateTransition);
    }

    [Fact]
    public void Succeeded_And_Failed_Have_Fact_Semantic()
    {
        SystemEventDescriptors.CapabilitySucceeded.Semantic.Should().Be(EventSemantic.Fact);
        SystemEventDescriptors.CapabilityFailed.Semantic.Should().Be(EventSemantic.Fact);
    }

    [Fact]
    public void RegisterAll_Registers_All_Four_Events()
    {
        var registry = new Event.EventRegistry();
        SystemEventDescriptors.RegisterAll(registry);

        var all = registry.GetAll();
        all.Should().HaveCount(4);
    }

    [Fact]
    public void System_Events_Are_Active()
    {
        SystemEventDescriptors.CapabilityExecuting.State.Should().Be(DescriptorState.Active);
        SystemEventDescriptors.CapabilitySucceeded.State.Should().Be(DescriptorState.Active);
        SystemEventDescriptors.CapabilityFailed.State.Should().Be(DescriptorState.Active);
        SystemEventDescriptors.CapabilityCompensated.State.Should().Be(DescriptorState.Active);
    }
}
