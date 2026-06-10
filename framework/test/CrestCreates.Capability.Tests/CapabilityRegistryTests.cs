using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityRegistryTests
{
    private sealed class TestCapabilityProvider : IDescriptorProvider<CapabilityDescriptor>
    {
        private readonly List<CapabilityDescriptor> _descriptors;
        public TestCapabilityProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
    }

    [Fact]
    public void Register_And_GetById_Returns_Descriptor()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor
            {
                Id = "cap_01",
                Name = "crm.customer.create",
                Version = 1,
                CapabilityKind = CapabilityKind.Command
            }
        ])]);
        var result = registry.GetById("cap_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("crm.customer.create");
    }

    [Fact]
    public void GetByKind_Filters_Correctly()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor { Id = "cap_01", Name = "crm.customer.read", Version = 1, CapabilityKind = CapabilityKind.Query },
            new CapabilityDescriptor { Id = "cap_02", Name = "crm.customer.create", Version = 1, CapabilityKind = CapabilityKind.Command }
        ])]);

        var queries = registry.GetByKind(CapabilityKind.Query);

        queries.Should().HaveCount(1);
        queries[0].Name.Should().Be("crm.customer.read");
    }

    [Fact]
    public void GetByTag_Finds_SemanticTags()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor { Id = "cap_01", Name = "crm.customer.create", Version = 1, SemanticTags = ["customer", "crm", "create"] },
            new CapabilityDescriptor { Id = "cap_02", Name = "hr.employee.create", Version = 1, SemanticTags = ["employee", "hr", "create"] }
        ])]);

        var customerCaps = registry.GetByTag("customer");

        customerCaps.Should().HaveCount(1);
        customerCaps[0].Name.Should().Be("crm.customer.create");
    }

    [Fact]
    public void GetByTag_Shared_Tag_Returns_Multiple()
    {
        var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
        var registry = new CapabilityRegistry(engine);
        registry.Build([new TestCapabilityProvider([
            new CapabilityDescriptor { Id = "cap_01", Name = "crm.customer.create", Version = 1, SemanticTags = ["create"] },
            new CapabilityDescriptor { Id = "cap_02", Name = "hr.employee.create", Version = 1, SemanticTags = ["create"] }
        ])]);

        var createCaps = registry.GetByTag("create");

        createCaps.Should().HaveCount(2);
    }
}
