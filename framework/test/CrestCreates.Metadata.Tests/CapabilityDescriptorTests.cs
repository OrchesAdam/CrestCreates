using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class CapabilityDescriptorTests
{
    [Fact]
    public void Has_runtime_properties_from_merged_descriptor()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "customer.create",
            Name = "Create Customer",
            Version = 1,
            CapabilityKind = CapabilityKind.Command,
            Permissions = new[] { "Customer.Create" },
            RiskLevel = CapabilityRiskLevel.Medium,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_customer_output", 1),
            SemanticTags = new[] { "customer", "crm" },
            Categories = new[] { "Customer" }
        };

        descriptor.CapabilityKind.Should().Be(CapabilityKind.Command);
        descriptor.Permissions.Should().Contain("Customer.Create");
        descriptor.RiskLevel.Should().Be(CapabilityRiskLevel.Medium);
        descriptor.InputSchema!.Value.Id.Should().Be("schema_customer");
        descriptor.OutputSchema!.Value.Id.Should().Be("schema_customer_output");
    }

    [Fact]
    public void Id_is_stable_identifier_name_is_display_name()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "customer.create",
            Name = "Create Customer",
            Version = 1
        };

        descriptor.Id.Should().Be("customer.create");
        descriptor.Name.Should().Be("Create Customer");
    }

    [Fact]
    public void Schema_refs_are_nullable()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "noop.ping",
            Version = 1
        };

        descriptor.InputSchema.Should().BeNull();
        descriptor.OutputSchema.Should().BeNull();
    }
}
