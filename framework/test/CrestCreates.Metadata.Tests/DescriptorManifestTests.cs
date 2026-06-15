using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorManifestTests
{
    [Fact]
    public void Serialize_And_Deserialize_Manifest()
    {
        var manifest = new DescriptorManifest
        {
            PackageId = "CrestCreates.CRM",
            PackageVersion = "1.0.0",
            DescriptorEntries = new[]
            {
                new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("schema", "schema_01", 1),
                    Kind = DescriptorKind.Schema,
                    Name = "CustomerInput",
                    State = DescriptorState.Active
                },
                new DescriptorManifestEntry
                {
                    Ref = new DescriptorRef("capability", "cap_01", 1),
                    Kind = DescriptorKind.Capability,
                    Name = "crm.customer.create",
                    State = DescriptorState.Active
                }
            }
        };

        var json = DescriptorManifestSerializer.Serialize(manifest);
        var deserialized = DescriptorManifestSerializer.Deserialize(json);

        deserialized.Should().NotBeNull();
        deserialized!.PackageId.Should().Be("CrestCreates.CRM");
        deserialized.DescriptorEntries.Should().HaveCount(2);
        deserialized.DescriptorEntries
            .Should().ContainSingle(e => e.Name == "CustomerInput")
            .Which.Kind.Should().Be(DescriptorKind.Schema);
        deserialized.DescriptorEntries
            .Should().ContainSingle(e => e.Name == "crm.customer.create")
            .Which.Kind.Should().Be(DescriptorKind.Capability);
    }
}