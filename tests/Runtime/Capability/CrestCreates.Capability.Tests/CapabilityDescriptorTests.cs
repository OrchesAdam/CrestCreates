using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityDescriptorTests
{
    [Fact]
    public void CapabilityDescriptor_Implements_IVersionedDescriptor()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "crm.customer.create",
            Version = 1,
            CapabilityKind = CapabilityKind.Command,
            InputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 1),
            OutputSchema = new VersionedDescriptorRef<SchemaDescriptor>("schema_02", 1),
            Permissions = new[] { "Customer.Create" },
            RiskLevel = CapabilityRiskLevel.Medium
        };

        descriptor.Should().BeAssignableTo<IVersionedDescriptor>();
        descriptor.Kind.Should().Be(DescriptorKind.Capability);
    }

    [Fact]
    public void CapabilityDescriptor_SemanticTags_Defaults_Empty()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_02",
            Name = "test.operation",
            Version = 1
        };

        descriptor.SemanticTags.Should().BeEmpty();
    }

    [Fact]
    public void CapabilityDescriptor_Permissions_Defaults_Empty()
    {
        var descriptor = new CapabilityDescriptor
        {
            Id = "cap_03",
            Name = "test.operation",
            Version = 1
        };

        descriptor.Permissions.Should().BeEmpty();
    }

    [Fact]
    public void CapabilityKind_Query_And_Command_Only()
    {
        var values = Enum.GetValues<CapabilityKind>();

        values.Should().Contain(CapabilityKind.Query);
        values.Should().Contain(CapabilityKind.Command);
        values.Should().HaveCount(2);
    }
}
