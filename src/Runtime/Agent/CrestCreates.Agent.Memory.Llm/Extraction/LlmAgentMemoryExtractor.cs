using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Extraction;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Prompting;
using CrestCreates.Agent.Memory.Llm.Validation;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Llm.Extraction;

/// <summary>
/// LLM-backed memory extractor that sanitizes content before prompting,
/// enforces candidate lifecycle guards (no Active/Authoritative from LLM),
/// and falls back to deterministic extraction on any failure.
/// </summary>
public sealed class LlmAgentMemoryExtractor : IAgentMemoryExtractor
{
    private readonly IAgentMemoryContentSanitizer _sanitizer;
    private readonly IAgentMemoryExtractor _fallback;
    private readonly IAgentMemoryExtractionPromptBuilder _promptBuilder;
    private readonly IAgentMemoryLlmModelClient _modelClient;
    private readonly IAgentMemoryExtractionOutputParser _parser;
    private readonly IAgentPromptEvidenceFactory _evidenceFactory;
    private readonly IAgentPromptHashService _hashService;
    private readonly AgentMemoryLlmAdapterOptions _options;

    public LlmAgentMemoryExtractor(
        IAgentMemoryContentSanitizer sanitizer,
        IAgentMemoryExtractor fallback,
        IAgentMemoryExtractionPromptBuilder promptBuilder,
        IAgentMemoryLlmModelClient modelClient,
        IAgentMemoryExtractionOutputParser parser,
        IAgentPromptEvidenceFactory evidenceFactory,
        IAgentPromptHashService hashService,
        AgentMemoryLlmAdapterOptions options)
    {
        _sanitizer = sanitizer;
        _fallback = fallback;
        _promptBuilder = promptBuilder;
        _modelClient = modelClient;
        _parser = parser;
        _evidenceFactory = evidenceFactory;
        _hashService = hashService;
        _options = options;
    }

    public async ValueTask<IReadOnlyList<AgentMemoryCandidate>> ExtractCandidatesAsync(
        AgentCompressedContext context,
        CancellationToken cancellationToken = default)
    {
        var promptInput = BuildExtractionPromptInput(context);

        if (_options.EnableDeterministicFallback)
        {
            try
            {
                var result = await AttemptLlmExtractionAsync(promptInput, context, cancellationToken);
                if (result.Count > 0)
                    return result;

                // Empty result from LLM — fallback, carry LLM diagnostics from context
                return await FallbackWithDiagnosticAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Cancellation must propagate — never swallow into fallback
            }
            catch (Exception ex)
            {
                var errorDiagnostic = AgentMemoryLlmDiagnostics.Create(
                    AgentMemoryLlmDiagnosticCodes.ExtractionParseError,
                    $"LLM extraction threw: {ex.Message}", SeverityLevel.Warning);

                return await FallbackWithDiagnosticAsync(context, cancellationToken, [errorDiagnostic]);
            }
        }

        return await AttemptLlmExtractionAsync(promptInput, context, cancellationToken);
    }

    private AgentMemoryExtractionPromptInput BuildExtractionPromptInput(AgentCompressedContext context)
    {
        return new AgentMemoryExtractionPromptInput
        {
            TenantId = context.TenantId,
            Blocks = context.Blocks,
            MaxCandidateCount = _options.MaxCandidateCount
        };
    }

    private async ValueTask<IReadOnlyList<AgentMemoryCandidate>> AttemptLlmExtractionAsync(
        AgentMemoryExtractionPromptInput promptInput,
        AgentCompressedContext context,
        CancellationToken cancellationToken)
    {
        // Build prompt
        var promptText = _promptBuilder.Build(promptInput);

        // Create prompt input evidence
        var inputEvidence = _evidenceFactory.CreateInputEvidence(new AgentPromptEvidenceCreationRequest<AgentMemoryExtractionPromptInput>
        {
            Purpose = AgentPromptPurpose.MemoryExtraction,
            TemplateId = _options.ExtractionTemplateId,
            TemplateVersion = _options.ExtractionTemplateVersion,
            ContractVersion = _options.PromptContractVersion,
            ModelProfileRef = _options.ModelProfileRef,
            ProviderProfileRef = _options.ProviderProfileRef,
            Payload = promptInput
        });

        // Call model
        var modelRequest = new AgentMemoryLlmModelRequest
        {
            PromptText = promptText,
            PromptInputEvidence = AgentPromptEvidenceSummaryFactory.CreateInputSummary(inputEvidence)
        };

        var modelResponse = await _modelClient.CompleteAsync(modelRequest, cancellationToken);

        // Handle provider failure
        if (modelResponse.FailureKind is not null)
        {
            var providerDiagnostics = new List<AgentMemoryDiagnostic>();
            AgentMemoryLlmOutputValidators.AddProviderFailureDiagnostics(modelResponse, providerDiagnostics);

            // Provider failure triggers fallback with provider diagnostics attached
            var fallbackResult = await _fallback.ExtractCandidatesAsync(context, cancellationToken);
            var fallbackDiagnostic = AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicExtractor,
                "LLM extraction failed due to provider error, falling back to deterministic extractor.",
                SeverityLevel.Warning);
            var allDiagnostics = providerDiagnostics.Concat([fallbackDiagnostic]).ToArray();

            return fallbackResult.Select(c => c with
            {
                SanitizationDiagnostics = c.SanitizationDiagnostics.Concat(allDiagnostics).ToArray()
            }).ToArray();
        }

        // Parse response — collect allowed source ref IDs from context blocks
        var sourceRefMap = new Dictionary<string, AgentContextSourceRef>();
        foreach (var block in context.Blocks)
        {
            // Use block's existing source refs (which should have correct SourceKind from compression)
            sourceRefMap[block.BlockId] = block.SourceRefs.Count > 0
                ? block.SourceRefs[0]
                : new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.CompressedContextBlock,
                    TenantId = context.TenantId,
                    SourceId = block.BlockId
                };
        }

        var allowedSourceRefIds = sourceRefMap.Keys.ToArray();
        var parseResult = _parser.Parse(modelResponse.ResponseText ?? "", allowedSourceRefIds);

        if (!parseResult.IsValid || parseResult.Candidates.Count == 0)
        {
            // Return empty with parse diagnostics attached — caller (CompressWithFallbackAsync) will fallback
            // But we need to carry these diagnostics. Since we can't attach them to an empty list,
            // we throw so the catch block handles fallback with diagnostics.
            if (parseResult.Diagnostics.Count > 0)
            {
                throw new InvalidOperationException(
                    $"LLM extraction parse failed: {string.Join("; ", parseResult.Diagnostics.Select(d => d.Message))}");
            }
            return [];
        }

        // Convert parsed candidates to domain candidates with lifecycle guards
        var candidates = new List<AgentMemoryCandidate>();
        foreach (var dto in parseResult.Candidates)
        {
            // Enforce MaxCandidateCharacters from options
            var content = dto.Content ?? "";
            if (content.Length > _options.MaxCandidateCharacters)
                content = content[.._options.MaxCandidateCharacters];

            var sanitized = _sanitizer.Sanitize(context.TenantId, content, Array.Empty<AgentContextSourceRef>());

            // Use original source ref semantics from compressed context blocks
            var sourceRefs = (dto.SourceRefIds ?? [])
                .Where(id => sourceRefMap.ContainsKey(id))
                .Select(id => sourceRefMap[id])
                .ToArray();

            var kind = ParseMemoryKind(dto.Kind);
            var confidence = ParseConfidence(dto.Confidence);

            var sanitizationDiagnostics = new List<AgentMemoryDiagnostic>(sanitized.Diagnostics);
            confidence = AgentMemoryLlmOutputValidators.CapConfidence(
                confidence, _options.MaxCandidateConfidence, sanitizationDiagnostics);

            var candidate = new AgentMemoryCandidate
            {
                CandidateId = dto.CandidateId ?? Guid.NewGuid().ToString("N"),
                TenantId = context.TenantId,
                Kind = kind,
                Content = sanitized.SanitizedContent,
                CanonicalContentHash = sanitized.CanonicalContentHash,
                Confidence = confidence,
                SourceRefs = sourceRefs,
                Status = AgentMemoryStatus.Candidate, // Always Candidate — never Active from LLM
                SanitizationDiagnostics = sanitizationDiagnostics.ToArray()
            };

            candidates.Add(candidate);
        }

        // Create output evidence with provider observation
        var providerObservation = new AgentPromptProviderObservation
        {
            ProviderName = modelResponse.ProviderName,
            ModelName = modelResponse.ModelName
        };

        var outputEvidence = _evidenceFactory.CreateOutputEvidence(
            new AgentPromptEvidenceCreationRequest<IReadOnlyList<AgentMemoryCandidate>>
            {
                Purpose = AgentPromptPurpose.MemoryExtraction,
                TemplateId = _options.ExtractionTemplateId,
                TemplateVersion = _options.ExtractionTemplateVersion,
                ContractVersion = _options.PromptContractVersion,
                ModelProfileRef = _options.ModelProfileRef,
                ProviderProfileRef = _options.ProviderProfileRef,
                Payload = candidates.ToArray()
            },
            inputEvidence.InputHash,
            providerObservation,
            artifactKind: CanonicalHashArtifactNames.AgentMemoryCandidateOutput,
            canonicalShapeVersion: AgentPromptCanonicalShapeVersions.MemoryExtractionOutput,
            purpose: CanonicalHashPurposeNames.SourceIdentity);

        var outputSummary = AgentPromptEvidenceSummaryFactory.CreateOutputSummary(outputEvidence);

        // Attach output evidence summary to each candidate
        return candidates.Select(c => c with
        {
            PromptOutputEvidence = outputSummary
        }).ToArray();
    }

    private async ValueTask<IReadOnlyList<AgentMemoryCandidate>> FallbackWithDiagnosticAsync(
        AgentCompressedContext context,
        CancellationToken cancellationToken,
        IReadOnlyList<AgentMemoryDiagnostic>? llmDiagnostics = null)
    {
        var fallbackResult = await _fallback.ExtractCandidatesAsync(context, cancellationToken);
        var fallbackDiagnostic = AgentMemoryLlmDiagnostics.Create(
            AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicExtractor,
            "LLM extraction failed, falling back to deterministic extractor.",
            SeverityLevel.Warning);

        var extraDiagnostics = llmDiagnostics is not null
            ? llmDiagnostics.Concat([fallbackDiagnostic]).ToArray()
            : [fallbackDiagnostic];

        return fallbackResult.Select(c => c with
        {
            SanitizationDiagnostics = c.SanitizationDiagnostics.Concat(extraDiagnostics).ToArray()
        }).ToArray();
    }

    private static AgentMemoryKind ParseMemoryKind(string? kind)
    {
        return kind?.ToLowerInvariant() switch
        {
            "preference" => AgentMemoryKind.Preference,
            "projectfact" or "project-fact" or "project_fact" => AgentMemoryKind.ProjectFact,
            "decision" => AgentMemoryKind.Decision,
            "constraint" => AgentMemoryKind.Constraint,
            "workflowhint" or "workflow-hint" or "workflow_hint" => AgentMemoryKind.WorkflowHint,
            "risk" => AgentMemoryKind.Risk,
            _ => AgentMemoryKind.ProjectFact // Default to safest kind
        };
    }

    private static AgentMemoryConfidence ParseConfidence(string? confidence)
    {
        return confidence?.ToLowerInvariant() switch
        {
            "low" => AgentMemoryConfidence.Low,
            "medium" => AgentMemoryConfidence.Medium,
            "high" => AgentMemoryConfidence.High,
            _ => AgentMemoryConfidence.Unknown
        };
    }
}
