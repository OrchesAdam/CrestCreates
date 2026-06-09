using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class CapabilityRegistryTests
{
    private class TestCapabilityProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly List<CapabilityDescriptor> _descriptors;
        public TestCapabilityProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }

    [Fact]
    public void Build_succeeds_with_valid_descriptors()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var provider = new TestCapabilityProvider([
            new CapabilityDescriptor { Id = "approval", Name = "Approval" }
        ]);
        registry.Build([provider]);
        registry.State.Should().Be(RegistryState.Built);
    }

    [Fact]
    public void GetById_returns_capability()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var provider = new TestCapabilityProvider([
            new CapabilityDescriptor { Id = "approval", Name = "Approval" }
        ]);
        registry.Build([provider]);
        registry.GetById("approval")!.Name.Should().Be("Approval");
    }

    [Fact]
    public void Categories_are_preserved()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var provider = new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "approval",
                Name = "Approval",
                Categories = ["HumanTask", "Workflow"]
            }
        ]);
        registry.Build([provider]);
        registry.GetById("approval")!.Categories.Should().Contain("HumanTask");
    }

    [Fact]
    public void Produces_and_Consumes_are_preserved()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        var provider = new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "approval",
                Name = "Approval",
                Produces = [new EventRef("event", "approval.completed")],
                Consumes = [new EventRef("event", "approval.requested")]
            }
        ]);
        registry.Build([provider]);
        var cap = registry.GetById("approval")!;
        cap.Produces.Should().HaveCount(1);
        cap.Consumes.Should().HaveCount(1);
    }
}
