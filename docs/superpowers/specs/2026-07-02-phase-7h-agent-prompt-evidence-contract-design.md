# Phase 7h - Agent Prompt Evidence Contract

> Date: 2026-07-02  
> Status: Approved  
> Issue: #52  
> Builds on: #48 Phase 7g LLM-backed descriptor authoring adapter, #43 Agent Memory first closure, #32 Phase 7f authoring golden scenario

## 1. Goal

Introduce a framework-level prompt evidence contract for Agent/LLM integrations.

Phase 7h is not a prompt management system, prompt runtime, or PromptOps
platform. It is a thin evidence kernel that gives prompt inputs and provider
outputs stable identity, version, hash, diagnostic, and audit metadata.

The immediate consumer is descriptor authoring from Phase 7g:

```text
DescriptorAuthoringPromptInput
  -> AgentPromptInputEvidence<DescriptorAuthoringPromptInput>
  -> canonical input hash
  -> existing prompt builder
  -> existing model client boundary
  -> AgentPromptOutputEvidence<DescriptorAuthoringModelResponse or safe output projection>
  -> parser validates expected input hash
  -> DescriptorAuthoringPlan + DescriptorDraftSet
  -> existing deterministic review / package / activation chain
```

The LLM remains a draft producer only. Prompt evidence improves traceability and
reproducibility. It does not approve review, package descriptors, activate
runtime state, or make provider output authoritative.

## 2. Current Codebase Facts

Phase 7g already introduced framework-level descriptor authoring projects:

```text
src/Runtime/Agent/
  CrestCreates.Agent.Authoring.Abstractions/
  CrestCreates.Agent.Authoring/
  CrestCreates.Agent.Authoring.Http/
```

The current prompt contracts are authoring-specific:

```text
CrestCreates.Agent.Authoring.Abstractions/Prompting/
  DescriptorAuthoringPromptInput
  DescriptorAuthoringPromptOutput
  DescriptorAuthoringModelRequest
  DescriptorAuthoringModelResponse
```

`DescriptorAuthoringPromptInput` already carries authoring payload identity
inputs such as `ContractVersion`, `TenantId`, metadata projection, memory
projection, visible descriptor refs, supported descriptor kinds, and an optional
`PromptInputHash`.

`DefaultDescriptorAuthoringPromptInputHashService` already uses the canonical
hash infrastructure instead of ad-hoc SHA-256. Its current hash artifact name
and shape version are authoring-local:

```text
DescriptorAuthoringPromptInput
DescriptorAuthoringPromptInputShapeVersion
```

`DescriptorAuthoringModelResponse` already carries observed provider metadata:

```text
ProviderName
ModelName
PromptInputHash
FailureKind
FailureDetail
```

The existing parser boundary uses an expected prompt input hash to block prompt
hash mismatches. That behavior must remain blocking.

Boundary tests already enforce that Authoring abstractions and runtime do not
reference ControlPlane, DraftContracts, Authoring.Http, Platform, or provider
SDK concerns beyond the intended provider integration project.

## 3. Non-Goals

Phase 7h must not implement:

- `IAgentPromptExecutor`;
- `IAgentPromptModelClient`;
- `IAgentPromptCompletionService`;
- a new model/provider runtime;
- a `Prompting.Http` provider project;
- prompt UI, prompt CMS, prompt marketplace, or prompt repository;
- prompt cache optimization;
- raw prompt or raw response persistence;
- review, package, activation, RuntimeGate, or ControlPlane ownership;
- a second descriptor draft model;
- a second descriptor review/package/activation path;
- provider SDK abstractions or credential resolution.

Prompt evidence is metadata and hash evidence. It is not execution authority,
review authority, activation authority, or governance approval.

## 4. Architecture Direction

Add two narrow projects under the Agent runtime area:

```text
src/Runtime/Agent/
  CrestCreates.Agent.Prompting.Abstractions/
  CrestCreates.Agent.Prompting/
```

### 4.1 Prompting.Abstractions

`CrestCreates.Agent.Prompting.Abstractions` owns reusable cross-agent prompt
evidence contracts:

```text
AgentPromptTemplateId
AgentPromptVersion
AgentPromptPurpose
AgentPromptContractVersion
AgentPromptModelProfileRef
AgentPromptProviderProfileRef
AgentPromptTemplateDescriptor
AgentPromptEvidenceCreationRequest<TPayload>
AgentPromptInputEvidence<TInput>
AgentPromptOutputEvidence<TOutput>
AgentPromptInputEvidenceSummary
AgentPromptOutputEvidenceSummary
AgentPromptProviderObservation
AgentPromptDiagnostic
AgentPromptDiagnosticCodes
IAgentPromptEvidenceFactory
IAgentPromptHashService
IAgentPromptTemplateRegistry
```

The abstractions project may depend on stable lower-level contracts needed for
canonical hash identity and diagnostics. It must not depend on Authoring,
ControlPlane, DraftContracts, Activation, RuntimeGate, Authoring.Http, Platform,
HTTP clients, provider SDKs, or provider credential types.

### 4.2 Prompting Runtime

`CrestCreates.Agent.Prompting` owns default implementations only:

```text
DefaultAgentPromptEvidenceFactory
DefaultAgentPromptHashService
InMemoryAgentPromptTemplateRegistry
AgentPromptingServiceCollectionExtensions
```

The runtime project uses the existing canonical hash infrastructure. It must not
own provider execution, HTTP request/response mapping, provider options,
credentials, review, package, activation, or governance decisions.

### 4.3 Authoring Ownership Stays Put

The following contracts and services remain owned by Authoring:

```text
DescriptorAuthoringPromptInput
DescriptorAuthoringPromptOutput
DescriptorAuthoringModelRequest
DescriptorAuthoringModelResponse
DescriptorAuthoringModelProfile
DescriptorAuthoringProviderProfile
DefaultDescriptorAuthoringPromptInputFactory
DefaultDescriptorAuthoringPromptBuilder
IDescriptorAuthoringModelClient
JsonDescriptorAuthoringOutputParser
LlmDescriptorAuthoringAgent
OpenAICompatibleDescriptorAuthoringModelClient
```

Prompting must not become a generic version of Authoring. It only supplies
evidence contracts and hash mechanics that Authoring, Memory Compression,
Memory Extraction, and future explanation prompts can reuse.

## 5. Core Contracts

Prompt identity must use semantic value objects instead of bare strings:

```csharp
public readonly record struct AgentPromptTemplateId(string Value);
public readonly record struct AgentPromptVersion(string Value);
public readonly record struct AgentPromptContractVersion(string Value);
public readonly record struct AgentPromptModelProfileRef(string Value);
public readonly record struct AgentPromptProviderProfileRef(string Value);

public enum AgentPromptPurpose
{
    DescriptorAuthoring = 1,
    MemoryCompression = 2,
    MemoryExtraction = 3,
    ReviewExplanation = 4,
    FixProposalExplanation = 5
}
```

These semantic value objects should follow the existing project value-object
style while enforcing the minimum governance rules: `Value` must not be null or
whitespace, `ToString()` should return `Value`, and any string conversion should
avoid weakening the semantic boundary. An implicit conversion to `string` may be
added only if it matches nearby value-object conventions.

`AgentPromptTemplateDescriptor` describes prompt contract metadata. It must not
store the full prompt template body:

```csharp
public sealed record AgentPromptTemplateDescriptor
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion Version { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }

    public string? Description { get; init; }
    public string? InputSchemaVersion { get; init; }
    public string? OutputSchemaVersion { get; init; }
    public bool ContainsSensitiveContent { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();
}
```

The implementation must snapshot dictionary/list inputs so callers cannot
mutate registered descriptors after construction.

## 6. Evidence Contracts

Generic evidence is useful inside runtime composition, but it must not become
the cross-boundary JSON/audit DTO. Phase 7h separates typed runtime evidence
from stable summaries.

### 6.1 Creation Request

```csharp
public sealed record AgentPromptEvidenceCreationRequest<TPayload>
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }

    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }

    public required TPayload Payload { get; init; }

    public string? TenantId { get; init; }
    public string? ActorId { get; init; }
    public string? CorrelationId { get; init; }
}
```

`TenantId`, `ActorId`, and `CorrelationId` are evidence metadata. They do not
automatically enter the hash payload. Tenant identity enters hash identity only
when it is part of the normalized payload itself, as descriptor authoring already
does through `DescriptorAuthoringPromptInput.TenantId`.

For output evidence, `TPayload` is the safe normalized output projection selected
by the owning agent boundary. For descriptor authoring Phase 7h, this may be
`DescriptorAuthoringModelResponse` only if its canonical projection excludes raw
provider response content and raw prompt content. Otherwise, Authoring must use a
safe output projection instead of hashing the model response object directly.

### 6.2 Runtime Typed Evidence

```csharp
public sealed record AgentPromptInputEvidence<TInput>
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }

    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }

    public required TInput Input { get; init; }
    public required CanonicalHash InputHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public string? TenantId { get; init; }
    public string? ActorId { get; init; }
    public string? CorrelationId { get; init; }

    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } =
        Array.Empty<AgentPromptDiagnostic>();
}
```

```csharp
public sealed record AgentPromptOutputEvidence<TOutput>
{
    public required AgentPromptTemplateId TemplateId { get; init; }
    public required AgentPromptVersion TemplateVersion { get; init; }
    public required AgentPromptPurpose Purpose { get; init; }
    public required AgentPromptContractVersion ContractVersion { get; init; }

    public required AgentPromptModelProfileRef ModelProfileRef { get; init; }
    public required AgentPromptProviderProfileRef ProviderProfileRef { get; init; }

    public required CanonicalHash InputHash { get; init; }
    public CanonicalHash? OutputHash { get; init; }
    public required TOutput Output { get; init; }

    public AgentPromptProviderObservation? ProviderObservation { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<AgentPromptDiagnostic> Diagnostics { get; init; } =
        Array.Empty<AgentPromptDiagnostic>();
}
```

`CreatedAt` must be assigned by `TimeProvider` in the default implementation.
Implementations must not call `DateTimeOffset.UtcNow` directly.

### 6.3 Cross-Boundary Summaries

Summaries live in `CrestCreates.Agent.Prompting.Abstractions` so Authoring,
Memory Compression, Memory Extraction, and future explanation prompts can share
the same audit contract:

```text
AgentPromptInputEvidenceSummary
AgentPromptOutputEvidenceSummary
```

The summaries carry prompt identity, profile refs, hashes, created-at metadata,
diagnostics, and optional provider observation. They must not carry generic
payloads, raw prompt strings, raw provider responses, API keys, endpoint URLs,
or provider configuration.

Public authoring results should expose summaries rather than generic evidence:

```csharp
public sealed record DescriptorAuthoringResult
{
    public AgentPromptInputEvidenceSummary? PromptInputEvidence { get; init; }
    public AgentPromptOutputEvidenceSummary? PromptOutputEvidence { get; init; }
}
```

## 7. Profile Refs and Provider Observations

Phase 7h must keep configuration identity separate from execution observation:

```text
AgentPromptModelProfileRef     = configured model profile identity
AgentPromptProviderProfileRef  = configured provider profile identity
ProviderName                   = observed provider name returned or recorded
ModelName                      = observed model name returned or recorded
```

This distinction handles future profile alias changes, provider fallback, or
model routing without rewriting evidence identity semantics.

Provider observation is optional and evidence-only:

```csharp
public sealed record AgentPromptProviderObservation
{
    public string? ProviderName { get; init; }
    public string? ModelName { get; init; }
    public string? ResponseId { get; init; }
    public string? FinishReason { get; init; }
    public long? LatencyMs { get; init; }
}
```

Provider observation must not include provider config, credentials, endpoint
URLs, request headers, raw prompt strings, or raw response strings.

## 8. Hash Design

All prompt hashes must be produced through the canonical hash infrastructure.
Phase 7h must not introduce ad-hoc SHA-256 helpers.

`IAgentPromptHashService` computes hashes over sanitized canonical content:

```csharp
public interface IAgentPromptHashService
{
    CanonicalHash ComputeInputHash<TInput>(
        AgentPromptEvidenceCreationRequest<TInput> request);

    CanonicalHash? ComputeOutputHash<TOutput>(
        AgentPromptEvidenceCreationRequest<TOutput> request,
        CanonicalHash inputHash,
        AgentPromptProviderObservation? providerObservation);
}
```

The input hash canonical payload includes:

```text
TemplateId
TemplateVersion
Purpose
ContractVersion
ModelProfileRef
ProviderProfileRef
NormalizedInput
```

The input hash canonical payload excludes:

```text
CreatedAt
Diagnostics
CorrelationId
ActorId
provider latency
raw prompt string
raw provider response string
```

Output hash canonical payload includes output evidence that is safe to hash and
the associated input hash. It excludes timestamps, diagnostics, correlation
metadata, latency, raw prompt strings, raw provider responses, credentials, and
provider configuration.

`DefaultAgentPromptHashService` must not serialize arbitrary `TInput` or
`TOutput` through reflection-based `JsonSerializer` calls. Each owning agent
boundary must provide or select an AoT-safe canonical projection path for the
payload it asks Prompting to hash.

Phase 7h uses existing canonical hash purpose names:

```text
Prompt input hash  -> SourceIdentity
Prompt output hash -> AuditEvidence
```

The prompt output hash represents provider-output traceability evidence. It is
not source identity, review approval, activation approval, or governance
approval.

The implementation should define prompt artifact names and shape versions for:

```text
AgentPromptInputEvidence
AgentPromptOutputEvidence
AgentPromptTemplateDescriptor
```

Phase 7h must add prompt artifact names through `CanonicalHashArtifactNames` or
the existing governed artifact-name mechanism. It must not extend
`CanonicalHashArtifactKind` enum for prompt artifacts.

## 9. Evidence Factory

```csharp
public interface IAgentPromptEvidenceFactory
{
    AgentPromptInputEvidence<TInput> CreateInputEvidence<TInput>(
        AgentPromptEvidenceCreationRequest<TInput> request);

    AgentPromptOutputEvidence<TOutput> CreateOutputEvidence<TOutput>(
        AgentPromptEvidenceCreationRequest<TOutput> request,
        CanonicalHash inputHash,
        AgentPromptProviderObservation? providerObservation = null);
}
```

The default factory uses `IAgentPromptHashService` and `TimeProvider`.
It snapshots diagnostics and metadata collections. It must not resolve provider
clients, execute model calls, fetch credentials, or read prompt template bodies
from external storage.

`CreateOutputEvidence` may return `OutputHash = null` when the output cannot be
safely projected into canonical hash content. A missing output hash is evidence
of unavailable output identity, not a provider success signal.

`InMemoryAgentPromptTemplateRegistry` stores
`AgentPromptTemplateDescriptor` metadata only. It must not store full prompt
template bodies, rendered prompts, external prompt content, or provider request
content.

## 10. Authoring Integration

Phase 7h updates descriptor authoring to consume Prompting evidence without
moving authoring payload contracts out of Authoring.

The target flow is:

```text
AgentAuthoringContext
  -> DefaultDescriptorAuthoringPromptInputFactory
  -> DescriptorAuthoringPromptInput
  -> IAgentPromptEvidenceFactory.CreateInputEvidence(...)
  -> DescriptorAuthoringPromptInput.PromptInputHash = evidence.InputHash
  -> DefaultDescriptorAuthoringPromptBuilder
  -> DescriptorAuthoringPromptOutput
  -> IDescriptorAuthoringModelClient
  -> AgentPromptProviderObservation from DescriptorAuthoringModelResponse
  -> safe output projection selected by Authoring
  -> IAgentPromptEvidenceFactory.CreateOutputEvidence(...)
  -> JsonDescriptorAuthoringOutputParser(ExpectedPromptInputHash = evidence.InputHash)
  -> DescriptorAuthoringResult with evidence summaries
```

For descriptor authoring, output evidence may use
`DescriptorAuthoringModelResponse` only when the canonical projection excludes
`ResponseText`, raw provider JSON, rendered prompt content, and any other raw
provider response/prompt content. If that condition is not met, Authoring must
define a smaller safe output projection for output evidence.

`DescriptorAuthoringParseContext.ExpectedPromptInputHash` remains the parser
authority for prompt hash mismatch detection. A mismatch remains `Blocked` and
must not be downgraded to a warning.

`DescriptorAuthoringPromptInputHashService` can be replaced by or wrapped around
`IAgentPromptHashService`. The migration should leave a single main hash path
after Phase 7h rather than maintaining parallel authoring-local and generic
hash implementations.

Prompt metadata should be visible in authoring diagnostics and results:

```text
TemplateId
TemplateVersion
Purpose
ContractVersion
ModelProfileRef
ProviderProfileRef
InputHash
OutputHash
ProviderName
ModelName
```

This metadata is diagnostic evidence only. It must not allow Authoring to bypass
deterministic review, package, activation request creation, activation evidence
recheck, or `IRuntimeActivationGate`.

## 11. Diagnostics

Prompt diagnostics must be semantic-string governed. Phase 7h should add a
small diagnostic code set focused on evidence creation and hash projection:

```text
AgentPromptDiagnosticCodes.TemplateDescriptorMissing
AgentPromptDiagnosticCodes.TemplateDescriptorPurposeMismatch
AgentPromptDiagnosticCodes.InputHashProjectionFailed
AgentPromptDiagnosticCodes.OutputHashProjectionFailed
AgentPromptDiagnosticCodes.OutputHashUnavailable
AgentPromptDiagnosticCodes.ProviderObservationUnavailable
AgentPromptDiagnosticCodes.PromptEvidenceCreated
```

Diagnostics are not hash identity inputs. They can explain hash projection
decisions but cannot change whether an LLM output is accepted by review or
activation. Successful input/output hash computation does not need separate
diagnostic noise because the evidence summaries already carry the hashes.

## 12. Tests

### 12.1 Boundary Tests

Add dependency boundary coverage:

```text
Prompting.Abstractions does not reference ControlPlane
Prompting.Abstractions does not reference DraftContracts
Prompting.Abstractions does not reference Authoring.Http
Prompting.Abstractions does not reference Platform
Prompting.Abstractions does not reference provider SDKs
Prompting runtime does not reference ControlPlane
Prompting runtime does not reference RuntimeActivationGate
Prompting runtime does not reference DraftContracts
Prompting runtime does not reference Authoring.Http
Prompting runtime does not reference provider SDKs
Prompting does not expose prompt executor/model client/completion interfaces
```

### 12.2 Contract Tests

Add tests for evidence and summaries:

```text
PromptInputEvidence_Carries_Template_Version_Profile_And_InputHash
PromptOutputEvidence_Carries_InputHash_OutputHash_And_ProviderObservation
PromptEvidenceSummary_Does_Not_Expose_GenericPayload
PromptEvidenceSummary_Does_Not_Expose_RawPromptOrRawResponse
PromptEvidence_Snapshots_Diagnostic_Collections
PromptTemplateDescriptor_Does_Not_Store_TemplateBody
TemplateRegistry_DoesNotStorePromptBody
ProviderObservation_Is_Separate_From_ProfileRefs
```

### 12.3 Hash Tests

Add canonical hash tests:

```text
SamePromptInputEvidence_ProducesStableInputHash
TemplateVersionChange_ChangesInputHash
PurposeChange_ChangesInputHash
ModelProfileRefChange_ChangesInputHash
ProviderProfileRefChange_ChangesInputHash
CreatedAtChange_DoesNotChangeInputHash
CorrelationIdChange_DoesNotChangeInputHash
DiagnosticsChange_DoesNotChangeInputHash
PromptOutputHash_UsesAuditEvidencePurpose
PromptOutputHash_ChangesWhenOutputProjectionChanges
PromptInputHash_DoesNotRequireRawPromptText
PromptOutputHash_DoesNotPersistRawProviderResponse
PromptArtifactNames_AreCanonicalStrings_NotEnumExtensions
PromptHashService_DoesNotUseReflectionJsonSerialization
```

### 12.4 Authoring Integration Tests

Add integration coverage around the existing Phase 7g adapter:

```text
LlmAuthoringAgent_UsesPromptEvidenceInputHash_AsParserExpectedHash
LlmAuthoringAgent_ReturnsPromptEvidenceSummaries
PromptHashMismatch_RemainsBlocked
PromptHashMismatch_IsNotDowngradedToWarning
PromptVersion_IsVisibleInAuthoringResultDiagnostics
ProviderObservation_UsesModelResponseProviderAndModelNames
PromptEvidence_DoesNotBypassReviewPackageOrActivation
```

## 13. Exit Criteria

Phase 7h is complete when:

1. `CrestCreates.Agent.Prompting.Abstractions` and
   `CrestCreates.Agent.Prompting` exist as thin prompt evidence projects.
2. Authoring-local prompt hash logic is replaced by or wrapped around the
   framework-level prompt evidence/hash service, leaving one main prompt hash
   path.
3. Descriptor authoring prompt input evidence carries template identity,
   template version, purpose, contract version, model profile ref, provider
   profile ref, and canonical input hash.
4. Descriptor authoring prompt output evidence carries input hash traceability,
   optional output hash, and optional provider observation.
5. Cross-boundary authoring results expose input/output evidence summaries, not
   generic evidence payloads.
6. Prompt hashes are computed through canonical hash infrastructure only.
7. Input hash payload excludes timestamps, diagnostics, correlation metadata,
   actor metadata, provider latency, raw prompt strings, and raw provider
   response strings.
8. Prompt output hash uses `AuditEvidence` and is documented as provider-output
   traceability evidence only.
9. Prompt artifact names are added through `CanonicalHashArtifactNames` or the
   existing governed artifact-name mechanism, not by extending
   `CanonicalHashArtifactKind`.
10. Prompt hash projection uses AoT-safe canonical projection paths and does not
    serialize arbitrary generic payloads through reflection-based JSON
    serialization.
11. Prompting does not own model execution, HTTP provider integration, provider
   SDKs, credential resolution, raw prompt cache, prompt CMS, review, package,
   activation, RuntimeGate, or ControlPlane decisions.
12. Boundary tests prove Prompting remains independent from ControlPlane,
    Activation, DraftContracts, Authoring.Http, Platform, and provider SDKs.
13. Authoring integration tests prove prompt hash mismatch remains blocked and
    prompt evidence cannot bypass deterministic review, package, activation, or
    governance.

## 14. Risks and Mitigations

### Generic evidence leaks across JSON/AOT boundaries

Generic evidence is convenient internally but awkward as a stable JSON/audit
contract. Phase 7h mitigates this by exposing
`AgentPromptInputEvidenceSummary` and `AgentPromptOutputEvidenceSummary` across
public result boundaries.

### Prompting turns into a second LLM runtime

The design forbids executor, model client, completion service, provider SDK, and
HTTP provider abstractions in Prompting. Boundary tests enforce this.

### Hash identity becomes unstable

Timestamps, diagnostics, correlation metadata, actor metadata, latency, raw
prompt strings, and raw provider responses are excluded from input hash identity.
Tests must prove these exclusions.

### Sensitive prompt content is persisted accidentally

Prompt template descriptors carry metadata only. Summaries do not expose raw
prompt or raw response content. Phase 7h does not add prompt cache persistence.

### Prompt version is mistaken for governance approval

Prompt version is contract identity for LLM input/output evidence only. It does
not imply review approval, package compatibility, activation eligibility, or
runtime gate approval.

## 15. Implementation Notes

The implementation should proceed in this order:

1. Add `CrestCreates.Agent.Prompting.Abstractions` with value objects,
   descriptors, evidence records, summaries, diagnostics, and interfaces.
2. Add `CrestCreates.Agent.Prompting` with default evidence factory, hash
   service, in-memory template registry, and DI extension.
3. Add prompt artifact names and shape versions following the existing canonical
   hash governance pattern. Do not extend `CanonicalHashArtifactKind`.
4. Integrate descriptor authoring with `IAgentPromptEvidenceFactory` and
   `IAgentPromptHashService`.
5. Select AoT-safe canonical projection paths for prompt input and output
   payloads. A future implementation may expose a typed projector contract such
   as `IAgentPromptCanonicalPayloadProjector<TPayload>`, but Phase 7h must not
   fall back to reflection-based generic JSON serialization.
6. Add evidence summaries to authoring results and diagnostics.
7. Add boundary, contract, hash, and authoring integration tests.
8. Run targeted tests first, then the full affected Agent and Boundary suites.
