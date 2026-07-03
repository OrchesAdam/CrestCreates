using System.Text.Json;
using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Llm.Json;
using CrestCreates.Agent.Memory.Llm.Validation;
using CrestCreates.Core.Abstractions.Identity;

namespace CrestCreates.Agent.Memory.Llm.Extraction;

public sealed class JsonAgentMemoryExtractionOutputParser : IAgentMemoryExtractionOutputParser
{
    public AgentMemoryExtractionParseResult Parse(string json, IReadOnlyList<string> allowedSourceRefIds)
    {
        var diagnostics = new List<AgentMemoryDiagnostic>();

        if (string.IsNullOrWhiteSpace(json))
        {
            diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.ProviderReturnedEmptyOutput,
                "Provider returned empty output.",
                SeverityLevel.Error));
            return new AgentMemoryExtractionParseResult(false, Array.Empty<AgentMemoryCandidateDto>(), diagnostics);
        }

        AgentMemoryExtractionProviderOutputDto? output;
        try
        {
            output = JsonSerializer.Deserialize(json, AgentMemoryLlmJsonSerializerContext.Default.AgentMemoryExtractionProviderOutputDto);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.ParseFailed,
                $"Failed to parse extraction output: {ex.Message}",
                SeverityLevel.Error));
            return new AgentMemoryExtractionParseResult(false, Array.Empty<AgentMemoryCandidateDto>(), diagnostics);
        }

        if (output?.Candidates is null || output.Candidates.Count == 0)
        {
            diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.ProviderReturnedEmptyOutput,
                "Provider returned no candidates.",
                SeverityLevel.Warning));
            return new AgentMemoryExtractionParseResult(false, Array.Empty<AgentMemoryCandidateDto>(), diagnostics);
        }

        var allowedSet = allowedSourceRefIds as IReadOnlySet<string>
            ?? new HashSet<string>(allowedSourceRefIds);
        var validCandidates = new List<AgentMemoryCandidateDto>();
        var isValid = true;

        foreach (var candidate in output.Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.CandidateId) || string.IsNullOrWhiteSpace(candidate.Content))
            {
                diagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                    AgentMemoryLlmDiagnosticCodes.ParseFailed,
                    "Candidate missing CandidateId or Content.",
                    SeverityLevel.Warning));
                isValid = false;
                continue;
            }

            // Enforce non-authoritative output
            if (AgentMemoryLlmOutputValidators.EnforceNonAuthoritativeOutput(
                candidate.Status, candidate.IsAuthoritative, diagnostics))
            {
                isValid = false;
            }

            // Validate source refs
            if (candidate.SourceRefIds is { Count: > 0 })
            {
                AgentMemoryLlmOutputValidators.ValidateSourceRefs(candidate.SourceRefIds, allowedSet, diagnostics);

                if (candidate.SourceRefIds.Any(id => !allowedSet.Contains(id)))
                {
                    isValid = false;
                }
            }

            validCandidates.Add(candidate);
        }

        return new AgentMemoryExtractionParseResult(isValid, validCandidates, diagnostics);
    }
}

public sealed record AgentMemoryExtractionProviderOutputDto(
    IReadOnlyList<AgentMemoryCandidateDto>? Candidates);
