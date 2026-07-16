using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;
using CrestCreates.Metadata.Mcp;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpToolDescriptorValidatorTests
{
    private readonly McpToolDescriptorValidator _validator = new();

    [Theory]
    [InlineData(VersionSelectionMode.Exact, 0)]
    [InlineData(VersionSelectionMode.Latest, 1)]
    [InlineData(VersionSelectionMode.Compatible, 0)]
    public void Validate_invalid_capability_reference_fails(
        VersionSelectionMode selectionMode,
        int version)
    {
        var descriptor = ValidDescriptor(
            capability: new CapabilityProjectionReference(
                "orders.get",
                version,
                selectionMode));

        var report = _validator.Validate([descriptor]);

        report.Issues.Should().Contain(issue => issue.Code.HasValue && issue.Code.Value.Value == "MCP117");
    }

    [Fact]
    public void Validate_expected_contract_hash_fails()
    {
        var descriptor = ValidDescriptor(
            capability: new CapabilityProjectionReference(
                "orders.get",
                1,
                VersionSelectionMode.Exact,
                "sha256:expected"));

        var report = _validator.Validate([descriptor]);

        report.Issues.Should().Contain(issue => issue.Code.HasValue && issue.Code.Value.Value == "MCP119");
    }

    [Fact]
    public void Validate_duplicate_active_tool_name_uses_ordinal_comparison()
    {
        var first = ValidDescriptor(id: "mcp-tool:one", toolName: "orders.get");
        var duplicate = ValidDescriptor(id: "mcp-tool:two", toolName: "orders.get");
        var differentCase = ValidDescriptor(id: "mcp-tool:three", toolName: "Orders.get");

        var report = _validator.Validate([first, duplicate, differentCase]);

        report.Issues.Count(issue => issue.Code.HasValue && issue.Code.Value.Value == "MCP102").Should().Be(1);
    }

    [Fact]
    public void Validate_duplicate_descriptor_identity_fails_for_any_state()
    {
        var first = ValidDescriptor(id: "mcp-tool:orders.get", toolName: "orders.get");
        var duplicate = ValidDescriptor(id: "mcp-tool:orders.get", toolName: "orders.get.v2");

        var report = _validator.Validate([first, duplicate]);

        report.Issues.Should().Contain(issue => issue.Code.HasValue && issue.Code.Value.Value == "MCP101");
    }

    [Theory]
    [InlineData("", "Tool", "Description", "orders.get")]
    [InlineData("mcp-tool:orders.get", "", "Description", "orders.get")]
    [InlineData("mcp-tool:orders.get", "Tool", "", "orders.get")]
    [InlineData("mcp-tool:orders.get", "Tool", "Description", "invalid tool")]
    public void Validate_invalid_descriptor_shape_fails(
        string id,
        string name,
        string description,
        string toolName)
    {
        var descriptor = ValidDescriptor(
            id: id,
            name: name,
            description: description,
            toolName: toolName);

        var report = _validator.Validate([descriptor]);

        report.Issues.Should().Contain(issue => issue.Code.HasValue && issue.Code.Value.Value == "MCP116");
    }

    [Fact]
    public void Validate_null_annotation_overrides_fails()
    {
        var descriptor = new McpToolDescriptor
        {
            Id = "mcp-tool:orders.get",
            Name = "Get order",
            Version = 1,
            Capability = new CapabilityProjectionReference(
                "orders.get", 0, VersionSelectionMode.Latest),
            ToolName = "orders.get",
            Description = "Gets one order.",
            AnnotationOverrides = null!
        };

        var report = _validator.Validate([descriptor]);

        report.Issues.Should().Contain(issue => issue.Code.HasValue && issue.Code.Value.Value == "MCP116");
    }

    private static McpToolDescriptor ValidDescriptor(
        string id = "mcp-tool:orders.get",
        string name = "Get order MCP tool",
        string description = "Gets one order.",
        string toolName = "orders.get",
        CapabilityProjectionReference? capability = null) => new()
    {
        Id = id,
        Name = name,
        Version = 1,
        Capability = capability ?? new CapabilityProjectionReference(
                "orders.get",
                0,
                VersionSelectionMode.Latest),
        ToolName = toolName,
        Description = description
    };
}
