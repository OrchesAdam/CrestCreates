using CrestCreates.Metadata.Abstractions;
using CrestCreates.Metadata.Abstractions.DescriptorCapability;

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

    public required CapabilityProjectionReference Capability { get; init; }

    public string ToolName { get; init; } = string.Empty;

    public string? Title { get; init; }

    public string Description { get; init; } = string.Empty;

    public McpToolAnnotationOverrides AnnotationOverrides { get; init; } = new();
}

/// <summary>
/// Source-compatibility wrapper for the shared Capability projection reference.
/// It does not preserve the former binary signature of <see cref="McpToolDescriptor.Capability"/>.
/// </summary>
[Obsolete("Use CapabilityProjectionReference. This source compatibility wrapper will be removed after the Phase 8f migration window.")]
public readonly record struct McpCapabilityReference(
    string Id,
    int Version,
    VersionSelectionMode SelectionMode = VersionSelectionMode.Exact,
    string? ExpectedContractHash = null)
{
    public static implicit operator CapabilityProjectionReference(McpCapabilityReference reference)
        => new(
            reference.Id,
            reference.Version,
            reference.SelectionMode,
            reference.ExpectedContractHash);

    public static implicit operator McpCapabilityReference(CapabilityProjectionReference reference)
        => new(
            reference.Id,
            reference.Version,
            reference.SelectionMode,
            reference.ExpectedContractHash);
}
