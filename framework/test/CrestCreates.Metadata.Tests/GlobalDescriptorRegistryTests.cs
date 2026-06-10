using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class GlobalDescriptorRegistryTests
{
    [Fact]
    public void Register_And_GetById()
    {
        var registry = new GlobalDescriptorRegistry();
        var schema = new SchemaDescriptor { Id = "schema_01", Name = "Test", Version = 1 };

        registry.Register(schema);
        var result = registry.GetById("schema_01");

        result.Should().NotBeNull();
        result!.Kind.Should().Be(DescriptorKind.Schema);
    }

    [Fact]
    public void GetByKind_Returns_Only_Matching()
    {
        var registry = new GlobalDescriptorRegistry();
        registry.Register(new SchemaDescriptor { Id = "schema_01", Name = "S1", Version = 1 });
        registry.Register(new CapabilityDescriptor
        {
            Id = "cap_01",
            Name = "test.op",
            Version = 1
        });

        var schemas = registry.GetByKind(DescriptorKind.Schema);

        schemas.Should().HaveCount(1);
    }

    [Fact]
    public void RegisterPackage_Groups_Descriptors()
    {
        var registry = new GlobalDescriptorRegistry();
        var descriptors = new List<IDescriptor>
        {
            new SchemaDescriptor { Id = "schema_01", Name = "S1", Version = 1 }
        };

        registry.RegisterPackage("CrestCreates.CRM", descriptors);

        var byPackage = registry.GetByPackage("CrestCreates.CRM");
        byPackage.Should().HaveCount(1);
    }
}