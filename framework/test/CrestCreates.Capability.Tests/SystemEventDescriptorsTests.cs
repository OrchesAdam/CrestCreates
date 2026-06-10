using CrestCreates.Capability;
using CrestCreates.Event;
using CrestCreates.Event.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class SystemEventDescriptorsTests
{
    [Fact]
    public void All_System_Events_Have_Unique_Ids()
    {
        var provider = new SystemEventDescriptorProvider();
        var descriptors = provider.GetDescriptors();

        var ids = descriptors.Select(d => d.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void All_System_Events_Have_Active_State()
    {
        var provider = new SystemEventDescriptorProvider();
        var descriptors = provider.GetDescriptors();

        descriptors.Should().AllSatisfy(d => d.State.Should().Be(DescriptorState.Active));
    }

    [Fact]
    public void RegisterAll_Registers_All_Four_Events()
    {
        var validationEngine = new RegistryValidationEngine<GeneratedEventDescriptor>([]);
        var registry = new EventRegistry(validationEngine);
        registry.Build([new SystemEventDescriptorProvider()]);

        var all = registry.GetAll();
        all.Should().HaveCount(4);
    }
}
