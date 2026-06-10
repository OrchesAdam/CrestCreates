using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskRegistryTests
{
    private static HumanTaskDescriptor CreateTask(string id, string name, int version)
    {
        return new HumanTaskDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1)
        };
    }

    private class TestHumanTaskProvider : IDescriptorProvider<HumanTaskDescriptor>
    {
        private readonly List<HumanTaskDescriptor> _descriptors;
        public TestHumanTaskProvider(List<HumanTaskDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<HumanTaskDescriptor> GetDescriptors() => _descriptors;
    }

    private static HumanTaskRegistry CreateRegistry(params HumanTaskDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<HumanTaskDescriptor>(Array.Empty<IRegistryValidator<HumanTaskDescriptor>>());
        var registry = new HumanTaskRegistry(engine);
        registry.Build([new TestHumanTaskProvider(descriptors.ToList())]);
        return registry;
    }

    [Fact]
    public void Register_And_GetById_Works()
    {
        var registry = CreateRegistry(CreateTask("ht_01", "manager.approval", 1));

        var result = registry.GetById("ht_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("manager.approval");
    }

    [Fact]
    public void GetAll_Returns_All_Tasks()
    {
        var registry = CreateRegistry(
            CreateTask("ht_01", "task.a", 1),
            CreateTask("ht_02", "task.b", 1));

        var all = registry.GetAll();
        all.Should().HaveCount(2);
    }

    [Fact]
    public void Build_Sets_State_To_Built()
    {
        var engine = new RegistryValidationEngine<HumanTaskDescriptor>(Array.Empty<IRegistryValidator<HumanTaskDescriptor>>());
        var registry = new HumanTaskRegistry(engine);
        var provider = new TestHumanTaskProvider([CreateTask("ht_01", "t", 1)]);

        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }
}
