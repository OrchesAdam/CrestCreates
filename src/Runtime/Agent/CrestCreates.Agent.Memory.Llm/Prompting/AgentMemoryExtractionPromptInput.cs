using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Prompting;

public sealed record AgentMemoryExtractionPromptInput
{
    public required string TenantId { get; init; }
    public required IReadOnlyList<AgentCompressedContextBlock> Blocks { get; init; }
    public required int MaxCandidateCount { get; init; }
    public string? Purpose { get; init; }
}
