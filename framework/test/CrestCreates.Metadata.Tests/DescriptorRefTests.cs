using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorRefTests
{
    [Fact]
    public void DescriptorRef_Records_Id()
    {
        var ref1 = new DescriptorRef<SchemaDescriptor>("schema_01");
        var ref2 = new DescriptorRef<SchemaDescriptor>("schema_01");

        ref1.Id.Should().Be("schema_01");
        ref1.Should().Be(ref2);
    }

    [Fact]
    public void VersionedDescriptorRef_Records_Id_And_Version()
    {
        var vref = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        vref.Id.Should().Be("schema_01");
        vref.Version.Should().Be(3);
    }

    [Fact]
    public void VersionedDescriptorRef_Default_SelectionMode_Is_Exact()
    {
        var vref = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        vref.SelectionMode.Should().Be(VersionSelectionMode.Exact);
    }

    [Fact]
    public void VersionedDescriptorRef_With_Same_Id_Version_Are_Equal()
    {
        var vref1 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);
        var vref2 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);

        vref1.Should().Be(vref2);
    }

    [Fact]
    public void VersionedDescriptorRef_With_Different_Version_Are_Not_Equal()
    {
        var vref1 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 3);
        var vref2 = new VersionedDescriptorRef<SchemaDescriptor>("schema_01", 4);

        vref1.Should().NotBe(vref2);
    }
}
