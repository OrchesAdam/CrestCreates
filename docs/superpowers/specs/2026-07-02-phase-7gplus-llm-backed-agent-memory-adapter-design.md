# Phase 7g+ - LLM-backed Agent Memory Adapter

> Date: 2026-07-02  
> Status: Approved for implementation planning  
> Issue: #49  
> Builds on: #43 Agent Memory first closure, Phase 7g LLM-backed Descriptor Authoring Adapter, Phase 7h Agent Prompt Evidence Contract

## 1. Goal

Add an optional, provider-agnostic LLM adapter around Agent Memory compression
and memory-candidate extraction.

The deterministic Agent Memory runtime remains the official default lifecycle
path for memory state transitions:

```text
AddAgentMemoryRuntime()
  -> DefaultAgentContextCompressor
  -> DefaultAgentMemoryExtractor
  -> DefaultAgentMemoryPromotionService
  -> DefaultAgentMemoryRetriever
```

Phase 7g+ may improve the quality of compressed context blocks and extracted
memory candidates, but it must not replace the #43 memory lifecycle. The LLM
adapter is a non-authoritative suggestion layer. It produces compressed context
and candidate records only; promotion, recall, and memory lifecycle state remain
owned by the existing deterministic services.

## 2. Current Codebase Facts

Agent Memory already has a closed first runtime chain:

```text
src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/
src/Runtime/Agent/CrestCreates.Agent.Memory/
tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/
```

The stable contracts are:

```text
IAgentMemoryContentSanitizer
IAgentContextCompressor
IAgentMemoryExtractor
IAgentMemoryPromotionService
IAgentMemoryRetriever
IAgentContextSourceExpander
IAgentAuthoringContextBuilder
```

The current DI entrypoint is `AddAgentMemoryRuntime()`, not `AddAgentMemory()`.
It registers deterministic implementations through `TryAddSingleton`.

The current main-chain tests verify:

```text
SaveConversation
  -> Compress
  -> ExtractCandidates
  -> Promote
  -> Recall
  -> BuildAuthoringContext
```

Agent Memory boundary tests already assert that memory runtime does not depend
on Agent Control Plane. Phase 7g+ must extend that boundary instead of weakening
it.

Phase 7g descriptor authoring already established the project-local LLM adapter
pattern:

```text
Authoring.Abstractions  -> semantic model/client contracts
Authoring               -> provider-agnostic prompt/parser/agent runtime
Authoring.Http          -> provider-specific HTTP integration
```

Phase 7h added the reusable prompt evidence kernel:

```text
IAgentPromptEvidenceFactory
IAgentPromptHashService
IAgentPromptCanonicalPayloadProjector<TPayload>
AgentPromptInputEvidenceSummary
AgentPromptOutputEvidenceSummary
```

Phase 7g+ should follow those facts. It should not introduce a second prompt
hash path, a generic prompt executor, or a shared provider runtime.

## 3. Non-Goals

This phase must not implement:

- Agent Memory Runtime v2.
- A durable memory provider.
- Vector search, embedding retrieval, or semantic ranking.
- A prompt CMS, prompt repository, PromptOps surface, or provider platform.
- A generic `IAgentPromptExecutor`, `IAgentPromptModelClient`, or completion
  runtime.
- Control Plane, MCP, HTTP API, Dynamic API, CLI, or tool-surface exposure for
  memory.
- Provider-specific HTTP integration such as `CrestCreates.Agent.Memory.Llm.Http`.
- Automatic background compression, extraction, or memory maintenance.
- Fact validation or truth scoring.
- Active memory promotion.
- Descriptor, draft, registry, activation-request, runtime-handler, or runtime
  state mutation.

Provider-specific HTTP integration is explicitly deferred to a later phase. The
first implementation should close the adapter boundary with fake and recorded
model clients only.

## 4. Authority Boundary

LLM output may only become:

```text
AgentCompressedContext
AgentCompressedContextBlock
AgentMemoryCandidate
```

LLM output must not:

- Create `AgentMemoryItem` directly.
- Mark memory as authoritative.
- Set candidate status to `Active`.
- Save active memory.
- Make candidates visible to recall before explicit promotion.
- Call or bypass `IAgentMemoryPromotionService`.
- Override metadata, review, activation, lifecycle, authorization, or runtime
  gate decisions.

The existing authority order still applies:

```text
MetadataContextPack > AgentMemoryPack
ReviewResult > AgentMemoryPack
ActivationEvidence > AgentMemoryPack
LifecycleGovernance > AgentMemoryPack
AuthorizationPolicy > AgentMemoryPack
RuntimeActivationGate > AgentMemoryPack
```

Memory remains non-authoritative recalled context. If memory conflicts with
metadata or governance state, memory loses.

## 5. Project Shape

Add one provider-agnostic project and one test project:

```text
src/Runtime/Agent/
  CrestCreates.Agent.Memory.Llm/

tests/Runtime/Agent/
  CrestCreates.Agent.Memory.Llm.Tests/
```

Allowed project dependencies:

```text
CrestCreates.Agent.Memory.Llm
  -> CrestCreates.Agent.Memory.Abstractions
  -> CrestCreates.Agent.Memory
  -> CrestCreates.Agent.Prompting.Abstractions
  -> CrestCreates.Metadata.Abstractions
  -> Microsoft.Extensions.DependencyInjection.Abstractions
  -> Microsoft.Extensions.Options
```

The dependency on `CrestCreates.Agent.Memory` is allowed only so the LLM adapter
can delegate to concrete deterministic fallback implementations such as
`DefaultAgentContextCompressor` and `DefaultAgentMemoryExtractor`.

Forbidden dependencies:

```text
CrestCreates.Agent.Memory -> CrestCreates.Agent.Memory.Llm
CrestCreates.Agent.Prompting -> CrestCreates.Agent.Memory.Llm
CrestCreates.Agent.Memory.Llm -> CrestCreates.Agent.ControlPlane
CrestCreates.Agent.Memory.Llm -> CrestCreates.Agent.ControlPlane.Abstractions
CrestCreates.Agent.Memory.Llm -> CrestCreates.Agent.Authoring.Http
CrestCreates.Agent.Memory.Llm -> Platform projects
CrestCreates.Agent.Memory.Llm -> Framework Api/Web projects
CrestCreates.Agent.Memory.Llm -> persistence provider projects
CrestCreates.Agent.Memory.Llm -> runtime handler implementation projects
```

## 6. Components

### 6.1 Options

`AgentMemoryLlmAdapterOptions` controls adapter behavior:

```text
UseLlmCompressor
UseLlmExtractor
EnableDeterministicFallback
MaxCompressedBlockCount
MaxCompressedBlockCharacters
MaxCandidateCount
MaxCandidateCharacters
MaxCandidateConfidence
CompressionTemplateId
CompressionTemplateVersion
ExtractionTemplateId
ExtractionTemplateVersion
PromptContractVersion
ModelProfileRef
ProviderProfileRef
```

The default options must be conservative:

```text
UseLlmCompressor = false
UseLlmExtractor = false
EnableDeterministicFallback = true
MaxCandidateConfidence = AgentMemoryConfidence.Medium
```

### 6.2 Model Boundary

Memory owns its own narrow model client abstraction:

```csharp
public interface IAgentMemoryLlmModelClient
{
    Task<AgentMemoryLlmModelResponse> CompleteAsync(
        AgentMemoryLlmModelRequest request,
        CancellationToken cancellationToken = default);
}
```

Do not reuse `IDescriptorAuthoringModelClient`. Descriptor authoring owns draft
production semantics. Agent Memory owns compression and extraction semantics.

Provider-agnostic DTOs live in `CrestCreates.Agent.Memory.Llm`:

```text
AgentMemoryLlmModelProfile
AgentMemoryLlmProviderProfile
AgentMemoryLlmModelRequest
AgentMemoryLlmModelResponse
AgentMemoryLlmProviderFailureKind
FakeAgentMemoryLlmModelClient
RecordedAgentMemoryLlmModelClient
```

`AgentMemoryLlmModelResponse` may carry raw `ResponseText` for the parser, but
raw response text must never enter output evidence hash payloads or public
evidence summaries.

### 6.3 Compression Adapter

`LlmAgentContextCompressor` implements `IAgentContextCompressor`.

It owns this flow:

```text
AgentConversationRecord / AgentTaskRecord
  -> IAgentMemoryContentSanitizer
  -> AgentMemoryCompressionPromptInput
  -> IAgentPromptEvidenceFactory.CreateInputEvidence
  -> IAgentMemoryCompressionPromptBuilder
  -> IAgentMemoryLlmModelClient
  -> AgentMemoryLlmModelResponseEvidenceProjection
  -> IAgentPromptEvidenceFactory.CreateOutputEvidence
  -> IAgentMemoryCompressionOutputParser
  -> validation / normalization
  -> AgentCompressedContext
```

If provider, parse, validation, source-ref, tenant, or redaction validation
fails and deterministic fallback is enabled, it delegates to
`DefaultAgentContextCompressor`.

The adapter must use sanitized content only. Raw conversation turns or task
events may be read so they can be sanitized, but only sanitized content may
enter prompt input or model request text.

The LLM may propose block content. The framework must own and validate:

- `ContextId`
- `TenantId`
- `BlockId`
- `SourceRefs`
- source ranges
- `CanonicalContentHash`
- redaction metadata
- diagnostics
- configured count and character budgets

### 6.4 Extraction Adapter

`LlmAgentMemoryExtractor` implements `IAgentMemoryExtractor`.

It owns this flow:

```text
AgentCompressedContext
  -> AgentMemoryExtractionPromptInput
  -> IAgentPromptEvidenceFactory.CreateInputEvidence
  -> IAgentMemoryExtractionPromptBuilder
  -> IAgentMemoryLlmModelClient
  -> AgentMemoryLlmModelResponseEvidenceProjection
  -> IAgentPromptEvidenceFactory.CreateOutputEvidence
  -> IAgentMemoryExtractionOutputParser
  -> validation / normalization
  -> AgentMemoryCandidate[]
```

Validation must enforce:

- Every candidate belongs to the input context tenant.
- Every candidate has at least one valid source ref.
- Source refs exist in the compressed input.
- Candidate status is `AgentMemoryStatus.Candidate`.
- Candidates are not active or authoritative.
- Candidate confidence is capped by `MaxCandidateConfidence`.
- Candidate count and content length obey configured limits.
- Redaction metadata and sanitization diagnostics are preserved when applicable.

If validation fails and deterministic fallback is enabled, it delegates to
`DefaultAgentMemoryExtractor`.

### 6.5 Prompt Builders and Parsers

Prompt builders live in Memory.Llm:

```text
IAgentMemoryCompressionPromptBuilder
DefaultAgentMemoryCompressionPromptBuilder
IAgentMemoryExtractionPromptBuilder
DefaultAgentMemoryExtractionPromptBuilder
```

Default prompt identity:

```text
Compression template id:       agent-memory.compression.default
Compression template version:  7gplus.v1
Extraction template id:        agent-memory.extraction.default
Extraction template version:   7gplus.v1
Prompt contract version:       agent-memory-llm.v1
```

Prompt builders must instruct the model to:

- Use already-sanitized content only.
- Never invent facts.
- Never validate facts.
- Preserve source reference identifiers exactly.
- Preserve redaction markers.
- Output JSON only.
- Cite source refs for every block and candidate.
- Keep memory candidates non-authoritative and promotion-required.

Provider output is parsed into provider DTOs before domain materialization:

```text
AgentMemoryCompressionProviderOutputDto
AgentMemoryCompressedBlockDto
AgentMemoryExtractionProviderOutputDto
AgentMemoryCandidateDto
AgentMemoryLlmOutputDiagnosticDto
```

Parsers must use source-generated JSON contexts. They must not materialize
domain objects directly from untrusted provider output without validation.

### 6.6 Prompt Evidence Integration

Memory.Llm must use Phase 7h Prompting evidence:

```text
IAgentPromptEvidenceFactory
IAgentPromptCanonicalPayloadProjector<TPayload>
AgentPromptInputEvidenceSummary
AgentPromptOutputEvidenceSummary
```

Required projectors:

```text
AgentMemoryCompressionPromptInputProjector
AgentMemoryExtractionPromptInputProjector
AgentMemoryLlmModelResponseEvidenceProjector
```

The output evidence projection excludes raw `ResponseText`:

```text
AgentMemoryLlmModelResponseEvidenceProjection
  ProviderName
  ModelName
  PromptInputHash
  FailureKind
  FailureDetail
  Metadata
```

Prompt input hash purpose remains `SourceIdentity`. Prompt output hash purpose
remains `AuditEvidence`. These hashes are evidence identity only; they are not
review approval, activation approval, or memory truth validation.

### 6.7 Canonical Output Hashing

Phase 7g+ also needs canonical hashes over sanitized output. Treat these as
separate from prompt evidence:

```text
PromptInputHash
  - hash over prompt input projection
  - purpose: SourceIdentity

PromptOutputEvidenceHash
  - hash over safe provider observation/output projection
  - purpose: AuditEvidence

CompressedOutputHash / CandidateOutputHash
  - hash over validated sanitized domain output
  - purpose: SourceIdentity or Integrity, depending on existing canonical hash naming
```

Compressed output hash must include stable semantic fields, not random runtime
IDs:

```text
tenant id
block semantic identity
source refs
source ranges
sanitized block content
redaction metadata
compression template identity
prompt input hash
```

Candidate output hash must include:

```text
tenant id
memory kind
candidate content
confidence
source refs
redaction metadata
extraction template identity
prompt input hash
compressed block refs
```

Implementation should extend the memory canonical hash projector or add an
adapter-local projector without introducing ad-hoc SHA-256 helpers.

## 7. DI Registration

The existing default remains deterministic:

```csharp
services.AddAgentMemoryRuntime();
```

It must still resolve:

```text
IAgentContextCompressor -> DefaultAgentContextCompressor
IAgentMemoryExtractor   -> DefaultAgentMemoryExtractor
```

To support safe fallback, `AddAgentMemoryRuntime()` should also register the
deterministic implementations as concrete services:

```text
DefaultAgentContextCompressor
DefaultAgentMemoryExtractor
```

LLM adapters are opt-in:

```csharp
services.AddAgentMemoryLlmCompressor();
services.AddAgentMemoryLlmExtractor();
```

An aggregate registration may exist for host convenience:

```csharp
services.AddAgentMemoryLlmAdapters(options =>
{
    options.UseLlmCompressor = true;
    options.UseLlmExtractor = true;
    options.EnableDeterministicFallback = true;
});
```

Explicit per-adapter registration is the preferred main path because it keeps
replacement boundaries clear.

Memory.Llm registration must not register a real HTTP provider. Hosts or tests
must register `IAgentMemoryLlmModelClient` explicitly.

## 8. Diagnostics

LLM-specific diagnostics live in Memory.Llm unless a diagnostic proves to be
runtime-generic later.

Required diagnostic coverage:

```text
PromptInputHashCreated
PromptOutputHashCreated
SanitizedInputRequired
SourceRefCovered
SourceRefSkipped
InvalidSourceRef
SourceRangePreserved
SourceRangeMissing
RedactionMetadataPreserved
RedactionMetadataMissing
ProviderUnavailable
ProviderReturnedEmptyOutput
ParseFailed
ValidationWarning
FallbackToDeterministicCompressor
FallbackToDeterministicExtractor
NonAuthoritativeOutputEnforced
PromotionRequiredBeforeRecall
CandidateConfidenceCapped
OutputBudgetTruncated
```

Diagnostics should be attached to returned `AgentCompressedContext`,
`AgentCompressedContextBlock`, or `AgentMemoryCandidate` objects where that is
the existing memory contract surface. Prompt evidence diagnostics should remain
prompt-evidence diagnostics and should not replace memory diagnostics.

## 9. Error Handling

The adapter is fallback-safe by default.

Compression must fallback when:

- Provider is unavailable.
- Provider returns empty output.
- JSON parse fails.
- Provider output references unknown source refs.
- Provider output crosses tenant boundaries.
- Redaction metadata is missing or inconsistent.
- Block count or character budgets are exceeded and cannot be normalized.
- Required prompt evidence cannot be created.

Extraction must fallback when:

- Provider is unavailable.
- Provider returns empty output.
- JSON parse fails.
- Candidate source refs do not exist in the compressed context.
- Candidate tenant does not match context tenant.
- Candidate status is not `Candidate`.
- Candidate content is empty after normalization.
- Candidate count or character budgets are exceeded and cannot be normalized.
- Required prompt evidence cannot be created.

Fallback must add diagnostics. It must not silently hide provider or validation
failure.

If `EnableDeterministicFallback` is false, the adapter should return an empty
safe result with diagnostics instead of producing partially trusted output.

## 10. Testing and Exit Criteria

### 10.1 Default Path

```text
AddAgentMemoryRuntime_UsesDeterministicCompressorAndExtractor_ByDefault
```

### 10.2 Opt-in Behavior

```text
AddAgentMemoryLlmCompressor_ReplacesOnlyCompressor_WhenExplicitlyEnabled
AddAgentMemoryLlmExtractor_ReplacesOnlyExtractor_WhenExplicitlyEnabled
AddAgentMemoryLlmAdapters_ReplacesOnlySelectedAdapters
```

### 10.3 Sanitized Input

```text
LlmCompressor_UsesSanitizedContentOnly
LlmExtractor_UsesCompressedSanitizedContextOnly
```

Use fixtures where raw content contains sensitive text and sanitized content
does not. Assert model requests do not contain the raw sensitive text.

### 10.4 Source Refs, Ranges, and Redaction

```text
LlmCompressor_PreservesSourceRefsAndRanges
LlmExtractor_CandidatesPreserveSourceRefs
InvalidSourceRef_TriggersDiagnosticAndFallback
LlmCompressor_PreservesRedactionMetadata
LlmExtractor_CandidatesCarryRedactionEvidence
```

### 10.5 Fallback

```text
LlmCompressor_ParseFailure_FallsBackToDeterministic
LlmExtractor_InvalidSourceRef_FallsBackToDeterministic
ProviderUnavailable_FallsBackWithDiagnostic
FallbackDisabled_ReturnsSafeDiagnosticsWithoutTrustedOutput
```

### 10.6 Non-authoritative Lifecycle

```text
LlmExtractor_CandidatesAreNotActiveMemoryItems
LlmExtractor_CandidatesAreNotAuthoritative
Candidates_DoNotAppearInRecall_BeforePromotion
Candidates_AppearInRecall_OnlyAfterExplicitPromotion
```

### 10.7 Prompt Evidence and Hashing

```text
LlmCompressor_AttachesPromptInputAndOutputEvidence
LlmExtractor_AttachesPromptInputAndOutputEvidence
OutputEvidenceHash_ExcludesRawProviderResponseText
PromptInputHash_Changes_WhenSanitizedInputChanges
CanonicalOutputHash_Changes_WhenSanitizedOutputChanges
```

### 10.8 Boundary Tests

```text
AgentMemoryRuntime_DoesNotReferenceMemoryLlm
AgentMemoryLlm_DoesNotReferenceControlPlaneActivationAuthoringHttpOrRuntimeHandlers
AgentPrompting_DoesNotReferenceMemoryLlm
MemoryLlm_DoesNotExposeToolSurfaceOrActivationSurface
```

## 11. Rejection Checklist

Reject the implementation if any of these occur:

- `AddAgentMemoryRuntime()` starts using LLM by default.
- `CrestCreates.Agent.Memory` references `CrestCreates.Agent.Memory.Llm`.
- LLM output creates `AgentMemoryItem` directly.
- LLM candidates are recallable before explicit promotion.
- LLM output is marked authoritative.
- Raw provider response text enters output evidence hash.
- Raw unsanitized source content enters prompt input or model request.
- Source refs are accepted without validation against input.
- Redaction metadata can be dropped silently.
- Prompting becomes a model executor/provider abstraction.
- Memory.Llm references ControlPlane, Activation, Authoring.Http, Platform,
  Framework Api/Web, persistence providers, or runtime handlers.
- The implementation requires broad redesign of `Agent.Memory.Abstractions`.

## 12. Implementation Order

1. Add `CrestCreates.Agent.Memory.Llm` and test project with provider-agnostic
   contracts, options, fake/recorded model clients, JSON contexts, and boundary
   tests.
2. Add prompt input/output evidence projectors and prompt builders.
3. Add compression parser/validator and `LlmAgentContextCompressor` with
   deterministic fallback.
4. Add extraction parser/validator and `LlmAgentMemoryExtractor` with
   deterministic fallback.
5. Add canonical output hash support over sanitized validated output.
6. Add DI opt-in registration and default-path regression tests.
7. Add lifecycle tests proving candidates remain non-authoritative and recall
   invisible before explicit promotion.

Do not start `CrestCreates.Agent.Memory.Llm.Http` in this phase.
