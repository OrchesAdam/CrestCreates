using System.Text.Json;
using CrestCreates.Agent.Memory.Llm.Json;
using CrestCreates.Agent.Memory.Llm.Prompting;

namespace CrestCreates.Agent.Memory.Llm.Compression;

public sealed class DefaultAgentMemoryCompressionPromptBuilder : IAgentMemoryCompressionPromptBuilder
{
    public string Build(AgentMemoryCompressionPromptInput input)
    {
        var sourcesJson = JsonSerializer.Serialize(input.Sources, AgentMemoryLlmJsonSerializerContext.Default.IReadOnlyListAgentMemoryCompressionPromptSource);
        var outputFormat = """{"blocks":[{"blockId":"...","content":"...","sourceRefIds":["..."],"redactionKinds":["..."]}]}""";

        return $"""
            You are a memory compression assistant. Compress the following conversation/task sources into a concise summary.

            Instructions:
            - Use sanitized content only.
            - Do not invent facts.
            - Preserve sourceRefId exactly.
            - Preserve redaction markers.
            - Return JSON only with blocks[].

            Constraints:
            - Maximum output characters: {input.MaxOutputCharacters}
            - Maximum blocks: {input.MaxOutputBlocks}

            Output format:
            {outputFormat}

            Sources (tenant: {input.TenantId}):
            {sourcesJson}

            {(input.Purpose is not null ? "Purpose: " + input.Purpose : "")}
            """;
    }
}
