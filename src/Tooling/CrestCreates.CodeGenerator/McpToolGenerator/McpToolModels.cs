using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace CrestCreates.CodeGenerator.McpToolGenerator;

internal sealed class McpToolSpecModel
{
    public string SpecName { get; set; } = string.Empty;
    public string CapabilityId { get; set; } = string.Empty;
    public int CapabilityVersion { get; set; }
    public string DescriptorId { get; set; } = string.Empty;
    public int DescriptorVersion { get; set; }
    public string ToolName { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? InputType { get; set; }
    public string? OutputType { get; set; }
    public int DestructiveHint { get; set; }
    public int IdempotentHint { get; set; }
    public int OpenWorldHint { get; set; }
}

internal sealed class McpToolContainerModel
{
    public string Namespace { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public ImmutableArray<McpToolSpecModel> Specs { get; set; }
    public ImmutableArray<Diagnostic> Diagnostics { get; set; }
}
