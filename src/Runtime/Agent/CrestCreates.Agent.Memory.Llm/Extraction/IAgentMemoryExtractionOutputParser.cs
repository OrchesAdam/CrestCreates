using CrestCreates.Agent.Memory.Abstractions;

namespace CrestCreates.Agent.Memory.Llm.Extraction;

public interface IAgentMemoryExtractionOutputParser
{
    AgentMemoryExtractionParseResult Parse(string json, IReadOnlyList<string> allowedSourceRefIds);
}

public sealed record AgentMemoryExtractionParseResult(
    bool IsValid,
    IReadOnlyList<AgentMemoryCandidateDto> Candidates,
    IReadOnlyList<AgentMemoryDiagnostic> Diagnostics);

public sealed record AgentMemoryCandidateDto(
    string? CandidateId,
    string? Content,
    IReadOnlyList<string>? SourceRefIds,
    string? Kind,
    string? Confidence,
    string? Reasoning,
    string? Status,
    bool? IsAuthoritative);
