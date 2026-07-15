using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorRelationship;
using CrestCreates.Metadata.Mcp;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpToolRelationshipExtractorTests
{
    [Fact]
    public void Extract_creates_strong_capability_reference()
    {
        var descriptor = new McpToolDescriptor
        {
            Id = "mcp-tool:orders.get",
            Name = "Get order",
            Version = 2,
            Capability = new McpCapabilityReference(
                "orders.get", 3, VersionSelectionMode.Exact),
            ToolName = "orders.get",
            Description = "Gets one order."
        };

        var relationships = new McpToolRelationshipExtractor().Extract(descriptor);

        relationships.Should().ContainSingle();
        relationships[0].Should().Be(new DescriptorRelationship(
            new DescriptorRef("mcp-tool", "mcp-tool:orders.get", 2),
            new DescriptorRef("capability", "orders.get", 3),
            RelationshipKind.References,
            "Capability",
            nameof(McpToolDescriptor.Capability),
            RelationshipStrength.Strong,
            false));
    }
}
