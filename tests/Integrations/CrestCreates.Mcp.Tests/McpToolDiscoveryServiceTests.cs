using System.Collections.Frozen;
using System.Text.Json;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Mcp;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpToolDiscoveryServiceTests
{
    [Fact]
    public async Task Discovery_is_ordinal_sorted_and_host_filtered()
    {
        var snapshot = Snapshot(Entry("zeta"), Entry("alpha"));
        var policy = new DelegatePolicy(context =>
            context.Host.ProfileName == "readonly" && context.Tool.ToolName == "zeta"
                ? McpToolExposureDecision.Deny
                : McpToolExposureDecision.Allow);
        var service = new McpToolDiscoveryService(snapshot, policy);

        var contracts = await service.ListAsync(new McpToolDiscoveryContext(
            new McpToolHostContext("host", "test", "readonly")));

        contracts.Select(contract => contract.Name).Should().Equal("alpha");
    }

    [Fact]
    public async Task Policy_failure_is_fail_closed_for_discovery()
    {
        var service = new McpToolDiscoveryService(
            Snapshot(Entry("orders.get")),
            new DelegatePolicy(_ => throw new InvalidOperationException("policy unavailable")));

        var action = async () => await service.ListAsync(new McpToolDiscoveryContext(
            new McpToolHostContext("host", "test")));

        var exception = await action.Should().ThrowAsync<McpToolProtocolException>();
        exception.Which.FailureKind.Should().Be(McpToolProtocolFailureKind.InternalServer);
        exception.Which.InternalCode.Should().Be("MCP_TOOL_EXPOSURE_POLICY_FAILURE");
    }

    [Fact]
    public async Task Invalid_host_context_fails_before_policy()
    {
        var calls = 0;
        var service = new McpToolDiscoveryService(
            Snapshot(Entry("orders.get")),
            new DelegatePolicy(_ => { calls++; return McpToolExposureDecision.Allow; }));

        var action = async () => await service.ListAsync(new McpToolDiscoveryContext(
            new McpToolHostContext("", "test")));

        await action.Should().ThrowAsync<ArgumentException>();
        calls.Should().Be(0);
    }

    private static McpToolRuntimeSnapshot Snapshot(params McpToolRuntimeEntry[] entries)
        => new(entries.ToFrozenDictionary(entry => entry.Descriptor.ToolName, StringComparer.Ordinal));

    private static McpToolRuntimeEntry Entry(string toolName)
    {
        using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
        var descriptor = new McpToolDescriptor
        {
            Id = "mcp-tool:" + toolName,
            Name = toolName,
            Version = 1,
            Capability = new McpCapabilityReference(toolName, 1),
            ToolName = toolName,
            Description = "Description"
        };
        var capability = new CapabilityDescriptor
        {
            Id = toolName,
            Name = toolName,
            Version = 1
        };
        var binding = new McpToolBindingContract
        {
            ToolDescriptorId = descriptor.Id,
            ToolDescriptorVersion = 1,
            BindInputAsync = (json, typeInfo, cancellationToken) => ValueTask.FromResult<object?>(null),
            SerializeOutputAsync = (output, typeInfo, cancellationToken) => ValueTask.FromResult<JsonElement?>(null)
        };
        var contract = new McpToolContract(
            toolName,
            null,
            descriptor.Description,
            document.RootElement.Clone(),
            null,
            new McpToolAnnotations(false, null, null, null));
        return new McpToolRuntimeEntry(
            descriptor,
            capability,
            null,
            null,
            new McpToolRuntimeBinding(binding, null, null),
            contract,
            "tool-hash",
            "capability-hash",
            null,
            null);
    }

    private sealed class DelegatePolicy(Func<McpToolExposureContext, McpToolExposureDecision> evaluate)
        : IMcpToolExposurePolicy
    {
        public ValueTask<McpToolExposureDecision> EvaluateAsync(
            McpToolExposureContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(evaluate(context));
    }
}
