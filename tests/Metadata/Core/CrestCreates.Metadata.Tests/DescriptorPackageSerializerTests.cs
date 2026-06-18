using CrestCreates.Metadata.Abstractions;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackageSerializerTests
{
    private readonly IDescriptorPackageBuilder _builder = new DefaultDescriptorPackageBuilder();
    private readonly IDescriptorPackageSerializer _serializer = new DescriptorPackageSerializer();

    private static SchemaDescriptor MakeSchema(string id, int version, string name)
    {
        return new SchemaDescriptor
        {
            Id = id, Version = version, Name = name, State = DescriptorState.Active,
            ContractHash = $"contract_{id}_v{version}",
            DefinitionHash = $"def_{id}_v{version}"
        };
    }

    [Fact]
    public void Serialize_Deserialize_Package_RoundTrip()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        var json = _serializer.Serialize(pkg);
        var deserialized = _serializer.Deserialize(json);

        deserialized.Should().NotBeNull();
        deserialized!.PackageId.Should().Be("test.pkg");
        deserialized.Manifest.DescriptorEntries.Should().HaveCount(1);
        deserialized.ContentHash.Should().Be(pkg.ContentHash);
        deserialized.Snapshot.Descriptors.Should().HaveCount(1);
    }

    [Fact]
    public void Serialize_Deserialize_PackageWithEvidence()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        var json = _serializer.Serialize(pkg);
        var deserialized = _serializer.Deserialize(json);

        deserialized!.Evidence.TopologyNodeCount.Should().Be(0);
    }

    [Fact]
    public void Serialize_Deserialize_PackageWithDiagnostics()
    {
        var desc1 = MakeSchema("s1", 1, "S1");
        var desc2 = MakeSchema("s1", 1, "S1"); // duplicate

        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { desc1, desc2 }
        });

        var json = _serializer.Serialize(pkg);
        var deserialized = _serializer.Deserialize(json);

        deserialized!.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorPackageDiagnosticCode.DuplicateDescriptorRef);
    }

    [Fact]
    public void DeserializedPackage_ContainsManifestEntryHashes()
    {
        var pkg = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "test.pkg", PackageVersion = "1.0.0",
            Descriptors = new IDescriptor[] { MakeSchema("s1", 1, "S1") }
        });

        var json = _serializer.Serialize(pkg);
        var deserialized = _serializer.Deserialize(json);

        var entry = deserialized!.Snapshot.Descriptors[0];
        entry.Ref.Id.Should().Be("s1");
        entry.ContractHash.Should().Be("contract_s1_v1");
        entry.DefinitionHash.Should().Be("def_s1_v1");
    }
}
