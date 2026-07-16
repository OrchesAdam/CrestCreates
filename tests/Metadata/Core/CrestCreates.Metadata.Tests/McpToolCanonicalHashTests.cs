using System.Buffers;
using System.Text;
using System.Text.Json;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.CanonicalHashing.Generated;
using CrestCreates.Metadata.Mcp;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public sealed class McpToolCanonicalHashTests
{
    private const string BaselineContractHash = "f35555aa09fda88b2aeadea86b3d9d97a144509d7d9792f7d28126d7e5e11df1";
    private const string BaselineDefinitionHash = "baa9a3b0d3b77cd4eca8d076677594b57815992c88fb544fda0092f5d1183540";

    private readonly DefaultCanonicalHashComputer _computer = new();

    [Fact]
    public void Capability_selection_mode_changes_contract_hash()
    {
        var exact = Create(new CapabilityProjectionReference(
            "orders.get", 1, VersionSelectionMode.Exact));
        var latest = Create(new CapabilityProjectionReference(
            "orders.get", 0, VersionSelectionMode.Latest));

        _computer.ComputeContractHash(exact, CanonicalHashScope.InternalFull)
            .Should().NotBe(_computer.ComputeContractHash(latest, CanonicalHashScope.InternalFull));
    }

    [Fact]
    public void Expected_contract_hash_changes_contract_hash()
    {
        var first = Create(new CapabilityProjectionReference(
            "orders.get", 1, VersionSelectionMode.Exact, "hash-a"));
        var second = Create(new CapabilityProjectionReference(
            "orders.get", 1, VersionSelectionMode.Exact, "hash-b"));

        _computer.ComputeContractHash(first, CanonicalHashScope.InternalFull)
            .Should().NotBe(_computer.ComputeContractHash(second, CanonicalHashScope.InternalFull));
    }

    [Fact]
    public void Canonical_payload_and_hashes_match_phase_8e_golden_vectors()
    {
        var descriptor = Create(new CapabilityProjectionReference(
            "orders.get", 1, VersionSelectionMode.Exact));

        WriteCanonicalJson(CanonicalHashProjectionDispatcher.ToContractProjection(
                descriptor,
                CanonicalHashScope.InternalFull,
                CanonicalHashContractVersions.DescriptorHash,
                DefaultCanonicalHashComputer.AlgorithmVersion))
            .Should().Be(LoadGolden("mcp-tool-phase-8e-contract.json"));
        _computer.ComputeContractHash(descriptor, CanonicalHashScope.InternalFull).Value
            .Should().Be(BaselineContractHash);

        WriteCanonicalJson(CanonicalHashProjectionDispatcher.ToDefinitionProjection(
                descriptor,
                CanonicalHashScope.InternalFull,
                CanonicalHashContractVersions.DescriptorHash,
                DefaultCanonicalHashComputer.AlgorithmVersion))
            .Should().Be(LoadGolden("mcp-tool-phase-8e-definition.json"));
        _computer.ComputeDefinitionHash(descriptor, CanonicalHashScope.InternalFull).Value
            .Should().Be(BaselineDefinitionHash);
    }

    [Fact]
    public void Obsolete_wrapper_and_shared_reference_produce_identical_hashes()
    {
        var shared = new CapabilityProjectionReference(
            "orders.get", 1, VersionSelectionMode.Exact, "hash-a");
#pragma warning disable CS0618 // Compatibility behavior is the subject under test.
        var wrapped = new McpCapabilityReference(
            "orders.get", 1, VersionSelectionMode.Exact, "hash-a");
#pragma warning restore CS0618

        var sharedDescriptor = Create(shared);
        var wrappedDescriptor = Create(wrapped);

        _computer.ComputeContractHash(wrappedDescriptor, CanonicalHashScope.InternalFull)
            .Should().Be(_computer.ComputeContractHash(sharedDescriptor, CanonicalHashScope.InternalFull));
        _computer.ComputeDefinitionHash(wrappedDescriptor, CanonicalHashScope.InternalFull)
            .Should().Be(_computer.ComputeDefinitionHash(sharedDescriptor, CanonicalHashScope.InternalFull));
    }

    private static McpToolDescriptor Create(
        CapabilityProjectionReference capability) => new()
    {
        Id = "mcp-tool:orders.get",
        Name = "Get order",
        Version = 1,
        Capability = capability,
        ToolName = "orders.get",
        Description = "Gets one order."
    };

    private static string WriteCanonicalJson(CanonicalHashProjectionResult projection)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = true
        });
        projection.WriteCanonicalJson(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string LoadGolden(string fileName)
        => File.ReadAllText(Path.Combine(
                "DescriptorPackage",
                "CanonicalHashing",
                "GoldenFiles",
                fileName))
            .TrimEnd('\r', '\n');
}
