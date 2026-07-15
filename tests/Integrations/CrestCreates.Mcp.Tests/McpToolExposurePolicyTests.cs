using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Mcp;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpToolExposurePolicyTests
{
    [Fact]
    public async Task Default_policy_allows_explicit_snapshot_entry()
    {
        var descriptor = new McpToolDescriptor
        {
            Id = "mcp-tool:orders.get",
            Name = "Get order",
            Version = 1,
            Capability = new McpCapabilityReference("orders.get", 1),
            ToolName = "orders.get",
            Description = "Gets order."
        };
        var decision = await new DefaultMcpToolExposurePolicy().EvaluateAsync(
            new McpToolExposureContext(
                new McpToolHostContext("host", "test"),
                descriptor,
                new CapabilityDescriptor { Id = "orders.get", Name = "Get order", Version = 1 },
                McpToolExposurePhase.Discovery));

        decision.Should().BeSameAs(McpToolExposureDecision.Allow);
    }
}
