using System.Collections.Frozen;
using System.Text.Json;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Mcp;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class DefaultMcpIdempotencyKeyBuilderTests
{
    [Fact]
    public void Key_is_stable_for_redelivery_and_changes_with_contract_or_host_boundaries()
    {
        var entry = Entry("tool-hash", "cap-hash");
        var sameCall = new McpToolCallContext(new McpToolHostContext("host", "test"), "logical-call", "request-1");
        var builder = new DefaultMcpIdempotencyKeyBuilder();

        var first = builder.Build(entry, sameCall);
        var redelivery = builder.Build(entry, sameCall with { RequestId = "request-2" });
        var changedHost = builder.Build(entry, sameCall with { Host = new McpToolHostContext("host:other", "test") });
        var changedContract = builder.Build(Entry("tool-hash-v2", "cap-hash"), sameCall);

        first.Should().Be(redelivery);
        first.Should().StartWith("mcp:v1:");
        first.Should().NotBe(changedHost);
        first.Should().NotBe(changedContract);
    }

    [Fact]
    public void Length_prefixing_prevents_separator_tuple_collisions()
    {
        var builder = new DefaultMcpIdempotencyKeyBuilder();
        var first = builder.Build(
            Entry("b:c", "cap"),
            new McpToolCallContext(new McpToolHostContext("a", "test"), "d", "request"));
        var second = builder.Build(
            Entry("c", "cap"),
            new McpToolCallContext(new McpToolHostContext("a:b", "test"), "d", "request"));

        first.Should().NotBe(second);
    }

    private static McpToolRuntimeEntry Entry(string toolHash, string capabilityHash)
    {
        using var schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
        var descriptor = new McpToolDescriptor
        {
            Id = "mcp-tool:test",
            Name = "Test",
            Version = 1,
            Capability = new McpCapabilityReference("test", 1),
            ToolName = "test",
            Description = "Test."
        };
        var capability = new CapabilityDescriptor { Id = "test", Name = "Test", Version = 1 };
        var binding = new McpToolBindingContract
        {
            ToolDescriptorId = descriptor.Id,
            ToolDescriptorVersion = 1,
            BindInputAsync = (json, info, ct) => ValueTask.FromResult<object?>(null),
            SerializeOutputAsync = (output, info, ct) => ValueTask.FromResult<JsonElement?>(null)
        };
        return new McpToolRuntimeEntry(
            descriptor,
            capability,
            null,
            null,
            new McpToolRuntimeBinding(binding, null, null),
            new McpToolContract("test", null, "Test.", schema.RootElement.Clone(), null, new McpToolAnnotations(false, null, null, null)),
            toolHash,
            capabilityHash,
            null,
            null);
    }
}
