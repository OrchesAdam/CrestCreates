using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Json;
using CrestCreates.Agent.Memory.Llm.Validation;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Llm.Compression;

public sealed class JsonAgentMemoryCompressionOutputParser : IAgentMemoryCompressionOutputParser
{
    public AgentMemoryCompressionParseResult Parse(string json, IReadOnlyList<string> allowedSourceRefIds)
    {
        var diagnostics = new List<AgentMemoryDiagnostic>();

        if (string.IsNullOrWhiteSpace(json))
        {
            diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.ProviderReturnedEmptyOutput,
                "Provider returned empty output.",
                SeverityLevel.Error));
            return new AgentMemoryCompressionParseResult(false, Array.Empty<AgentMemoryCompressedBlockDto>(), diagnostics);
        }

        AgentMemoryCompressionProviderOutputDto? output;
        try
        {
            output = JsonSerializer.Deserialize(json, AgentMemoryLlmJsonSerializerContext.Default.AgentMemoryCompressionProviderOutputDto);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.ParseFailed,
                $"Failed to parse compression output: {ex.Message}",
                SeverityLevel.Error));
            return new AgentMemoryCompressionParseResult(false, Array.Empty<AgentMemoryCompressedBlockDto>(), diagnostics);
        }

        if (output?.Blocks is null || output.Blocks.Count == 0)
        {
            diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.ProviderReturnedEmptyOutput,
                "Provider returned no blocks.",
                SeverityLevel.Warning));
            return new AgentMemoryCompressionParseResult(false, Array.Empty<AgentMemoryCompressedBlockDto>(), diagnostics);
        }

        var allowedSet = allowedSourceRefIds as IReadOnlySet<string>
            ?? new HashSet<string>(allowedSourceRefIds);
        var validBlocks = new List<AgentMemoryCompressedBlockDto>();
        var isValid = true;

        foreach (var block in output.Blocks)
        {
            if (string.IsNullOrWhiteSpace(block.BlockId) || string.IsNullOrWhiteSpace(block.Content))
            {
                diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                    AgentMemoryLlmDiagnosticCodes.ParseFailed,
                    $"Block missing BlockId or Content.",
                    SeverityLevel.Warning));
                isValid = false;
                continue;
            }

            if (block.SourceRefIds is { Count: > 0 })
            {
                AgentMemoryLlmOutputValidators.ValidateSourceRefs(block.SourceRefIds, allowedSet, diagnostics);

                if (block.SourceRefIds.Any(id => !allowedSet.Contains(id)))
                {
                    isValid = false;
                }
            }

            validBlocks.Add(block);
        }

        return new AgentMemoryCompressionParseResult(isValid, validBlocks, diagnostics);
    }
}

public sealed record AgentMemoryCompressionProviderOutputDto(
    IReadOnlyList<AgentMemoryCompressedBlockDto>? Blocks);
