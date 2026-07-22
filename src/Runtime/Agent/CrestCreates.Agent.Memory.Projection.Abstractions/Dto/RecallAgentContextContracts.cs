using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Protocol-neutral context recall input. Owned by Projection.Abstractions
/// because it is a capability contract, not MCP-specific.
/// </summary>
public sealed record RecallAgentContextInput
{
    public string ContextHandle { get; init; } = string.Empty;
    public int MaximumCharacters { get; init; }
}

/// <summary>
/// Protocol-neutral context recall result.
/// </summary>
public sealed record RecallAgentContextResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public string? SanitizedContent { get; init; }
    public AgentMemoryToolCanonicalHashDto? CanonicalContentHash { get; init; }
    public bool WasTruncated { get; init; }
    public IReadOnlyList<AgentMemoryToolBlockDto> Blocks { get; init; } = Array.Empty<AgentMemoryToolBlockDto>();
    public int BlockCount { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}
