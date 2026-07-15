using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.CanonicalHashing;
using CrestCreates.Metadata.Mcp;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public sealed class McpToolCanonicalHashTests
{
    private readonly DefaultCanonicalHashComputer _computer = new();

    [Fact]
    public void Capability_selection_mode_changes_contract_hash()
    {
        var exact = Create(new McpCapabilityReference(
            "orders.get", 1, VersionSelectionMode.Exact));
        var latest = Create(new McpCapabilityReference(
            "orders.get", 0, VersionSelectionMode.Latest));

        _computer.ComputeContractHash(exact, CanonicalHashScope.InternalFull)
            .Should().NotBe(_computer.ComputeContractHash(latest, CanonicalHashScope.InternalFull));
    }

    [Fact]
    public void Expected_contract_hash_changes_contract_hash()
    {
        var first = Create(new McpCapabilityReference(
            "orders.get", 1, VersionSelectionMode.Exact, "hash-a"));
        var second = Create(new McpCapabilityReference(
            "orders.get", 1, VersionSelectionMode.Exact, "hash-b"));

        _computer.ComputeContractHash(first, CanonicalHashScope.InternalFull)
            .Should().NotBe(_computer.ComputeContractHash(second, CanonicalHashScope.InternalFull));
    }

    private static McpToolDescriptor Create(
        McpCapabilityReference capability) => new()
    {
        Id = "mcp-tool:orders.get",
        Name = "Get order",
        Version = 1,
        Capability = capability,
        ToolName = "orders.get",
        Description = "Gets one order."
    };
}
