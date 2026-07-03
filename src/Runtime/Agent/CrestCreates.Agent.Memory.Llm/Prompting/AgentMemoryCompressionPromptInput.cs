using CrestCreates.Agent.Prompting.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Prompting;

public sealed record AgentMemoryCompressionPromptSource
{
    public required string SourceRefId { get; init; }
    public required string SanitizedContent { get; init; }
    public IReadOnlyList<string> RedactionKinds { get; init; } = Array.Empty<string>();
}

public sealed record AgentMemoryCompressionPromptInput
{
    public required string TenantId { get; init; }
    public required IReadOnlyList<AgentMemoryCompressionPromptSource> Sources { get; init; }
    public required int MaxOutputCharacters { get; init; }
    public required int MaxOutputBlocks { get; init; }
    public string? Purpose { get; init; }
}
