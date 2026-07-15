using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Mcp.Tests;

public sealed class McpToolBindingRegistryTests
{
    [Fact]
    public void Registry_is_keyed_by_descriptor_identity_and_preserves_reference_identity()
    {
        var id = "mcp-tool:test." + Guid.NewGuid().ToString("N");
        var contract = new McpToolBindingContract
        {
            ToolDescriptorId = id,
            ToolDescriptorVersion = 3,
            BindInputAsync = (json, typeInfo, cancellationToken) => ValueTask.FromResult<object?>(null),
            SerializeOutputAsync = (output, typeInfo, cancellationToken) => ValueTask.FromResult<JsonElement?>(null)
        };

        McpToolBindingRegistry.Register(contract);

        McpToolBindingRegistry.Find(id, 3).Should().BeSameAs(contract);
        McpToolBindingRegistry.GetRequired(id, 3).Should().BeSameAs(contract);
        McpToolBindingRegistry.Find(id, 2).Should().BeNull();
    }
}
