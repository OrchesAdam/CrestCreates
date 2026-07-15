using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Metadata.Mcp;

public sealed class McpToolDescriptor : IDescriptor, IVersionedDescriptor
{
    public string Namespace => "mcp-tool";

    public DescriptorKind Kind => DescriptorKind.McpTool;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public int Version { get; init; } = 1;

    public DescriptorState State { get; init; } = DescriptorState.Active;

    public string? SupersededById { get; init; }

    public required McpCapabilityReference Capability { get; init; }

    public string ToolName { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string Description { get; init; } = string.Empty;

    public McpToolAnnotationOverrides AnnotationOverrides { get; init; } = new();
}

public readonly record struct McpCapabilityReference(
    string Id,
    int Version,
    VersionSelectionMode SelectionMode = VersionSelectionMode.Exact,
    string? ExpectedContractHash = null);
