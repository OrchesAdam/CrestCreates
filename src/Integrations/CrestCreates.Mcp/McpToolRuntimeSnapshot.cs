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

/// Publishes the validated runtime snapshot exactly once after all dependency
/// registries have been built. Consumers must not be able to materialize an
/// empty pre-bootstrap snapshot.
public sealed class McpToolRuntimeSnapshotProvider
{
    private McpToolRuntimeSnapshot? _snapshot;

    public McpToolRuntimeSnapshotProvider()
    {
    }

    public McpToolRuntimeSnapshotProvider(McpToolRuntimeSnapshot snapshot)
        => Publish(snapshot);

    public void Publish(McpToolRuntimeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Interlocked.CompareExchange(ref _snapshot, snapshot, null) is not null)
            throw new InvalidOperationException("MCP snapshot is already published.");
    }

    public McpToolRuntimeSnapshot GetRequired()
        => Volatile.Read(ref _snapshot)
            ?? throw new McpToolConfigurationException(
                "MCP_SNAPSHOT_NOT_PUBLISHED",
                "MCP runtime snapshot has not been published.");
}
