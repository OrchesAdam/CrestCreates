using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityRegistryTests
{
    [Fact]
    public void Register_And_GetById_Returns_Descriptor()
    {
        var registry = new CapabilityRegistry();
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            CapabilityKind = CapabilityKind.Command
        };

        registry.Register(descriptor);
        var result = registry.GetById("cap_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("crm.customer.create");
    }

    [Fact]
    public void GetByKind_Filters_Correctly()
    {
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.read",
            Version = 1,
            CapabilityKind = CapabilityKind.Query
        });
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_02",
            Name = "crm.customer.create",
            Version = 1,
            CapabilityKind = CapabilityKind.Command
        });

        var queries = registry.GetByKind(CapabilityKind.Query);

        queries.Should().HaveCount(1);
        queries[0].Name.Should().Be("crm.customer.read");
    }

    [Fact]
    public void GetByTag_Finds_SemanticTags()
    {
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            SemanticTags = new List<string> { "customer", "crm", "create" }
        });
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_02",
            Name = "hr.employee.create",
            Version = 1,
            SemanticTags = new List<string> { "employee", "hr", "create" }
        });

        var customerCaps = registry.GetByTag("customer");

        customerCaps.Should().HaveCount(1);
        customerCaps[0].Name.Should().Be("crm.customer.create");
    }

    [Fact]
    public void GetByTag_Shared_Tag_Returns_Multiple()
    {
        var registry = new CapabilityRegistry();
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            SemanticTags = new List<string> { "create" }
        });
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_02",
            Name = "hr.employee.create",
            Version = 1,
            SemanticTags = new List<string> { "create" }
        });

        var createCaps = registry.GetByTag("create");

        createCaps.Should().HaveCount(2);
    }
}
