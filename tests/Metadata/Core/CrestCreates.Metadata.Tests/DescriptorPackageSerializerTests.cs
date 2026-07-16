using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorPackage;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.DescriptorPackage;
using CrestCreates.Metadata.DescriptorPackage.CanonicalHashing;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class DescriptorPackageSerializerTests
{
    private readonly ICanonicalHashComputer _hashComputer = new DefaultCanonicalHashComputer();
    private readonly IDescriptorStableHashBuilder _hashBuilder;
    private readonly IDescriptorPackageCanonicalHashComputer _packageHashComputer;
    private readonly IDescriptorPackageBuilder _builder;

    public DescriptorPackageSerializerTests()
    {
        _hashBuilder = new DescriptorStableHashBuilder(_hashComputer);
        _packageHashComputer = new DefaultDescriptorPackageCanonicalHashComputer(_hashComputer);
        _builder = new DefaultDescriptorPackageBuilder(_hashBuilder, _packageHashComputer);
    }

    private readonly IDescriptorPackageSerializer _serializer = new DescriptorPackageSerializer();

    private static SchemaDescriptor MakeSchema(string id, int version, string name)
    {
        return new SchemaDescriptor
        {
            Id = id, Version = version, Name = name,
            State = DescriptorState.Active
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
        deserialized.SnapshotData.Descriptors.Should().HaveCount(1);
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
        json.Should().Contain("\"severity\":\"Error\"");
        var deserialized = _serializer.Deserialize(json);

        deserialized!.Diagnostics.Should().Contain(d =>
            d.Code == DescriptorPackageDiagnosticCodes.DuplicateDescriptorRef);
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

        var entry = deserialized!.SnapshotData.Descriptors[0];
        entry.Ref.Id.Should().Be("s1");
        entry.ContractHash.Should().NotBeNullOrEmpty();
        entry.ContractHash.Should().HaveLength(64); // SHA-256 hex
        entry.DefinitionHash.Should().NotBeNullOrEmpty();
        entry.DefinitionHash.Should().HaveLength(64); // SHA-256 hex
    }

    [Fact]
    public void McpTool_package_entry_round_trips_identity_kind_and_hashes()
    {
        var descriptor = new McpToolDescriptor
        {
            Id = "mcp-tool:orders.get",
            Name = "Get order",
            Version = 1,
            Capability = new CapabilityProjectionReference(
                "orders.get", 1, VersionSelectionMode.Exact),
            ToolName = "orders.get",
            Description = "Gets one order."
        };
        var package = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "mcp.pkg",
            PackageVersion = "1.0.0",
            Descriptors = [descriptor]
        });

        var roundTripped = _serializer.Deserialize(_serializer.Serialize(package));

        var entry = roundTripped!.SnapshotData.Descriptors.Should().ContainSingle().Subject;
        entry.Ref.Should().Be(new DescriptorRef("mcp-tool", descriptor.Id, descriptor.Version));
        entry.Kind.Should().Be(DescriptorKind.McpTool);
        entry.ContractHash.Should().Be(package.SnapshotData.Descriptors[0].ContractHash);
        entry.DefinitionHash.Should().Be(package.SnapshotData.Descriptors[0].DefinitionHash);
    }

    [Fact]
    public void McpTool_package_json_matches_phase_8e_golden_bytes()
    {
        var descriptor = new McpToolDescriptor
        {
            Id = "mcp-tool:orders.get",
            Name = "Get order",
            Version = 1,
            Capability = new CapabilityProjectionReference(
                "orders.get", 1, VersionSelectionMode.Exact),
            ToolName = "orders.get",
            Description = "Gets one order."
        };
        var package = _builder.Build(new DescriptorPackageBuildRequest
        {
            PackageId = "mcp.pkg",
            PackageVersion = "1.0.0",
            CreatedAt = new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero),
            Descriptors = [descriptor]
        });

        var json = _serializer.Serialize(package);

        var golden = LoadGolden("mcp-tool-phase-8e-package.json");
        json.Should().Be(golden);
        _serializer.Serialize(_serializer.Deserialize(golden)).Should().Be(golden);
    }

    private static string LoadGolden(string fileName)
        => File.ReadAllText(Path.Combine(
                "DescriptorPackage",
                "CanonicalHashing",
                "GoldenFiles",
                fileName))
            .TrimEnd('\r', '\n');
}
