using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Schema.Tests;

public class SchemaRegistryTests
{
    [Fact]
    public void Register_And_GetById_Returns_Descriptor()
    {
        var registry = new SchemaRegistry();
        var descriptor = new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1
        };

        registry.Register(descriptor);
        var result = registry.GetById("schema_01");

        result.Should().NotBeNull();
        result!.Name.Should().Be("CustomerInput");
    }

    [Fact]
    public void GetByName_Returns_Active_Version()
    {
        var registry = new SchemaRegistry();
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            State = DescriptorState.Active
        });
        registry.Register(new SchemaDescriptor
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
        var registry = new SchemaRegistry();
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            State = DescriptorState.Active
        });
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_02",
            Name = "CustomerInput",
            Version = 2,
            State = DescriptorState.Active
        });
        registry.Register(new SchemaDescriptor
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
        var registry = new SchemaRegistry();
        registry.Register(new SchemaDescriptor
        {
            Id = "schema_01",
            Name = "CustomerInput",
            Version = 1,
            State = DescriptorState.Deprecated
        });
        registry.Register(new SchemaDescriptor
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
        var registry = new SchemaRegistry();

        var result = registry.GetById("nonexistent");

        result.Should().BeNull();
    }

    [Fact]
    public void GetAll_Returns_All_Registered()
    {
        var registry = new SchemaRegistry();
        registry.Register(new SchemaDescriptor { Id = "schema_01", Name = "A", Version = 1 });
        registry.Register(new SchemaDescriptor { Id = "schema_02", Name = "B", Version = 1 });

        var all = registry.GetAll();

        all.Should().HaveCount(2);
    }
}
