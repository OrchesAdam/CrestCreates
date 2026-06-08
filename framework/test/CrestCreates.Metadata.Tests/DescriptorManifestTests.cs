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
            Schemas = new[]
            {
                new DescriptorManifestEntry { Id = "schema_01", Name = "CustomerInput", Version = 1 }
            },
            Capabilities = new[]
            {
                new DescriptorManifestEntry { Id = "cap_01", Name = "crm.customer.create", Version = 1 }
            }
        };

        var json = DescriptorManifestSerializer.Serialize(manifest);
        var deserialized = DescriptorManifestSerializer.Deserialize(json);

        deserialized.Should().NotBeNull();
        deserialized!.PackageId.Should().Be("CrestCreates.CRM");
        deserialized.Schemas.Should().HaveCount(1);
        deserialized.Capabilities.Should().HaveCount(1);
    }
}