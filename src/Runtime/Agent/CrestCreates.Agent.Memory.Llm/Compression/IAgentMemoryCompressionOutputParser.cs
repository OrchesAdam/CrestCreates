using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Compression;

public interface IAgentMemoryCompressionOutputParser
{
    AgentMemoryCompressionParseResult Parse(string json, IReadOnlyList<string> allowedSourceRefIds);
}

public sealed record AgentMemoryCompressionParseResult(
    bool IsValid,
    IReadOnlyList<AgentMemoryCompressedBlockDto> Blocks,
    IReadOnlyList<AgentMemoryDiagnostic> Diagnostics);

public sealed record AgentMemoryCompressedBlockDto(
    string? BlockId,
    string? Content,
    IReadOnlyList<string>? SourceRefIds,
    IReadOnlyList<string>? RedactionKinds);
