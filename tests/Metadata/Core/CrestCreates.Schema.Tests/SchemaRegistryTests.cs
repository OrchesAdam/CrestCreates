using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.Registry;
using CrestCreates.Metadata.Registry;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Schema.Tests;

public class SchemaRegistryTests
{
    private class TestSchemaProvider : IDescriptorProvider<SchemaDescriptor>
    {
        private readonly List<SchemaDescriptor> _descriptors;
        public TestSchemaProvider(List<SchemaDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<SchemaDescriptor> GetDescriptors() => _descriptors;
    }

    private static SchemaRegistry CreateRegistry(params SchemaDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<SchemaDescriptor>(Array.Empty<IRegistryValidator<SchemaDescriptor>>());
        var registry = new SchemaRegistry(engine);
        registry.Build([new TestSchemaProvider(descriptors.ToList())]);
        return registry;
    }

    [Fact]
    public void Register_And_GetById_Returns_Descriptor()
    {
        var registry = CreateRegistry(new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1
        });

        var result = registry.GetById("schema_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("CustomerInput");
    }

    [Fact]
    public void GetByName_Returns_Active_Version()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor
            {
                Id = "schema_01",
                Name = "CustomerInput",
                Version = 1,
                State = DescriptorState.Active
            },
            new SchemaDescriptor
            {
                Id = "schema_02",
                Name = "CustomerInput",
                Version = 2,
                State = DescriptorState.Draft
            });

        var result = registry.GetByName("CustomerInput");

        result.Should().NotBeNull();
        result!.Version.Should().Be(1);
    }

    [Fact]
    public void GetActiveVersion_Returns_Highest_Active()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor
            {
                Id = "schema_01",
                Name = "CustomerInput",
                Version = 1,
                State = DescriptorState.Active
            },
            new SchemaDescriptor
            {
                Id = "schema_02",
                Name = "CustomerInput",
                Version = 2,
                State = DescriptorState.Active
            },
            new SchemaDescriptor
            {
                Id = "schema_03",
                Name = "CustomerInput",
                Version = 3,
                State = DescriptorState.Draft
            });

        var result = registry.GetActiveVersion("CustomerInput");

        result.Should().NotBeNull();
        result!.Version.Should().Be(2);
    }

    [Fact]
    public void GetDeprecatedVersions_Returns_Only_Deprecated()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor
            {
                Id = "schema_01",
                Name = "CustomerInput",
                Version = 1,
                State = DescriptorState.Deprecated
            },
            new SchemaDescriptor
            {
                Id = "schema_02",
                Name = "CustomerInput",
                Version = 2,
                State = DescriptorState.Active
            });

        var deprecated = registry.GetDeprecatedVersions("CustomerInput");

        deprecated.Should().HaveCount(1);
        deprecated[0].Version.Should().Be(1);
    }

    [Fact]
    public void GetById_Missing_Returns_Null()
    {
        var registry = CreateRegistry();

        var result = registry.GetById("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public void GetAll_Returns_All_Registered()
    {
        var registry = CreateRegistry(
            new SchemaDescriptor { Id = "schema_01", Name = "A", Version = 1 },
            new SchemaDescriptor { Id = "schema_02", Name = "B", Version = 1 });

        var all = registry.GetAll();

        all.Should().HaveCount(2);
    }

    [Fact]
    public void Build_Sets_State_To_Built()
    {
        var engine = new RegistryValidationEngine<SchemaDescriptor>(Array.Empty<IRegistryValidator<SchemaDescriptor>>());
        var registry = new SchemaRegistry(engine);
        var provider = new TestSchemaProvider([new SchemaDescriptor { Id = "s1", Name = "S", Version = 1 }]);

        registry.Build([provider]);

        registry.State.Should().Be(RegistryState.Built);
    }
}
