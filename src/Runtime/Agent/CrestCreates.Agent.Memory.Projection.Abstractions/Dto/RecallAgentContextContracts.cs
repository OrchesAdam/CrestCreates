using CrestCreates.Agent.Memory.Tools;

namespace CrestCreates.Agent.Memory.Projection.Abstractions;

/// <summary>
/// Protocol-neutral context recall input. Owned by Projection.Abstractions
/// because it is a capability contract, not MCP-specific.
/// </summary>
public sealed record RecallAgentContextInput
{
    public string ContextHandle { get; init; } = string.Empty;
    public int MaximumBlockCount { get; init; }
    public int CharacterBudget { get; init; }
    public int StartBlockIndex { get; init; }
    public int? EndBlockIndexExclusive { get; init; }
}

/// <summary>
/// Protocol-neutral context recall result.
/// Budget invariant: sum of Block.Content lengths &lt;= CharacterBudget.
/// Block count invariant: Blocks.Count &lt;= MaximumBlockCount.
/// No top-level SanitizedContent — content lives only in Blocks,
/// each carrying its own SourceGrants for ctx_expand follow-up.
/// </summary>
public sealed record RecallAgentContextResult
{
    public required AgentMemoryToolOperationStatus OperationStatus { get; init; }
    public bool WasTruncated { get; init; }
    public IReadOnlyList<AgentMemoryToolBlockDto> Blocks { get; init; } = Array.Empty<AgentMemoryToolBlockDto>();
    public int BlockCount { get; init; }
    public IReadOnlyList<AgentMemoryToolDiagnosticDto> Diagnostics { get; init; } = Array.Empty<AgentMemoryToolDiagnosticDto>();
}
