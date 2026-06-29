# Phase 7e+ — Agent Memory & Context Compression Runtime

> Date: 2026-06-29  
> Status: Approved for Implementation  
> Scope: Issue #43 first implementation phase

## 1. Goal

Introduce a bounded, deterministic, source-traceable Agent Memory and Context Compression runtime for the LLM Bootstrap Plane.

This phase fills the gap between Metadata Context Pack, Agent Control Plane, Review Report / Fix Proposal, Safe Activation Workflow, and the future AI-assisted Descriptor Authoring golden scenario.

The runtime is context infrastructure. It is not an agent execution runtime, governance authority, activation authority, tool surface, or chatbot framework.

The first-phase chain is:

```text
Raw conversation/task input
  -> sanitization / redaction / rejection
  -> sanitized conversation/task history
  -> deterministic compression
  -> memory candidate extraction
  -> explicit promotion
  -> budgeted recall
  -> sanitized source expansion
  -> AgentMemoryPack
  -> AgentAuthoringContext composition
```

## 2. Non-Goals

This phase must not implement:

- Control Plane tool methods for memory.
- Dynamic API, HTTP, MCP, CLI, or TUI adapters.
- Provider-specific LLM SDK integration.
- Vector search or embedding ranking.
- Production persistence providers.
- Background compaction or autonomous memory maintenance.
- AgentRuntime Domain/Application/DynamicApi module stack.
- Runtime descriptor activation.
- Descriptor draft mutation.
- Registry mutation.
- Workflow execution.
- HumanTask approval logic.
- Governance, compatibility, lifecycle, activation, or authorization truth.

`docs/design/agent-runtime-architecture.md` is background only. It describes a broader Agent Runtime direction and must not drive this issue into a full AgentRuntime platform build.

## 3. Architecture

Add current-layout projects:

```text
src/Runtime/Agent/
  CrestCreates.Agent.Memory.Abstractions/
  CrestCreates.Agent.Memory/

tests/Runtime/Agent/
  CrestCreates.Agent.Memory.Tests/
```

Dependency direction:

```text
CrestCreates.Agent.Memory.Abstractions
  -> CrestCreates.Core.Abstractions
  -> CrestCreates.Metadata.Abstractions
  -> CrestCreates.Metadata.ContextPack.Abstractions
  -> CrestCreates.Agent.Abstractions

CrestCreates.Agent.Memory
  -> CrestCreates.Agent.Memory.Abstractions
  -> Microsoft.Extensions.DependencyInjection.Abstractions
```

`CrestCreates.Agent.Memory.Abstractions` must not reference `CrestCreates.Agent.ControlPlane.Abstractions` for invocation identity. `ControlPlane.Abstractions` already carries DescriptorDraft, DraftContracts, HumanTask, and activation contracts, so referencing it would make memory infrastructure heavier than its purpose.

Preferred identity shape:

```text
CrestCreates.Agent.Abstractions
  AgentInvocationContext
```

The neutral context should align with existing Control Plane context semantics:

- `TenantId`
- `ActorId`
- `ActorKind`
- `AgentId`
- `SessionId`
- `CorrelationId`
- `CausationId`
- `InvocationSource`
- `TraceAttributes`

If introducing `AgentInvocationContext` in `CrestCreates.Agent.Abstractions` becomes too invasive, `Agent.Memory.Abstractions` may define a memory-owned context type with the same field semantics. The spec still forbids pulling in Control Plane activation and draft dependencies for this reason.

## 4. Authority Boundary

Keep these roles separate:

```text
MetadataContextPack
  authoritative descriptor/topology/governance-state context

AgentMemoryPack
  recalled historical/task/decision context

AuthoringRequest
  current user or agent authoring intent

AgentAuthoringContext
  bounded input for future descriptor authoring workflows
```

Memory may help an agent draft, explain, or suggest. Memory must not:

- Approve activation.
- Activate descriptors.
- Mutate registries.
- Create governance decisions.
- Override authorization.
- Override lifecycle governance.
- Override compatibility analysis.
- Bypass HumanTask review.
- Call `IRuntimeActivationGate`.
- Execute runtime handlers.
- Treat recalled memory as activation evidence.
- Treat recalled memory as validated fact.

If memory conflicts with authoritative context, memory loses:

```text
MetadataContextPack > AgentMemoryPack
ReviewResult > AgentMemoryPack
ActivationEvidence > AgentMemoryPack
LifecycleGovernance > AgentMemoryPack
AuthorizationPolicy > AgentMemoryPack
RuntimeActivationGate > AgentMemoryPack
```

Recalled memory should be explicitly marked as non-authoritative recalled context.

## 5. Runtime Services

Expose runtime services only. The exact DTO property set is defined by the contract sections below, but the service method boundaries should be explicit:

```csharp
public interface IAgentConversationStore
{
    ValueTask SaveConversationAsync(AgentConversationRecord conversation, CancellationToken cancellationToken = default);
    ValueTask<AgentConversationRecord?> GetConversationAsync(string tenantId, string conversationId, CancellationToken cancellationToken = default);
    ValueTask AppendTurnAsync(string tenantId, string conversationId, AgentConversationTurn turn, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentConversationRecord>> ListConversationsAsync(AgentConversationQuery query, CancellationToken cancellationToken = default);
}

public interface IAgentTaskHistoryStore
{
    ValueTask SaveTaskAsync(AgentTaskRecord task, CancellationToken cancellationToken = default);
    ValueTask<AgentTaskRecord?> GetTaskAsync(string tenantId, string taskId, CancellationToken cancellationToken = default);
    ValueTask AppendEventAsync(string tenantId, string taskId, AgentTaskEvent taskEvent, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentTaskRecord>> ListTasksAsync(AgentTaskHistoryQuery query, CancellationToken cancellationToken = default);
}

public interface IAgentCompressedContextStore
{
    ValueTask SaveBlockAsync(AgentCompressedContextBlock block, CancellationToken cancellationToken = default);
    ValueTask<AgentCompressedContextBlock?> GetBlockAsync(string tenantId, string blockId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentCompressedContextBlock>> ListBlocksAsync(AgentCompressedContextQuery query, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryStore
{
    ValueTask SaveCandidateAsync(AgentMemoryCandidate candidate, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryCandidate?> GetCandidateAsync(string tenantId, string candidateId, CancellationToken cancellationToken = default);
    ValueTask SaveMemoryAsync(AgentMemoryItem memory, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryItem?> GetMemoryAsync(string tenantId, string memoryId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<AgentMemoryItem>> ListMemoriesAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default);
    ValueTask SupersedeAsync(AgentMemorySupersedeRequest request, CancellationToken cancellationToken = default);
    ValueTask ArchiveAsync(AgentMemoryArchiveRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryContentSanitizer
{
    ValueTask<AgentMemorySanitizationResult> SanitizeAsync(AgentMemorySanitizationRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentContextCompressor
{
    ValueTask<AgentContextCompressionResult> CompressAsync(AgentContextCompressionRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryExtractor
{
    ValueTask<AgentMemoryExtractionResult> ExtractAsync(AgentMemoryExtractionRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryPromotionService
{
    ValueTask<AgentMemoryPromotionResult> PromoteAsync(AgentMemoryPromotionRequest request, CancellationToken cancellationToken = default);
    ValueTask<AgentMemoryPromotionResult> RejectCandidateAsync(AgentMemoryCandidateRejectionRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentMemoryRetriever
{
    ValueTask<AgentMemoryPack> RecallAsync(AgentMemoryQuery query, CancellationToken cancellationToken = default);
}

public interface IAgentContextSourceExpander
{
    ValueTask<AgentContextExpansionResult> ExpandAsync(AgentContextExpansionRequest request, CancellationToken cancellationToken = default);
}

public interface IAgentAuthoringContextBuilder
{
    ValueTask<AgentAuthoringContext> BuildAsync(AgentAuthoringContextBuildRequest request, CancellationToken cancellationToken = default);
}
```

The implementation project provides default in-memory and rule-based services:

- `InMemoryAgentConversationStore`
- `InMemoryAgentTaskHistoryStore`
- `InMemoryAgentCompressedContextStore`
- `InMemoryAgentMemoryStore`
- `DefaultAgentMemoryContentSanitizer`
- `DefaultAgentContextCompressor`
- `DefaultAgentMemoryExtractor`
- `DefaultAgentMemoryPromotionService`
- `DefaultAgentMemoryRetriever`
- `DefaultAgentContextSourceExpander`
- `DefaultAgentAuthoringContextBuilder`

DI registration follows existing patterns:

```text
AddAgentMemory()
  TryAddSingleton<IAgentConversationStore, InMemoryAgentConversationStore>
  TryAddSingleton<IAgentTaskHistoryStore, InMemoryAgentTaskHistoryStore>
  TryAddSingleton<IAgentCompressedContextStore, InMemoryAgentCompressedContextStore>
  TryAddSingleton<IAgentMemoryStore, InMemoryAgentMemoryStore>
  TryAddSingleton<IAgentMemoryContentSanitizer, DefaultAgentMemoryContentSanitizer>
  TryAddSingleton<IAgentContextCompressor, DefaultAgentContextCompressor>
  TryAddSingleton<IAgentMemoryExtractor, DefaultAgentMemoryExtractor>
  TryAddSingleton<IAgentMemoryPromotionService, DefaultAgentMemoryPromotionService>
  TryAddSingleton<IAgentMemoryRetriever, DefaultAgentMemoryRetriever>
  TryAddSingleton<IAgentContextSourceExpander, DefaultAgentContextSourceExpander>
  TryAddSingleton<IAgentAuthoringContextBuilder, DefaultAgentAuthoringContextBuilder>
  TryAddSingleton(TimeProvider.System)
```

Do not register persistence providers, LLM adapters, or Control Plane tool adapters in this phase.

`TimeProvider` may be used for `CreatedAt`, `UpdatedAt`, `PromotedAt`, `ArchivedAt`, and similar model timestamps. It must not be read during canonical hash computation. Any timestamp that affects a canonical hash must already be a stable model field and must be normalized before hashing.

`IAgentMemoryStore` is a persistence-like store boundary. It must not own promotion policy, dedup policy, authority decisions, or memory lifecycle semantics beyond atomic state writes. `IAgentMemoryPromotionService` owns candidate-to-memory semantics. If `SupersedeAsync` and `ArchiveAsync` remain on the store, they are atomic state-write helpers only; the request must already carry actor, reason, timestamp, and source/explanation data validated by the service layer.

`SaveMemoryAsync` is a persistence primitive, not the production promotion path. Runtime services and main-chain flows must use `IAgentMemoryPromotionService` to turn a candidate into active memory. Unit tests may use `SaveMemoryAsync` to seed store state, but production code must not bypass promotion semantics for candidate promotion.

## 6. Contracts

### 6.1 Source and Evidence

Source refs must be explicit and provider-neutral.

```text
AgentContextSourceRef
  SourceKind
  TenantId
  SourceId
  RangeStart?
  RangeEnd?
  DescriptorRefs
  CorrelationId?
  CausationId?
  CanonicalContentHash?

AgentContextEvidenceRef
  EvidenceId
  EvidenceKind
  TenantId
  SourceRefs
  CanonicalContentHash?
```

`AgentContextEvidenceRef` means context-source evidence only. It is not ActivationEvidence, package evidence, HumanTask evidence, or approval evidence, and activation services must not accept it as activation proof.

`AgentSourceKind` initial values:

- ConversationTurn
- TaskEvent
- CompressedContextBlock
- MemoryCandidate
- MemoryItem
- MetadataContextPack
- ReviewReport
- FixProposal
- PackagePreview
- ActivationRequest

Source expansion in this phase only expands sources owned by memory stores: conversation turns, task events, compressed blocks, candidates, and memory items. It must not query Control Plane private dictionaries, activation stores, HumanTask stores, or registries.

Non-memory source kinds may be recorded as trace references in this phase, but `DefaultAgentContextSourceExpander` must return a structured `NotExpandable` / `ExternalSourceNotSupported` diagnostic for sources not owned by memory stores. They are trace references, not expansion targets.

### 6.2 Conversation and Task History

Models:

- `AgentConversationRecord`
- `AgentConversationTurn`
- `AgentConversationTurnKind`
- `AgentTaskRecord`
- `AgentTaskEvent`
- `AgentTaskEventKind`

Required semantics:

- Tenant-aware.
- Actor-aware.
- Conversation/task/correlation identity.
- Timestamp and sequence.
- Sanitized content.
- Sanitization metadata.
- Stable source refs.
- Optional descriptor refs.
- Optional workflow/human task/capability refs as refs only, not service dependencies.

Task history has its own store boundary through `IAgentTaskHistoryStore`. `AgentTaskRecord` and `AgentTaskEvent` must not be model-only contracts with no storage path.

### 6.3 Sanitization

Add an ingestion-time sanitization boundary:

```csharp
public interface IAgentMemoryContentSanitizer
{
    ValueTask<AgentMemorySanitizationResult> SanitizeAsync(
        AgentMemorySanitizationRequest request,
        CancellationToken cancellationToken = default);
}
```

Sanitization happens before storage, compression, extraction, promotion, recall, and expansion.

Default sanitizer requirements:

- Deterministic and rule-based.
- No LLM or provider dependency.
- Redacts common secret-like patterns:
  - bearer tokens
  - `password=...`
  - `api_key=...`
  - connection-string password segments
  - long token-like values
- Can reject content that cannot safely be retained.
- Emits diagnostics and metadata.

Recommended metadata on stored and derived objects:

- `SensitivityLevel`
- `RedactionState`
- `ContainsSensitiveContent`
- `RedactionReasons`
- `SanitizationVersion`

Source expansion must return sanitized stored content only. It must not reveal raw input through expansion.

### 6.4 Compression

Models:

- `AgentCompressedContextBlock`
- `AgentCompressedContextKind`
- `AgentCompressionSourceRange`
- `AgentCompressionMetadata`
- `AgentContextCompressionRequest`
- `AgentContextCompressionResult`
- `AgentContextCompressionOptions`

Default compression is deterministic and rule-based.

Required behavior:

- Stable ordering by tenant, conversation/task id, timestamp, sequence, source id, and hash.
- Stable source ranges on every block.
- Content hash over sanitized canonical content and stable source identity.
- Diagnostics when count/budget truncation happens.
- No dictionary iteration order dependency.
- No runtime `GetHashCode()` dependency.
- No current-time hash input.
- No raw serializer-dependent JSON hash.

LLM-backed compression is future work behind an adapter. It must not become the only compression path.

### 6.5 Memory Extraction and Promotion

Models:

- `AgentMemoryCandidate`
- `AgentMemoryItem`
- `AgentMemoryKind`
- `AgentMemoryStatus`
- `AgentMemoryConfidence`
- `AgentMemoryVerificationState`
- `AgentMemoryPromotionRequest`
- `AgentMemoryPromotionResult`
- `AgentMemorySupersedeRequest`
- `AgentMemoryArchiveRequest`
- `AgentMemoryCandidateRejectionRequest`

Extraction produces candidates only:

```text
AgentCompressedContextBlock
  -> AgentMemoryCandidate
  -> explicit promotion
  -> AgentMemoryItem
```

Default extractor must not auto-promote candidates.

Promotion requirements:

- Requires actor/invocation context.
- Requires a reason.
- Requires a timestamp, supplied through the request or assigned by the promotion service via `TimeProvider`.
- Requires source refs or an explicit explanation.
- Promotes sanitized content only.
- Preserves source refs and redaction metadata.
- Produces recallable memory, not validated fact.
- Can reject stale, duplicate, invalid, or unsafe candidates.
- Supports superseding an active memory item with an explicit relationship.

Promotion, rejection, supersede, and archive are recall-universe-changing operations. All four require actor context, reason, timestamp, and source refs or explanation. They do not create validated facts, but they must be explainable.

Initial `AgentMemoryKind` values:

- ArchitectureDecision
- RuntimeConstraint
- DescriptorRule
- CodingConvention
- TestInvariant
- IntegrationPattern
- KnownIssue
- ConfigValue
- BusinessRule
- UserPreference
- ReviewFinding
- ActivationConstraint

Initial `AgentMemoryStatus` values:

- Active
- Superseded
- Stale
- Archived

`AgentMemoryConfidence` should be a closed enum in this phase, not a floating-point score:

```csharp
public enum AgentMemoryConfidence
{
    Unknown = 0,
    Low = 1,
    Medium = 2,
    High = 3
}
```

Avoid pseudo-precise confidence values such as `0.873` until there is a real calibration model and acceptance criteria for it.

### 6.6 Recall and Pack Building

Models:

- `AgentMemoryQuery`
- `AgentMemoryPack`
- `AgentMemoryPackEntry`
- `AgentMemoryPackDiagnostics`

Recall query supports:

- Tenant id.
- Caller/invocation context.
- Already-resolved visible descriptor refs or visibility scope.
- Intent text.
- Focus descriptor refs.
- Descriptor kinds.
- Tags.
- Included/excluded memory kinds.
- Maximum memory count.
- Maximum compressed block count.
- Approximate character budget.
- Minimum confidence threshold.
- Include stale flag.
- Include superseded flag.
- Include source refs flag.

Defaults:

```text
IncludeStale = false
IncludeSuperseded = false
IncludeArchived = false
```

Initial retrieval uses deterministic keyword/tag/ref matching. Vector search and embeddings are out of scope.

The memory runtime does not resolve descriptor visibility by itself in this phase. `AgentMemoryQuery` must carry an already-resolved visibility boundary, such as visible descriptor refs, visible descriptor kinds, or a caller-provided visibility scope. The retriever filters against that supplied boundary only. It must not call `AgentControlPlaneResourceResolver`, `IAgentControlPlaneToolService`, descriptor stores, draft stores, activation stores, or registries.

`AgentMemoryQuery` is intentionally shared by `IAgentMemoryStore.ListMemoriesAsync` and `IAgentMemoryRetriever.RecallAsync` in the first phase to keep contract count small. Implementers must keep the two semantics distinct:

- Store filtering fields: tenant, status, kinds, tags, descriptor refs, candidate or memory ids, and visibility-safe filters.
- Recall fields: intent text, scoring inputs, max counts, character budget, confidence threshold, and source inclusion.

If this DTO becomes ambiguous or bloated during implementation, split it before expanding behavior:

```text
AgentMemoryStoreQuery
AgentMemoryRecallQuery
```

Store code must not interpret recall scoring or budget fields as persistence rules.

Recommended scoring inputs:

- tenant match
- active status
- not stale
- not superseded
- descriptor ref overlap
- tag overlap
- intent token overlap
- recency bucket
- source count
- canonical hash tie-breaker

Stable ordering:

```text
score desc
kind
promotedAt desc
memoryId
canonicalContentHash
```

Pack diagnostics may include:

- candidate memory count
- eligible memory count
- returned memory count
- stale skipped count
- superseded skipped count
- unverified returned count
- source ref count
- budget exceeded flag
- requested budget
- actual character approximation
- warnings

Diagnostics must not leak denied descriptor existence, raw sensitive content, or redacted values.

### 6.7 Source Expansion

Service:

```csharp
public interface IAgentContextSourceExpander
{
    ValueTask<AgentContextExpansionResult> ExpandAsync(
        AgentContextExpansionRequest request,
        CancellationToken cancellationToken = default);
}
```

Expansion returns sanitized stored source content for stored source refs only.

For non-memory source kinds such as `MetadataContextPack`, `ReviewReport`, `FixProposal`, `PackagePreview`, or `ActivationRequest`, the default expander returns `NotExpandable` / `ExternalSourceNotSupported` with no leaked counts, summaries, or existence details.

Expansion is not validation. It only shows traceable origin.

### 6.8 Authoring Context Composition

Models:

- `AgentAuthoringRequest`
- `AgentAuthoringContext`
- `AgentAuthoringContextBudgetSummary`

`IAgentAuthoringContextBuilder` composes:

```text
MetadataContextPack
  + AgentMemoryPack
  + AgentAuthoringRequest
  = AgentAuthoringContext
```

The builder must:

- Accept an already-built `MetadataContextPack`.
- Accept an already-built `AgentMemoryPack`.
- Not call `IAgentControlPlaneToolService`.
- Not query descriptor stores, draft stores, activation stores, or registries.
- Not mutate either input pack.
- Mark memory entries as non-authoritative recalled context.
- Include budget/source/evidence summary.

## 7. Hashing and Identity

Apply the canonical hash direction from prior phases. Do not introduce ad hoc identity hashes.

Avoid:

```text
sha256-adhoc-v1
string.Join("|", ...)
GetHashCode()
raw serializer-dependent JSON hashing
bare string as identity truth
```

Use canonical identity fields:

- `CanonicalContentHash` for sanitized block/item content.
- `ScopeFingerprint` for recall query boundary.
- `VisibleMemorySetHash` for the eligible visible memory universe.
- `CanonicalPackHash` for final ordered pack.

Memory-specific rule:

```text
CanonicalContentHash = hash(sanitized canonical content + stable source identity)
RawSensitiveHash = not stored in this phase
```

Raw sensitive content must not be hashed into persistent comparable fingerprints. Short secrets, tokens, emails, phone numbers, passwords, and connection strings can become sensitive indices if hashed normally. A future issue may define HMAC/keyed hash semantics if forensic sensitive-source matching is required.

This phase guarantees stable identity for sanitized blocks, candidates, memory items, and packs only.

## 8. Store Semantics

Initial stores are in-memory only.

Key shapes:

```text
Conversation: (TenantId, ConversationId)
Task:         (TenantId, TaskId)
Block:        (TenantId, BlockId)
Candidate:    (TenantId, CandidateId)
Memory:       (TenantId, MemoryId)
```

Requirements:

- Tenant-aware composite keys.
- Snapshot-on-read.
- Snapshot-on-write.
- Defensive copies for nested collections and dictionaries.
- Deterministic query ordering.
- No provider-specific dependencies.
- No production database provider.
- No background compaction.
- No automatic promotion.

Mutable shared object leaks are not acceptable even if the collection itself is thread-safe.

## 9. AOT and Serialization

Add source-generated JSON support in `CrestCreates.Agent.Memory.Abstractions`:

```text
AgentMemoryJsonSerializerContext
AgentMemoryJsonSerializerOptions
AgentMemoryContractVersion
```

Register:

- all public request/result DTOs
- enums
- source refs
- evidence refs
- conversation/task records
- compressed blocks
- candidates
- memory items
- packs
- diagnostics
- `AgentAuthoringContext`

Adapter-facing contracts must not rely on reflection serialization.

## 10. Rejected and Deferred Approaches

### Approach B: Control Plane Integrated

Rejected for this phase.

Adding memory recall/compression directly to `IAgentControlPlaneToolService` would make memory look like a new tool execution path before authorization, audit, and visibility semantics are designed for adapters. It also risks pulling Memory into Control Plane draft, HumanTask, and activation dependencies.

### Approach C: Contract Only

Deferred / insufficient as the #43 exit state.

Contract-only work may be used as implementation step 1, but it is not sufficient to close #43. It would not prove the runnable context chain that #32 needs. This phase should include in-memory stores and default deterministic runtime services so a no-LLM main-chain test can exercise the full context flow.

### Broad Agent Runtime Stack

Deferred.

Do not introduce AgentRuntime Domain/Application/DynamicApi modules, generated endpoints, tool execution APIs, or autonomous orchestration in #43.

## 11. Acceptance Criteria

- New projects use `src/Runtime/Agent/...` and `tests/Runtime/Agent/...`.
- `CrestCreates.Agent.Memory.Abstractions` does not reference `CrestCreates.Agent.ControlPlane.Abstractions`.
- Memory projects do not reference `CrestCreates.Agent.ControlPlane` implementation.
- Memory projects do not reference DynamicApi, OpenApi, Web, Platform, persistence providers, concrete EventBus providers, DescriptorDraft implementation, HumanTask implementation, or activation implementations.
- `CrestCreates.Agent.Abstractions` or Memory-owned contracts provide neutral invocation identity.
- `IAgentTaskHistoryStore` provides a storage path for `AgentTaskRecord` and `AgentTaskEvent`.
- Public memory contracts are registered in a source-generated JSON context.
- In-memory stores are tenant-isolated.
- In-memory stores are snapshot-safe on read and write, including nested collections.
- Secret-like content is redacted or rejected before storage and compression.
- Compression, extraction, promotion, recall, and expansion operate on sanitized text by default.
- Recalled memory and source expansion return sanitized content, not raw sensitive input.
- Redaction metadata survives compression, extraction, promotion, and recall.
- `CanonicalContentHash` for memory blocks/items is based on sanitized canonical content.
- `CanonicalPackHash` is based on sanitized pack entries and deterministic pack metadata.
- Raw sensitive content is not hashed into a persistent comparable fingerprint.
- Compression is deterministic for identical sanitized input.
- Every compressed block has source refs or is explicitly marked unverified.
- Extraction creates candidates only.
- Promotion requires actor context and does not create validated facts.
- Recall is tenant-aware, budgeted, and deterministic.
- Recall filters against caller-supplied visibility boundaries and does not resolve descriptor visibility internally.
- Store filtering and recall scoring semantics remain distinct even if `AgentMemoryQuery` is shared in the first phase.
- Recall skips stale, superseded, and archived memory by default.
- Recall output includes source refs and non-authoritative markings.
- Source expansion only expands Memory-owned stored sources in this phase; external source kinds return non-expanding diagnostics.
- `AgentAuthoringContextBuilder` composes `MetadataContextPack + AgentMemoryPack + AgentAuthoringRequest` without mutating inputs.
- `AgentAuthoringContextBuilder` preserves authoritative MetadataContextPack content when recalled memory conflicts with it.
- Runtime promotion flows use `IAgentMemoryPromotionService`; `SaveMemoryAsync` remains a persistence primitive and is not the production candidate-promotion path.
- Promote, reject, supersede, and archive requests require actor, reason, timestamp, and source refs or explanation.
- `AgentMemoryConfidence` is a closed enum, not a pseudo-precise floating-point score.
- Existing activation, review, governance, authorization, and runtime gate boundaries remain untouched.
- Boundary tests, Metadata ContextPack tests, Agent ControlPlane tests, and new Agent Memory tests pass.

## 12. Required Tests

Minimum tests:

- `AgentMemoryAbstractions_DoesNotReference_ControlPlaneAbstractions`
- `AgentMemoryProjects_DoNotReference_ForbiddenRuntimeOrPlatformLayers`
- `ConversationStore_PreservesTenantIsolation`
- `ConversationStore_ReturnsSnapshotCopies`
- `ConversationStore_DefensivelyCopiesNestedCollections`
- `TaskHistoryStore_PreservesTenantIsolation`
- `TaskHistoryStore_ReturnsSnapshotCopies`
- `Sanitizer_RedactsSecretLikeContent_BeforeStorage`
- `Sanitizer_RejectedContent_IsNotCompressed`
- `Compression_IsDeterministic_ForSameSanitizedInput`
- `Compression_RecordsSourceRangesAndContentHashes`
- `Compression_TruncationProducesDiagnostic`
- `Extractor_CreatesCandidates_WithoutAutoPromoting`
- `Promotion_RequiresActorContext`
- `Promotion_RequiresReasonAndSourceExplanation`
- `Promotion_PreservesSourceRefsAndRedactionMetadata`
- `PromotionService_IsProductionPath_ForCandidatePromotion`
- `RejectCandidate_RequiresActorAndReason`
- `Supersede_RequiresActorAndReason`
- `Archive_RequiresActorAndReason`
- `MemoryStore_SaveMemoryAsync_IsPersistencePrimitiveOnly`
- `MemoryStore_UpsertUsesDeterministicOrdering`
- `MemoryStore_SupersedeHidesOriginalByDefault`
- `Recall_FiltersByTenant`
- `Recall_FiltersByMemoryKind`
- `Recall_FiltersByDescriptorRefs`
- `Recall_RespectsMaxCount`
- `Recall_RespectsCharacterBudget`
- `Recall_ExcludesStaleByDefault`
- `Recall_ExcludesSupersededByDefault`
- `Recall_DiagnosticsDoNotLeakDeniedDescriptorExistence`
- `Recall_UsesSuppliedVisibilityBoundary_WithoutResolvingDescriptors`
- `Recall_DoesNotApplyBudgetInsideStoreFiltering`
- `SourceExpansion_ReturnsSanitizedStoredContent`
- `SourceExpansion_ExternalSource_ReturnsNotExpandable`
- `SourceExpansion_DoesNotQueryControlPlaneStores`
- `AuthoringContextBuilder_ComposesMetadataAndMemoryPacks_WithoutMutation`
- `AuthoringContextBuilder_MarksMemoryAsNonAuthoritative_WhenMetadataContextConflicts`
- `AgentMemory_MainChain_BuildsSourceTraceableAuthoringContext`

The main-chain test must run without a real LLM:

```text
ConversationHistory
  -> Sanitize
  -> Compress
  -> ExtractCandidate
  -> Promote
  -> Recall
  -> Expand
  -> Build AgentAuthoringContext
```

## 13. Implementation Notes

The implementation plan should split the work into small phases:

1. Project scaffolding and dependency boundary tests.
2. Neutral invocation context and memory contract DTOs.
3. Source-generated JSON context.
4. In-memory conversation, task history, compressed context, and memory stores with snapshot semantics.
5. Sanitizer and sanitization metadata.
6. Deterministic compressor.
7. Candidate extractor and promotion service.
8. Retriever and pack builder.
9. Source expander.
10. Authoring context builder and main-chain test.

Do not implement adapter/tool exposure until the runtime service chain is tested and stable.
