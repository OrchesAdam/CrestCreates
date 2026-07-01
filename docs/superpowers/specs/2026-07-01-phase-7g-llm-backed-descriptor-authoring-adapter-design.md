# Phase 7g - LLM-backed Descriptor Authoring Adapter

> Date: 2026-07-01  
> Status: Design Approved  
> Issue: #48  
> Builds on: #43 Agent Memory first closure, #32 Phase 7f authoring golden scenario

## 1. Goal

Introduce the first framework-level LLM-backed implementation behind the
descriptor authoring boundary proven by Phase 7f.

The adapter must turn bounded authoring context into governable descriptor
draft artifacts:

```text
AgentAuthoringContext
  -> LLM-backed descriptor authoring adapter
  -> DescriptorAuthoringPlan
  -> DescriptorDraftSet
  -> existing deterministic review / package / activation chain
```

The LLM may propose plans and drafts. It must not become the governance
authority, approval authority, activation authority, runtime mutation authority,
or runtime execution engine.

## 2. Current Codebase Facts

Phase 7f already introduced a narrow sample-local authoring boundary:

```text
samples/CrestCreates.Samples.DescriptorControlPlane/Authoring/
  IDescriptorAuthoringAgent.cs
  DescriptorAuthoringPlan.cs
  DescriptorAuthoringResult.cs
  DescriptorDraftSet.cs
  FakeCompanyCertificationAuthoringAgent.cs
```

These contracts are not yet framework-level.

The input side is already framework-level:

```text
src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/
  AgentAuthoringRequest
  AgentAuthoringContext
  AgentMemoryPack
```

`AgentAuthoringContext` contains:

```text
AgentAuthoringRequest
MetadataContextPack
AgentMemoryPack
Diagnostics
```

`AgentMemoryPack` is non-authoritative recalled context. It already carries
hash-related fields that can support prompt identity:

```text
ScopeFingerprint
VisibleMemorySetHash
CanonicalPackHash
```

The governance and activation mainline is already established:

```text
IAgentControlPlaneToolService
IDescriptorActivationRequestService
IActivationEvidenceRechecker
IRuntimeActivationGate
```

`IRuntimeActivationGate` is explicitly the only runtime mutation point. Phase
7g must preserve that invariant.

## 3. Non-Goals

Phase 7g must not implement:

- a general autonomous agent framework;
- tool-calling runtime;
- MCP, HTTP, CLI, TUI, or UI authoring adapter;
- vector search;
- LLM-backed memory compression or extraction;
- production prompt management platform;
- automatic activation approval;
- direct active descriptor patching;
- runtime registry hot reload;
- runtime handler execution by the LLM;
- a second DescriptorDraft model;
- a second draft review service;
- a second package preview or activation request path.

## 4. Architecture Direction

Add framework-level authoring projects:

```text
src/Runtime/Agent/
  CrestCreates.Agent.Authoring.Abstractions/
  CrestCreates.Agent.Authoring/

tests/Runtime/Agent/
  CrestCreates.Agent.Authoring.Tests/
```

### 4.1 Abstractions Project

`CrestCreates.Agent.Authoring.Abstractions` owns stable authoring contracts:

```text
IDescriptorAuthoringAgent
DescriptorAuthoringPlan
DescriptorAuthoringResult
DescriptorDraftSet
DescriptorAuthoringDiagnostic
DescriptorAuthoringDiagnosticCodes
DescriptorAuthoringPromptInput
DescriptorAuthoringPromptOutput
DescriptorAuthoringModelRequest
DescriptorAuthoringModelResponse
DescriptorAuthoringModelProfile
DescriptorAuthoringProviderProfile
```

Dependency direction:

```text
CrestCreates.Agent.Authoring.Abstractions
  -> CrestCreates.Agent.Memory.Abstractions
  -> CrestCreates.DescriptorDraft.Abstractions
  -> CrestCreates.Metadata.Abstractions
  -> CrestCreates.Metadata.ContextPack.Abstractions
  -> CrestCreates.Snapshot.Abstractions
```

The abstractions project must not reference:

```text
CrestCreates.Agent.ControlPlane.Abstractions
CrestCreates.Agent.ControlPlane
CrestCreates.Capability.Runtime
CrestCreates.Workflow.Runtime
CrestCreates.HumanTask.Runtime
```

### 4.2 Runtime Project

`CrestCreates.Agent.Authoring` owns the default LLM adapter runtime:

```text
LlmDescriptorAuthoringAgent
DefaultDescriptorAuthoringPromptInputFactory
DefaultDescriptorAuthoringPromptBuilder
JsonDescriptorAuthoringOutputParser
FakeDescriptorAuthoringModelClient
RecordedDescriptorAuthoringModelClient
```

It also owns service registration:

```text
AddAgentAuthoring()
```

Dependency direction:

```text
CrestCreates.Agent.Authoring
  -> CrestCreates.Agent.Authoring.Abstractions
  -> Microsoft.Extensions.DependencyInjection.Abstractions
  -> Microsoft.Extensions.Logging.Abstractions
```

Provider-specific SDKs may be referenced only by real provider client
implementations behind `IDescriptorAuthoringModelClient`. Provider SDK types
must not leak into public authoring contracts.

## 5. Productizing the Phase 7f Boundary

The first implementation slice should move the sample-local authoring contracts
into `CrestCreates.Agent.Authoring.Abstractions`.

Framework contracts:

```text
IDescriptorAuthoringAgent
DescriptorAuthoringPlan
DescriptorAuthoringResult
DescriptorDraftSet
```

`DescriptorDraftSet` continues to contain existing
`CrestCreates.DescriptorDraft.Abstractions.DescriptorDraft` values. There is no
new draft domain model.

After the framework contracts exist, the sample fake agent must implement the
framework interface:

```text
IDescriptorAuthoringAgent
  -> FakeCompanyCertificationAuthoringAgent
  -> LlmDescriptorAuthoringAgent
```

The Phase 7f sample runner remains the orchestration layer that saves drafts,
reviews them, builds reports, creates package evidence, submits activation
handoff, and proves runtime behavior. The LLM adapter does none of that work.

## 6. Authoring Contract

The main authoring interface is:

```csharp
public interface IDescriptorAuthoringAgent
{
    Task<DescriptorAuthoringResult> AuthorAsync(
        AgentAuthoringContext context,
        CancellationToken cancellationToken = default);
}
```

The only input is `AgentAuthoringContext`.

The result carries:

```text
DescriptorAuthoringPlan Plan
DescriptorDraftSet DraftSet
IReadOnlyList<DescriptorAuthoringDiagnostic> Diagnostics
DescriptorAuthoringStatus Status
```

`DescriptorAuthoringStatus` must distinguish:

```text
Succeeded
Failed
Blocked
InvalidProviderOutput
ProviderUnavailable
```

`DescriptorAuthoringDiagnostic` must use `DiagnosticCode` and
`SeverityLevel`, matching the platform diagnostic style.

## 7. LLM Adapter Data Flow

The default LLM adapter flow is:

```text
AgentAuthoringContext
  -> DescriptorAuthoringPromptInput
  -> DescriptorAuthoringPromptOutput
  -> DescriptorAuthoringModelRequest
  -> IDescriptorAuthoringModelClient
  -> DescriptorAuthoringModelResponse
  -> JsonDescriptorAuthoringOutputParser
  -> DescriptorAuthoringPlan
  -> DescriptorDraftSet
  -> DescriptorAuthoringResult
```

### 7.1 Prompt Input

`DescriptorAuthoringPromptInput` is structural, stable, and canonical-hashable.

It includes:

```text
ContractVersion
TenantId
IntentText
MetadataContextPack
AgentMemoryPack
VisibleDescriptorRefs
SupportedDescriptorKinds
```

The prompt input hash is the deterministic fixture key.

Fixture identity:

```text
PromptInputHash
+ ContractVersion
+ PromptTemplateVersion
+ ModelProfileName
```

The implementation must use the existing canonical hash runtime. It must not
add ad-hoc SHA256, pipe-delimited string hashing, or helper-style hash utilities.

### 7.2 Prompt Output

`DescriptorAuthoringPromptOutput` contains:

```text
ContractVersion
PromptTemplateVersion
PromptInputHash
SystemPrompt
UserPrompt
```

The prompt output is not an authority boundary. It is reproducible evidence for
the model request and recorded fixtures.

### 7.3 Model Client Boundary

Use a narrow client abstraction:

```csharp
public interface IDescriptorAuthoringModelClient
{
    Task<DescriptorAuthoringModelResponse> CompleteAsync(
        DescriptorAuthoringModelRequest request,
        CancellationToken cancellationToken = default);
}
```

Model profiles describe model behavior without exposing provider SDK types:

```text
DescriptorAuthoringProviderProfile
  ProviderName
  Endpoint
  Timeout

DescriptorAuthoringModelProfile
  ProfileName
  ProviderName
  ModelName
  MaxInputTokens
  MaxOutputTokens
  SupportsJsonMode
  SupportsStructuredOutput
```

Phase 7g includes one real provider client behind this interface, but tests and
CI must use fake or recorded clients. Live provider access is not required for
deterministic verification.

## 8. Output Parser

The LLM response must not deserialize directly into `DescriptorDraftSet`.

Use an intermediate plan DTO:

```text
LLM JSON
  -> DescriptorAuthoringPlanDto
  -> DescriptorAuthoringPlan
  -> DescriptorDraftSet
```

The parser must validate:

- contract version match;
- prompt input hash match;
- known descriptor kind;
- valid draft payload discriminator;
- supported draft operation;
- no activation request;
- no approval decision;
- no runtime mutation request;
- no runtime handler execution request;
- no Control Plane tool invocation request;
- no memory claim overriding `MetadataContextPack`.

Invalid output returns structured diagnostics. It must not escape as an
uncontrolled exception from `LlmDescriptorAuthoringAgent.AuthorAsync`.

## 9. Governance Boundaries

`LlmDescriptorAuthoringAgent` produces only:

```text
DescriptorAuthoringPlan
DescriptorDraftSet
DescriptorAuthoringDiagnostics
```

It must not:

- save drafts;
- review drafts;
- build review reports;
- suggest or apply fix proposals;
- create package previews;
- bind package evidence;
- submit activation requests;
- approve activation requests;
- call `IRuntimeActivationGate`;
- execute runtime handlers;
- mutate runtime registries.

Forbidden dependencies in authoring projects:

```text
IAgentControlPlaneToolService
IDescriptorActivationRequestService
IRuntimeActivationGate
ICapabilityHandlerResolver
IWorkflowEngine
IHumanTaskRegistry mutation APIs
runtime handler resolvers
runtime execution services
```

The valid post-authoring flow remains:

```text
DescriptorDraftSet
  -> IDescriptorDraftStore
  -> IDescriptorDraftReviewService
  -> review report / fix proposal
  -> package preview / evidence binding
  -> activation handoff
  -> HumanTask review when required
  -> RuntimeActivationGate
```

## 10. Memory Authority Rule

`AgentMemoryPack` is recalled context only.

If memory conflicts with authoritative context, memory loses:

```text
MetadataContextPack > AgentMemoryPack
ReviewResult > AgentMemoryPack
ActivationEvidence > AgentMemoryPack
LifecycleGovernance > AgentMemoryPack
AuthorizationPolicy > AgentMemoryPack
RuntimeActivationGate > AgentMemoryPack
```

The prompt builder must explicitly state this rule. The output parser must
diagnose any model output that tries to treat memory as authoritative over the
metadata context.

## 11. Error Handling

The adapter returns structured authoring results for expected failures:

| Failure | Required behavior |
| --- | --- |
| Provider timeout | `ProviderUnavailable` or `Failed` with diagnostic |
| Provider returns malformed JSON | `InvalidProviderOutput` with parser diagnostic |
| Prompt input hash mismatch | `Blocked` with hash mismatch diagnostic |
| Unknown descriptor kind | `Blocked` with descriptor kind diagnostic |
| Unsupported draft operation | `Blocked` with operation diagnostic |
| Activation / approval / runtime mutation request | `Blocked` with governance boundary diagnostic |
| Memory conflicts with metadata | Metadata wins; diagnostic records ignored memory claim |

Unexpected exceptions may be logged internally, but public results should remain
structured unless the caller's cancellation token is canceled.

## 12. AOT and Serialization

Public DTOs should remain AoT-friendly:

- no `dynamic`;
- no `object` payloads in public contract models;
- no provider SDK request/response types in public contracts;
- source-generated `JsonSerializerContext` for authoring DTOs and parser DTOs;
- stable discriminators for descriptor draft payloads;
- immutable collection surfaces.

The authoring parser may use `JsonElement` internally when validating unknown
provider output, but the public result should be projected into typed DTOs and
diagnostics.

## 13. Testing Strategy

Add:

```text
tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/
```

Minimum tests:

```text
Contracts_Are_FrameworkNamespace_NotSampleNamespace
AuthoringAbstractions_DoNotReference_ControlPlane
AuthoringRuntime_DoNotReference_ControlPlane_Or_RuntimeExecution
LlmAgent_Consumes_Only_AgentAuthoringContext
PromptInputHash_IsStable
PromptInputHash_Changes_When_MetadataContextPack_Changes
PromptInputHashMismatch_IsRejected
InvalidJson_ReturnsParserDiagnostics
UnknownDescriptorKind_IsRejected
RuntimeOperationRequest_IsRejected
AgentMemoryPack_IsNonAuthoritative_MetadataWins
RecordedFixture_ProducesStableDraftSet
LlmResult_Output_IsDraftSet_NotActiveDescriptor
GoldenScenario_LlmFixture_StillUsesGovernanceMainline
```

Boundary tests should inspect assembly references and constructor dependencies.
If the authoring runtime references Control Plane, activation services, runtime
registries, or runtime handler resolvers, the tests should fail.

The golden scenario extension should add a fixture-backed path beside the
deterministic fake path. It must prove that fixture output still flows through
the existing review, package, evidence, activation handoff, HumanTask review
when required, and `RuntimeActivationGate` chain.

No test should require live provider access.

## 14. Implementation Slices

### Slice A - Framework Authoring Contracts

- Add `CrestCreates.Agent.Authoring.Abstractions`.
- Move framework-safe versions of the Phase 7f authoring contracts.
- Add authoring diagnostics and status models.
- Update sample fake authoring agent to implement the framework interface.
- Keep the sample golden scenario behavior unchanged.

### Slice B - Deterministic LLM Adapter Skeleton

- Add `CrestCreates.Agent.Authoring`.
- Add prompt input factory, prompt builder, model client abstraction, output
  parser, fake model client, and recorded fixture model client.
- Add prompt input canonical hash tests.
- Add parser diagnostics tests.

### Slice C - Provider Profile and Real Client Boundary

- Add provider and model profile contracts.
- Add one real provider client behind `IDescriptorAuthoringModelClient`.
- Keep live provider execution out of CI.
- Keep provider SDK details out of public contracts.

### Slice D - Golden Scenario Fixture Integration

- Add fixture-backed LLM path for the Company Certification sample.
- Reuse the existing sample runner and deterministic governance chain.
- Prove fixture output cannot bypass review, evidence, activation handoff, or
  runtime gate.

## 15. Acceptance Criteria

Phase 7g is complete when:

- framework-level authoring contracts exist outside sample namespaces;
- the Phase 7f fake agent implements the framework `IDescriptorAuthoringAgent`;
- `LlmDescriptorAuthoringAgent` can be configured behind the same interface;
- the LLM adapter consumes only `AgentAuthoringContext`;
- the LLM adapter produces `DescriptorAuthoringPlan` and `DescriptorDraftSet`;
- prompt input is canonical-hashable and fixture-friendly;
- model output is parsed through deterministic diagnostics;
- fake and recorded model clients make tests deterministic;
- one real provider client boundary exists without leaking provider SDK types;
- produced drafts still flow through existing review, report/fix, package
  preview, evidence binding, activation handoff, HumanTask review when required,
  and `RuntimeActivationGate`;
- boundary tests prove the adapter cannot depend on governance or runtime
  mutation services.

The success condition is not that the LLM is always correct. The success
condition is that LLM output becomes a deterministic, diagnosable, governable
draft artifact inside the existing descriptor lifecycle.
