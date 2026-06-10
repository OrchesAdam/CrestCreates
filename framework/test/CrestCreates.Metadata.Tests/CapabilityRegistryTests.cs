using CrestCreates.Metadata;
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

    [Fact]
    public void GetByKind_returns_matching_capabilities()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider([
            new() { Id = "cmd.one", Name = "cmd.one", Version = 1, CapabilityKind = CapabilityKind.Command },
            new() { Id = "cmd.two", Name = "cmd.two", Version = 1, CapabilityKind = CapabilityKind.Command },
            new() { Id = "qry.one", Name = "qry.one", Version = 1, CapabilityKind = CapabilityKind.Query }
        ])]);

        var commands = registry.GetByKind(CapabilityKind.Command);

        commands.Should().HaveCount(2);
        commands.Should().OnlyContain(d => d.CapabilityKind == CapabilityKind.Command);
    }

    [Fact]
    public void GetByTag_returns_matching_capabilities()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider([
            new() { Id = "a", Name = "a", Version = 1, SemanticTags = ["customer", "crm"] },
            new() { Id = "b", Name = "b", Version = 1, SemanticTags = ["order"] },
            new() { Id = "c", Name = "c", Version = 1, SemanticTags = ["customer"] }
        ])]);

        var customerCaps = registry.GetByTag("customer");

        customerCaps.Should().HaveCount(2);
        customerCaps.Should().OnlyContain(d => d.SemanticTags.Contains("customer"));
    }
}
