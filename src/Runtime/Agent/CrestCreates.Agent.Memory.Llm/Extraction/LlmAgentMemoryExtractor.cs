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
        var inputEvidence = _evidenceFactory.CreateInputEvidence(new AgentPromptEvidenceCreationRequest<AgentMemoryExtractionPromptInput>
        {
            Purpose = AgentPromptPurpose.MemoryExtraction,
            TemplateId = AgentMemoryLlmContractVersions.ExtractionTemplateId,
            TemplateVersion = AgentMemoryLlmContractVersions.ExtractionTemplateVersion,
            ContractVersion = AgentMemoryLlmContractVersions.PromptContractVersion,
            ModelProfileRef = AgentMemoryLlmContractVersions.DefaultModelProfileRef,
            ProviderProfileRef = AgentMemoryLlmContractVersions.DefaultProviderProfileRef,
            Payload = promptInput
        });
        var inputSummary = AgentPromptEvidenceSummaryFactory.CreateInputSummary(inputEvidence);

        if (_options.EnableDeterministicFallback)
        {
            try
            {
                var result = await AttemptLlmExtractionAsync(promptInput, context, inputEvidence, inputSummary, cancellationToken);
                if (result.Candidates.Count > 0)
                    return result.Candidates;

                // Empty result from LLM — fallback, carry LLM diagnostics
                return await FallbackWithDiagnosticAsync(context, cancellationToken, inputSummary, result.Diagnostics);
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

                return await FallbackWithDiagnosticAsync(context, cancellationToken, inputSummary, [errorDiagnostic]);
            }
        }

        var noFallbackResult = await AttemptLlmExtractionAsync(promptInput, context, inputEvidence, inputSummary, cancellationToken);
        return noFallbackResult.Candidates;
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

    private async ValueTask<ExtractionAttemptResult> AttemptLlmExtractionAsync(
        AgentMemoryExtractionPromptInput promptInput,
        AgentCompressedContext context,
        AgentPromptInputEvidence<AgentMemoryExtractionPromptInput> inputEvidence,
        AgentPromptInputEvidenceSummary inputSummary,
        CancellationToken cancellationToken)
    {
        // Build prompt
        var promptText = _promptBuilder.Build(promptInput);

        // Call model
        var modelRequest = new AgentMemoryLlmModelRequest
        {
            PromptText = promptText,
            PromptInputEvidence = inputSummary
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

            return new ExtractionAttemptResult(
                fallbackResult.Select(c => c with
                {
                    SanitizationDiagnostics = c.SanitizationDiagnostics.Concat(allDiagnostics).ToArray()
                }).ToArray(),
                allDiagnostics);
        }

        // Parse response — collect allowed source ref IDs from context blocks
        var sourceRefMap = new Dictionary<string, IReadOnlyList<AgentContextSourceRef>>();
        foreach (var block in context.Blocks)
        {
            // Preserve all source refs from compressed context blocks
            sourceRefMap[block.BlockId] = block.SourceRefs.Count > 0
                ? block.SourceRefs
                : [new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.CompressedContextBlock,
                    TenantId = context.TenantId,
                    SourceId = block.BlockId
                }];
        }

        var allowedSourceRefIds = sourceRefMap.Keys.ToArray();
        var parseResult = _parser.Parse(modelResponse.ResponseText ?? "", allowedSourceRefIds);

        if (!parseResult.IsValid || parseResult.Candidates.Count == 0)
        {
            // Return empty with parse diagnostics — caller checks candidate count and falls back
            return new ExtractionAttemptResult([], parseResult.Diagnostics);
        }

        // Convert parsed candidates to domain candidates with lifecycle guards
        var truncationDiagnostics = new List<AgentMemoryDiagnostic>();
        var candidateDtos = parseResult.Candidates;
        if (candidateDtos.Count > _options.MaxCandidateCount)
        {
            truncationDiagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.CandidateCountTruncated,
                $"Truncated from {candidateDtos.Count} to {_options.MaxCandidateCount} candidates",
                SeverityLevel.Warning));
            candidateDtos = candidateDtos.Take(_options.MaxCandidateCount).ToList();
        }

        var candidates = new List<AgentMemoryCandidate>();
        foreach (var dto in candidateDtos)
        {
            // Enforce MaxCandidateCharacters from options
            var content = dto.Content ?? "";
            if (content.Length > _options.MaxCandidateCharacters)
            {
                truncationDiagnostics.Add(AgentMemoryLlmDiagnostics.Create(
                    AgentMemoryLlmDiagnosticCodes.CandidateTruncated,
                    $"Candidate content truncated from {content.Length} to {_options.MaxCandidateCharacters} characters",
                    SeverityLevel.Warning));
                content = content[.._options.MaxCandidateCharacters];
            }

            var sanitized = _sanitizer.Sanitize(context.TenantId, content, Array.Empty<AgentContextSourceRef>());

            // Use original source ref semantics from compressed context blocks — preserve all refs
            var sourceRefs = (dto.SourceRefIds ?? [])
                .Where(id => sourceRefMap.ContainsKey(id))
                .SelectMany(id => sourceRefMap[id])
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
                RedactionKinds = sanitized.RedactionKinds.ToArray(),
                SanitizationDiagnostics = sanitizationDiagnostics.ToArray()
            };

            candidates.Add(candidate);
        }

        // Step 1: Prompt output evidence — safe provider projection (AuditEvidence)
        var providerObservation = new AgentPromptProviderObservation
        {
            ProviderName = modelResponse.ProviderName,
            ModelName = modelResponse.ModelName
        };

        var promptOutputEvidence = _evidenceFactory.CreateOutputEvidence(
            new AgentPromptEvidenceCreationRequest<AgentMemoryLlmModelResponseEvidenceProjection>
            {
                Purpose = AgentPromptPurpose.MemoryExtraction,
                TemplateId = AgentMemoryLlmContractVersions.ExtractionTemplateId,
                TemplateVersion = AgentMemoryLlmContractVersions.ExtractionTemplateVersion,
                ContractVersion = AgentMemoryLlmContractVersions.PromptContractVersion,
                ModelProfileRef = AgentMemoryLlmContractVersions.DefaultModelProfileRef,
                ProviderProfileRef = AgentMemoryLlmContractVersions.DefaultProviderProfileRef,
                Payload = new AgentMemoryLlmModelResponseEvidenceProjection
                {
                    ProviderName = modelResponse.ProviderName,
                    ModelName = modelResponse.ModelName,
                    PromptInputHash = inputEvidence.InputHash.Value
                }
            },
            inputEvidence.InputHash,
            providerObservation);

        var promptOutputSummary = AgentPromptEvidenceSummaryFactory.CreateOutputSummary(promptOutputEvidence);

        // Step 2: Domain output hash — canonical candidates (SourceIdentity)
        var domainOutputHash = _hashService.ComputeOutputHash(
            new AgentPromptEvidenceCreationRequest<IReadOnlyList<AgentMemoryCandidate>>
            {
                Purpose = AgentPromptPurpose.MemoryExtraction,
                TemplateId = AgentMemoryLlmContractVersions.ExtractionTemplateId,
                TemplateVersion = AgentMemoryLlmContractVersions.ExtractionTemplateVersion,
                ContractVersion = AgentMemoryLlmContractVersions.PromptContractVersion,
                ModelProfileRef = AgentMemoryLlmContractVersions.DefaultModelProfileRef,
                ProviderProfileRef = AgentMemoryLlmContractVersions.DefaultProviderProfileRef,
                Payload = candidates.ToArray()
            },
            inputEvidence.InputHash,
            providerObservation,
            artifactKind: CanonicalHashArtifactNames.AgentMemoryCandidateOutput,
            canonicalShapeVersion: AgentPromptCanonicalShapeVersions.MemoryExtractionOutput,
            purpose: CanonicalHashPurposeNames.SourceIdentity);

        // Attach evidence summaries, domain output hash, and truncation diagnostics to each candidate
        return new ExtractionAttemptResult(
            candidates.Select(c => c with
            {
                PromptInputEvidence = inputSummary,
                PromptOutputEvidence = promptOutputSummary,
                CanonicalOutputHash = domainOutputHash,
                SanitizationDiagnostics = c.SanitizationDiagnostics.Concat(truncationDiagnostics).ToArray()
            }).ToArray(),
            []);
    }

    private sealed record ExtractionAttemptResult(
        IReadOnlyList<AgentMemoryCandidate> Candidates,
        IReadOnlyList<AgentMemoryDiagnostic> Diagnostics);

    private async ValueTask<IReadOnlyList<AgentMemoryCandidate>> FallbackWithDiagnosticAsync(
        AgentCompressedContext context,
        CancellationToken cancellationToken,
        AgentPromptInputEvidenceSummary? inputEvidenceSummary = null,
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
            SanitizationDiagnostics = c.SanitizationDiagnostics.Concat(extraDiagnostics).ToArray(),
            PromptInputEvidence = inputEvidenceSummary ?? c.PromptInputEvidence
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
