using CrestCreates.Agent.Memory.Abstractions;
using CrestCreates.Agent.Memory.Compression;
using CrestCreates.Agent.Memory.Llm.Model;
using CrestCreates.Agent.Memory.Llm.Prompting;
using CrestCreates.Agent.Memory.Llm.Validation;
using CrestCreates.Agent.Prompting.Abstractions;
using CrestCreates.Core.Abstractions.Identity;
using CrestCreates.Metadata.Abstractions.CanonicalHashing;

namespace CrestCreates.Agent.Memory.Llm.Compression;

/// <summary>
/// LLM-backed context compressor that sanitizes content before prompting,
/// falls back to deterministic compression on any failure.
/// </summary>
public sealed class LlmAgentContextCompressor : IAgentContextCompressor
{
    private readonly IAgentMemoryContentSanitizer _sanitizer;
    private readonly IAgentContextCompressor _fallback;
    private readonly IAgentMemoryCompressionPromptBuilder _promptBuilder;
    private readonly IAgentMemoryLlmModelClient _modelClient;
    private readonly IAgentMemoryCompressionOutputParser _parser;
    private readonly IAgentPromptEvidenceFactory _evidenceFactory;
    private readonly IAgentPromptHashService _hashService;
    private readonly AgentMemoryLlmAdapterOptions _options;

    public LlmAgentContextCompressor(
        IAgentMemoryContentSanitizer sanitizer,
        IAgentContextCompressor fallback,
        IAgentMemoryCompressionPromptBuilder promptBuilder,
        IAgentMemoryLlmModelClient modelClient,
        IAgentMemoryCompressionOutputParser parser,
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

    public async ValueTask<AgentCompressedContext> CompressConversationAsync(
        AgentConversationRecord conversation,
        CancellationToken cancellationToken = default)
    {
        var (promptInput, sourceRefMap) = BuildConversationPromptInput(conversation);
        return await CompressWithFallbackAsync(
            promptInput,
            sourceRefMap,
            () => _fallback.CompressConversationAsync(conversation, cancellationToken),
            AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicCompressor,
            cancellationToken);
    }

    public async ValueTask<AgentCompressedContext> CompressTaskAsync(
        AgentTaskRecord task,
        CancellationToken cancellationToken = default)
    {
        var (promptInput, sourceRefMap) = BuildTaskPromptInput(task);
        return await CompressWithFallbackAsync(
            promptInput,
            sourceRefMap,
            () => _fallback.CompressTaskAsync(task, cancellationToken),
            AgentMemoryLlmDiagnosticCodes.FallbackToDeterministicCompressor,
            cancellationToken);
    }

    private (AgentMemoryCompressionPromptInput PromptInput, Dictionary<string, IReadOnlyList<AgentContextSourceRef>> SourceRefMap) BuildConversationPromptInput(
        AgentConversationRecord conversation)
    {
        var sources = new List<AgentMemoryCompressionPromptSource>();
        var sourceRefMap = new Dictionary<string, IReadOnlyList<AgentContextSourceRef>>();
        var diagnostics = new List<AgentMemoryDiagnostic>();

        for (var i = 0; i < conversation.Turns.Count; i++)
        {
            var turn = conversation.Turns[i];
            var sanitized = _sanitizer.Sanitize(conversation.TenantId, turn.Content, turn.SourceRefs);
            var sourceRefId = $"{conversation.ConversationId}_{turn.TurnId}";

            // Skip rejected content (consistent with DefaultAgentContextCompressor behavior)
            if (sanitized.Rejected)
            {
                diagnostics.Add(new AgentMemoryDiagnostic
                {
                    Code = AgentMemoryDiagnosticCodes.ContentRejected,
                    Message = $"Skipped turn '{turn.TurnId}' because content was rejected after sanitization.",
                    Severity = SeverityLevel.Warning,
                    SourceRefs = turn.SourceRefs
                });
                continue;
            }

            sources.Add(new AgentMemoryCompressionPromptSource
            {
                SourceRefId = sourceRefId,
                SanitizedContent = sanitized.SanitizedContent,
                RedactionKinds = sanitized.RedactionKinds
            });

            // Preserve all original source refs — point back to ConversationTurn, not CompressedContextBlock
            sourceRefMap[sourceRefId] = turn.SourceRefs.Count > 0
                ? turn.SourceRefs.ToArray() // Preserve all original source refs (descriptor refs, causation, etc.)
                : [new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.ConversationTurn,
                    TenantId = conversation.TenantId,
                    SourceId = conversation.ConversationId,
                    RangeStart = i,
                    RangeEnd = i,
                    CanonicalContentHash = sanitized.CanonicalContentHash
                }];
        }

        var promptInput = new AgentMemoryCompressionPromptInput
        {
            TenantId = conversation.TenantId,
            Sources = sources.ToArray(),
            MaxOutputCharacters = _options.MaxCompressedBlockCharacters,
            MaxOutputBlocks = _options.MaxCompressedBlockCount,
            Purpose = null
        };

        // Store diagnostics on prompt input for later use — but prompt input doesn't have Diagnostics.
        // We'll carry them through the result instead.
        return (promptInput, sourceRefMap);
    }

    private (AgentMemoryCompressionPromptInput PromptInput, Dictionary<string, IReadOnlyList<AgentContextSourceRef>> SourceRefMap) BuildTaskPromptInput(
        AgentTaskRecord task)
    {
        var sources = new List<AgentMemoryCompressionPromptSource>();
        var sourceRefMap = new Dictionary<string, IReadOnlyList<AgentContextSourceRef>>();

        // Task summary
        var summaryContent = $"{task.Title}: {task.Summary ?? "No summary"}";
        var summarySanitized = _sanitizer.Sanitize(task.TenantId, summaryContent, Array.Empty<AgentContextSourceRef>());
        var summaryRefId = $"{task.TaskId}_summary";

        if (!summarySanitized.Rejected)
        {
            sources.Add(new AgentMemoryCompressionPromptSource
            {
                SourceRefId = summaryRefId,
                SanitizedContent = summarySanitized.SanitizedContent,
                RedactionKinds = summarySanitized.RedactionKinds
            });

            sourceRefMap[summaryRefId] = [new AgentContextSourceRef
            {
                SourceKind = AgentSourceKind.TaskRecord,
                TenantId = task.TenantId,
                SourceId = task.TaskId,
                CanonicalContentHash = summarySanitized.CanonicalContentHash
            }];
        }

        // Task events
        for (var j = 0; j < task.Events.Count; j++)
        {
            var evt = task.Events[j];
            var sanitized = _sanitizer.Sanitize(task.TenantId, evt.Content, evt.SourceRefs);
            var eventRefId = $"{task.TaskId}_{evt.EventId}";

            if (sanitized.Rejected)
                continue;

            sources.Add(new AgentMemoryCompressionPromptSource
            {
                SourceRefId = eventRefId,
                SanitizedContent = sanitized.SanitizedContent,
                RedactionKinds = sanitized.RedactionKinds
            });

            sourceRefMap[eventRefId] = evt.SourceRefs.Count > 0
                ? evt.SourceRefs.ToArray() // Preserve all original source refs
                : [new AgentContextSourceRef
                {
                    SourceKind = AgentSourceKind.TaskEvent,
                    TenantId = task.TenantId,
                    SourceId = task.TaskId,
                    RangeStart = j,
                    RangeEnd = j,
                    CanonicalContentHash = sanitized.CanonicalContentHash
                }];
        }

        var promptInput = new AgentMemoryCompressionPromptInput
        {
            TenantId = task.TenantId,
            Sources = sources.ToArray(),
            MaxOutputCharacters = _options.MaxCompressedBlockCharacters,
            MaxOutputBlocks = _options.MaxCompressedBlockCount,
            Purpose = null
        };

        return (promptInput, sourceRefMap);
    }

    private async ValueTask<AgentCompressedContext> CompressWithFallbackAsync(
        AgentMemoryCompressionPromptInput promptInput,
        Dictionary<string, IReadOnlyList<AgentContextSourceRef>> sourceRefMap,
        Func<ValueTask<AgentCompressedContext>> fallbackFunc,
        DiagnosticCode fallbackDiagnosticCode,
        CancellationToken cancellationToken)
    {
        if (!_options.EnableDeterministicFallback)
        {
            return await AttemptLlmCompressionAsync(promptInput, sourceRefMap, cancellationToken);
        }

        try
        {
            var result = await AttemptLlmCompressionAsync(promptInput, sourceRefMap, cancellationToken);
            if (result.Blocks.Count > 0)
                return result;

            // Empty result from LLM — fallback, carry LLM diagnostics
            var llmDiagnostics = result.Diagnostics;
            return await FallbackWithDiagnosticAsync(fallbackFunc, fallbackDiagnosticCode,
                "LLM compression returned empty result, falling back to deterministic compressor.",
                llmDiagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Cancellation must propagate — never swallow into fallback
        }
        catch (Exception ex)
        {
            var errorDiagnostic = AgentMemoryLlmDiagnostics.Create(
                AgentMemoryLlmDiagnosticCodes.CompressionParseError,
                $"LLM compression threw: {ex.Message}", SeverityLevel.Warning);

            return await FallbackWithDiagnosticAsync(fallbackFunc, fallbackDiagnosticCode,
                "LLM compression failed, falling back to deterministic compressor.",
                [errorDiagnostic]);
        }
    }

    private async ValueTask<AgentCompressedContext> AttemptLlmCompressionAsync(
        AgentMemoryCompressionPromptInput promptInput,
        Dictionary<string, IReadOnlyList<AgentContextSourceRef>> sourceRefMap,
        CancellationToken cancellationToken)
    {
        // Build prompt
        var promptText = _promptBuilder.Build(promptInput);

        // Create prompt input evidence
        var inputEvidence = _evidenceFactory.CreateInputEvidence(new AgentPromptEvidenceCreationRequest<AgentMemoryCompressionPromptInput>
        {
            Purpose = AgentPromptPurpose.MemoryCompression,
            TemplateId = _options.CompressionTemplateId,
            TemplateVersion = _options.CompressionTemplateVersion,
            ContractVersion = _options.PromptContractVersion,
            ModelProfileRef = _options.ModelProfileRef,
            ProviderProfileRef = _options.ProviderProfileRef,
            Payload = promptInput
        });

        var inputSummary = AgentPromptEvidenceSummaryFactory.CreateInputSummary(inputEvidence);

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
            var diagnostics = new List<AgentMemoryDiagnostic>();
            AgentMemoryLlmOutputValidators.AddProviderFailureDiagnostics(modelResponse, diagnostics);
            return new AgentCompressedContext
            {
                ContextId = promptInput.TenantId,
                TenantId = promptInput.TenantId,
                Blocks = [],
                Diagnostics = diagnostics.ToArray()
            };
        }

        // Parse response
        var allowedSourceRefIds = sourceRefMap.Keys.ToArray();
        var parseResult = _parser.Parse(modelResponse.ResponseText ?? "", allowedSourceRefIds);

        if (!parseResult.IsValid || parseResult.Blocks.Count == 0)
        {
            return new AgentCompressedContext
            {
                ContextId = promptInput.TenantId,
                TenantId = promptInput.TenantId,
                Blocks = [],
                Diagnostics = parseResult.Diagnostics
            };
        }

        // Enforce MaxCompressedBlockCount from options
        var blockDtos = parseResult.Blocks;
        if (blockDtos.Count > _options.MaxCompressedBlockCount)
            blockDtos = blockDtos.Take(_options.MaxCompressedBlockCount).ToList();

        // Convert parsed blocks to domain blocks
        var blocks = new List<AgentCompressedContextBlock>();
        foreach (var dto in blockDtos)
        {
            var content = dto.Content ?? "";
            var sanitized = _sanitizer.Sanitize(promptInput.TenantId, content, Array.Empty<AgentContextSourceRef>());

            // Use original source ref semantics — look up from map, flatten all refs per source
            var sourceRefs = (dto.SourceRefIds ?? [])
                .Where(id => sourceRefMap.ContainsKey(id))
                .SelectMany(id => sourceRefMap[id])
                .ToArray();

            blocks.Add(new AgentCompressedContextBlock
            {
                BlockId = dto.BlockId ?? Guid.NewGuid().ToString("N"),
                TenantId = promptInput.TenantId,
                Content = sanitized.SanitizedContent,
                CanonicalContentHash = sanitized.CanonicalContentHash,
                SourceRefs = sourceRefs,
                Diagnostics = sanitized.Diagnostics.ToArray()
            });
        }

        // Create output evidence with provider observation
        var providerObservation = new AgentPromptProviderObservation
        {
            ProviderName = modelResponse.ProviderName,
            ModelName = modelResponse.ModelName
        };

        var outputEvidence = _evidenceFactory.CreateOutputEvidence(
            new AgentPromptEvidenceCreationRequest<IReadOnlyList<AgentCompressedContextBlock>>
            {
                Purpose = AgentPromptPurpose.MemoryCompression,
                TemplateId = _options.CompressionTemplateId,
                TemplateVersion = _options.CompressionTemplateVersion,
                ContractVersion = _options.PromptContractVersion,
                ModelProfileRef = _options.ModelProfileRef,
                ProviderProfileRef = _options.ProviderProfileRef,
                Payload = blocks.ToArray()
            },
            inputEvidence.InputHash,
            providerObservation,
            artifactKind: CanonicalHashArtifactNames.AgentMemoryCompressedOutput,
            canonicalShapeVersion: AgentPromptCanonicalShapeVersions.MemoryCompressionOutput,
            purpose: CanonicalHashPurposeNames.SourceIdentity);

        var outputSummary = AgentPromptEvidenceSummaryFactory.CreateOutputSummary(outputEvidence);

        return new AgentCompressedContext
        {
            ContextId = promptInput.TenantId,
            TenantId = promptInput.TenantId,
            Blocks = blocks.ToArray(),
            Diagnostics = parseResult.Diagnostics,
            PromptOutputEvidence = outputSummary
        };
    }

    private async ValueTask<AgentCompressedContext> FallbackWithDiagnosticAsync(
        Func<ValueTask<AgentCompressedContext>> fallbackFunc,
        DiagnosticCode fallbackDiagnosticCode,
        string message,
        IReadOnlyList<AgentMemoryDiagnostic>? llmDiagnostics = null)
    {
        var fallbackResult = await fallbackFunc();
        var fallbackDiagnostic = AgentMemoryLlmDiagnostics.Create(fallbackDiagnosticCode, message, SeverityLevel.Warning);

        var allDiagnostics = llmDiagnostics is not null
            ? fallbackResult.Diagnostics.Concat(llmDiagnostics).Concat([fallbackDiagnostic]).ToArray()
            : fallbackResult.Diagnostics.Concat([fallbackDiagnostic]).ToArray();

        return fallbackResult with
        {
            Diagnostics = allDiagnostics
        };
    }
}
