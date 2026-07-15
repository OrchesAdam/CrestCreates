using System.Collections.Frozen;
using System.Text.Json.Serialization.Metadata;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Mcp;
using CrestCreates.Schema.Abstractions;

namespace CrestCreates.Mcp;

public sealed record McpToolRuntimeBinding(
    McpToolBindingContract Contract,
    JsonTypeInfo? InputTypeInfo,
    JsonTypeInfo? OutputTypeInfo);

public sealed record McpToolRuntimeEntry(
    McpToolDescriptor Descriptor,
    CapabilityDescriptor Capability,
    SchemaDescriptor? InputSchema,
    SchemaDescriptor? OutputSchema,
    McpToolRuntimeBinding Binding,
    McpToolContract DiscoveryContract,
    string ToolContractHash,
    string CapabilityContractHash,
    string? InputSchemaContractHash,
    string? OutputSchemaContractHash);

public sealed class McpToolRuntimeSnapshot
{
    public McpToolRuntimeSnapshot(FrozenDictionary<string, McpToolRuntimeEntry> entries)
        => Entries = entries;

    public FrozenDictionary<string, McpToolRuntimeEntry> Entries { get; }

    public McpToolRuntimeEntry? Find(string toolName)
        => Entries.TryGetValue(toolName, out var entry) ? entry : null;
}
