using System.Text.Json;
using CrestCreates.Agent.Memory.Llm.Json;
using CrestCreates.Agent.Memory.Llm.Prompting;

namespace CrestCreates.Agent.Memory.Llm.Extraction;

public sealed class DefaultAgentMemoryExtractionPromptBuilder : IAgentMemoryExtractionPromptBuilder
{
    public string Build(AgentMemoryExtractionPromptInput input)
    {
        var blocksJson = JsonSerializer.Serialize(input.Blocks, AgentMemoryLlmJsonSerializerContext.Default.IReadOnlyListAgentCompressedContextBlock);
        var outputFormat = """{"candidates":[{"candidateId":"...","content":"...","sourceRefIds":["..."],"kind":"...","confidence":"Low|Medium|High","reasoning":"..."}]}""";

        return $"""
            You are a memory extraction assistant. Extract memorable facts from the following compressed context blocks.

            Instructions:
            - Use sanitized content only.
            - Do not invent facts.
            - Preserve sourceRefId exactly.
            - Preserve redaction markers.
            - Return JSON only with candidates[].
            - Never set status to Active or authoritative flags. All candidates are non-authoritative.

            Constraints:
            - Maximum candidates: {input.MaxCandidateCount}
            - Confidence levels: Low, Medium, High. Do not use numeric scores.

            Output format:
            {outputFormat}

            Compressed blocks (tenant: {input.TenantId}):
            {blocksJson}

            {(input.Purpose is not null ? "Purpose: " + input.Purpose : "")}
            """;
    }
}
