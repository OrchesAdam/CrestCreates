# Phase 9b+ Agent Memory Accountability Integration Implementation Plan

> Implement Issue #56 through ordered Case-first TDD slices. The approved Spec
> is normative. This Plan fixes project placement, contract cutovers, causal
> ownership, exact test runners, and NativeAOT evidence without reopening the
> Spec.

**Goal:** Project known Agent Memory Recall, Curation, and Source Expansion
results into the unified Accountability write chain with exact fact replay
semantics, caller-visible-only hashes, conditional curation, bounded
best-effort recording, and Agent Tool/MCP causal containment.

**Spec:** `docs/superpowers/specs/2026-08-11-phase-9bplus-agent-memory-accountability-integration-design.md`

**Issue:** #56

**Branch:** `agents/agent-memory-accountability-integration`

**Spec status:** APPROVED

**Plan status:** IMPLEMENTED — reviewed implementation head `2ba2f775` passed the
full PR CI matrix in run `31663216616`, including PostgreSQL composition,
Capability/Agent/MCP suites, dependency boundaries, and NativeAOT publish-link-run.

```text
Memory fact identity:       fresh OperationId + OccurredAt per admitted execution
Exact Duplicate:            complete established safe fact republished unchanged
Cross-execution ID reuse:   same AuditId + changed RecordHash -> Conflict
Direct causal parent:       current Capability execution/accountability scope
Recall evidence:            effective-visible content hashes + EffectivePackHash
Forbidden Recall inputs:    MemoryId/Handle/SourceRef/DescriptorRef/all Retriever hashes
Curation mainline:          ConfirmedAtomic conditional Promote/Reject/Supersede/Archive
Delivery:                   one bounded post-result Recorder attempt
Serialization:              exact source-generated payload roots only
```

---

## 1. Execution Rules

- Run shell commands through `rtk`; use `apply_patch` for source/document edits.
- Before the first build/test command in an implementation session, run:

  ```bash
  rtk --version
  rtk dotnet --info
  rtk git status --short --branch
  ```

- Preserve unrelated worktree changes. Move retired files to
  `99_RecycleBin/Phase9bPlusAgentMemoryAccountability/`; never delete directly.
- Begin every Task with the named Red tests. Red must fail because the contract
  or behavior is missing, not because DI or the fixture cannot start.
- A public/shared signature cutover is not Green until every production caller
  is migrated in the same Task and every caller project compiles. Before editing
  a signature, enumerate its call sites with `rtk rg`; add the discovered caller
  files to that Task's change set. Task 7 verifies end-to-end causality; it is
  not a deferred compile-repair Task for Tasks 1, 4, or 5.
- Make the smallest mainline change for Green, then run the focused project,
  dependency boundaries where relevant, and `rtk git diff --check`.
- Do not restore `VisibleMemorySetHash`, ScopeFingerprint, Retriever
  CanonicalPackHash, domain CanonicalContentHash, MemoryId, or Handle as Recall
  Accountability inputs.
- Do not make Agent/MCP logical InvocationId the Memory Accountability
  OperationId. Allocate once per admitted Memory execution.
- Never use `Guid.NewGuid()`, `UtcNow`, CorrelationId,
  `AgentMemoryArtifactOrigin.OperationId`, Capability StartedAt, or a payload
  hash as a temporary caller-migration fallback. Production callers must use
  `IAgentMemoryOperationIdentityFactory` or an explicit trusted-host identity
  in the same Task that changes their contract.
- Do not make Capability ExecutionId/AuditId replayable. A fresh Capability
  execution is a distinct fact parent.
- Once a terminal/effective Memory result is established, all
  Accountability-only work is inside one post-result best-effort fence:
  Accountability hash projection, payload/fact construction, and producer
  invocation. No exception from that work may replace the established Memory
  result or its original exception. Source Expansion sanitization, truncation,
  and caller-visible hash/result construction remain before this boundary and
  fail closed; only its subsequent Accountability projection is fenced.
- Do not retain legacy non-conditional Promote/Reject/Supersede/Archive paths.
- Do not add Store audit hooks, a Memory audit table/sink, reliable delivery,
  outbox, mutation/audit atomicity, or business replay claims.
- Do not update `memory.md` to Implemented or NativeAOT-verified until the
  original linked native binaries execute the new paths successfully.

---

## 2. Ordered Delivery Map

| Task | Deliverable | Required Red evidence | Must not include |
|---|---|---|---|
| 1 | Public identity/payload/producer contracts and null path | identity, JSON-root, forbidden-field cases | real Recorder bridge |
| 2 | Complete conditional curation cutover including Archive | atomic Archive, provider/startup, legacy-path guards | Accountability Store hooks |
| 3 | Real bridge, exact rules, deterministic AuditId, bounded recording | Accepted/Duplicate/Conflict, rule, timeout, DI cases | Memory orchestration changes |
| 4 | Recall effective-result integration | H01/H02/B01/B02/F01/F02/S01/S03/S04 | any internal identity/set hash |
| 5 | Source Expansion safety closure and projection | H07/B05/B06/F09/S02 | sanitizer/domain hash reuse |
| 6 | Curation post-result projection | H03-H06/F03/F04/F10/S05/S06 | false unknown-outcome facts |
| 7 | Agent Tool/MCP/direct-host causal composition | C01/C02/B08/F12/F13/C10 | second correlation chain |
| 8 | Durable sink, architecture, NativeAOT, full evidence | C03-C09 and original binaries | overclaiming #25/#55 |

Do not begin the next Task while the prior Task has unresolved Red cases,
unmigrated production callers, non-compiling caller projects, or
review-guardrail violations.

---

## 3. Final Project and Dependency Changes

### 3.1 Create production bridge

```text
src/Runtime/Agent/CrestCreates.Agent.Memory.Accountability/
  CrestCreates.Agent.Memory.Accountability.csproj
  AgentMemoryAccountabilityServiceCollectionExtensions.cs
  Bootstrap/AgentMemoryAccountabilityCompositionValidator.cs
  CanonicalHashing/AgentMemoryAccountabilityAuditIdProjector.cs
  Options/AgentMemoryAccountabilityOptions.cs
  Production/AgentMemoryAccountabilityProducer.cs
  Sanitization/AgentMemoryRecallPayloadSanitizationRule.cs
  Sanitization/AgentMemoryCurationPayloadSanitizationRule.cs
  Sanitization/AgentMemorySourceExpansionPayloadSanitizationRule.cs
```

The bridge references:

```text
CrestCreates.Agent.Memory.Abstractions
CrestCreates.Accountability.Abstractions
CrestCreates.Metadata.Abstractions
Microsoft.Extensions.DependencyInjection.Abstractions
Microsoft.Extensions.Hosting.Abstractions
Microsoft.Extensions.Logging.Abstractions
```

It must not reference concrete Accountability, Agent Tools, MCP, PostgreSQL,
ASP.NET Core, Platform, or any Memory Store implementation.

### 3.2 Create test project

```text
tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests/
  CrestCreates.Agent.Memory.Accountability.Tests.csproj
  Identity/AgentMemoryAccountabilityIdentityTests.cs
  Contracts/AgentMemoryAccountabilityPayloadContractTests.cs
  Production/AgentMemoryAccountabilityProducerTests.cs
  Sanitization/AgentMemoryPayloadSanitizationRuleTests.cs
  Composition/AgentMemoryAccountabilityCompositionTests.cs
  Architecture/AgentMemoryAccountabilityArchitectureTests.cs
```

Add both projects to `CrestCreates.slnx` and
`solutions/CrestCreates.All.slnx` in Task 3, when the bridge first compiles.

### 3.3 Modify existing ownership boundaries

- `Agent.Memory.Abstractions` owns operation identity, typed payloads, producer
  interface, exact JSON context, and ReadCore operation-request contracts.
- `Agent.Memory` owns the default identity factory, null producer, conditional
  curation orchestration, and runtime composition validation.
- `AddAgentMemoryReadRuntime()` owns `TimeProvider`,
  `DefaultAgentMemoryOperationIdentityFactory`,
  `NullAgentMemoryAccountabilityProducer`, canonical hashing, sanitization,
  retriever/expander/base Store registrations, and all other non-curation
  primitives. Recall and Expansion therefore remain independently composable
  when neither formal curation nor the real Accountability bridge is enabled.
- `AddAgentMemoryCuration()` owns the Promotion Service, curation capabilities,
  explicit formal-curation marker, and curation composition validator.
  `AddAgentMemoryRuntime()` is exactly ReadRuntime + Curation.
- `Agent.Memory.ReadCore` owns effective-visible hash projection and Recall /
  Expansion fact construction.
- `Agent.Memory.Accountability` alone owns AuditEnvelope mapping, AuditId,
  payload rules, timeout, diagnostics, and bridge activation.
- Agent Tool and MCP adapters only capture the authoritative Capability context,
  allocate a fresh Memory operation identity, and call the shared mainline.

---

## 4. Task 1 — Freeze Public Contracts and Null Runtime

**Files:**

- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/Accountability/AgentMemoryOperationIdentity.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/Accountability/AgentMemoryAccountabilityPayloads.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/Accountability/IAgentMemoryAccountabilityProducer.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/Json/AgentMemoryAccountabilityJsonSerializerContext.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryContracts.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryInterfaces.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Identity/DefaultAgentMemoryOperationIdentityFactory.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Accountability/NullAgentMemoryAccountabilityProducer.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/Handlers/AgentMemoryCurationHandlerHelpers.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/Handlers/PromoteMemoryCandidateHandler.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/Handlers/RejectMemoryCandidateHandler.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/Handlers/SupersedeMemoryItemHandler.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/ContractTests.cs`
- Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/AccountabilityContractTests.cs`
- Modify: affected Tool/Memory tests discovered by the pre-cutover `rtk rg`

### Red

- Add only contract-local identity cases: required/non-default OperationId +
  OccurredAt, one factory allocation per admitted Memory execution, request
  snapshot ownership, absence of Timestamp, and no correlation/origin fallback.
- Do not add `SameOperationDifferentFact_Should_UseSameAuditId`,
  `ChangedOccurredAtForSameOperationId_Should_Conflict`,
  `OccurredAt_Should_BeExcludedFromAuditIdAndIncludedInRecordHash`, Tenant/
  payload-version AuditId isolation, or Duplicate/Conflict tests here. Those
  require the Task 3 AuditId projector and Recorder/Sink semantics.
- Prove payload roots are exact, unknown members fail, null fields follow the
  v1 matrix, and no payload exposes raw content, SourceRefs, DescriptorRefs,
  MemoryIds, Handles, RuleSet/version, TraceAttributes, Reason, or Explanation.
- Prove `AgentMemoryOperationRequest.Timestamp` is replaced by required
  `Identity`, and the producer cannot accept a separate timestamp.
- Prove standalone runtime resolves the null producer and one identity factory.

Run and expect Red:

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~AccountabilityContractTests|FullyQualifiedName~ContractTests"
```

### Green

- Add the exact three payload roots and sanitization-summary contract.
- Add `AgentMemoryOperationIdentity` and
  `IAgentMemoryOperationIdentityFactory`; the default implementation captures
  OperationId + OccurredAt exactly once using Host `TimeProvider`.
- Add the producer interface with typed methods and no business cancellation
  token.
- Add the source-generated JSON context with camelCase, null omission, and
  unmapped-member rejection; no open/object payload root.
- Replace the loose curation timestamp and migrate every production caller in
  this Task. Curation handlers call the registered identity factory exactly once
  per admitted handler execution and pass the pair into
  `AgentMemoryCurationHandlerHelpers`; the helper may not sample a clock or
  reconstruct an ID.
- Preserve the current callable surface only through the new legal contract;
  do not add a temporary Timestamp overload or default Identity initializer.
- Register the default identity factory and null producer with `TryAdd*`.

### Verify

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
rtk dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Tools
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
rtk git diff --check
```

---

## 5. Task 2 — Cut Over Complete Conditional Curation

**Files:**

- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/AgentMemoryInterfaces.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory/Promotion/DefaultAgentMemoryPromotionService.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory/Stores/InMemoryAgentMemoryStore.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Bootstrap/AgentMemoryCurationCompositionValidator.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/AgentMemoryToolServiceCollectionExtensions.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/MainChainTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/BoundaryTests.cs`
- Create/Modify: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/AgentMemoryRuntimeRegistrationTests.cs`
- Modify: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests/AgentMemoryToolStartupTests.cs`
- Modify: every existing `AddAgentMemoryRuntime()` host/fixture whose actual
  surface is read-only, as identified by the mandatory registration inventory

### Red

- Add `AgentMemoryConditionalArchiveContractTests` from Spec §15.3.
- Add startup cases proving null producer does not exempt an
  `IAgentMemoryStore`-only or partial conditional provider.
- Add explicit registration cases proving `AddAgentMemoryReadRuntime()` does
  not enable formal curation or its validator, while `AddAgentMemoryCuration()`
  and the compatibility aggregate `AddAgentMemoryRuntime()` do.
- Add `ReadRuntime_ShouldResolveIdentityFactoryAndNullProducer` and
  `ReadRuntime_ShouldNotResolveFormalCurationMarker`.
- Prove both direct `IBootstrapValidator.Validate()` and hosted
  `IHostedService.StartAsync()` execute the same curation validation and fail
  closed for an invalid formal-curation composition.
- Add `CancelledBeforeTransition_Should_NotCommit` with a conditional Store
  double that observes the original business token and proves zero writes.
- Add an architecture assertion that `LegacyPromote`, `LegacySupersede`,
  Get/Save Archive, and fallback branches no longer exist after Green.

### Green

- Add conditional `ArchiveAsync` with `AgentMemoryItemExpectation` to
  `IAgentMemoryConditionalCurationStore`.
- Implement atomic Active/Superseded -> Archived CAS in the InMemory Store.
- Make `DefaultAgentMemoryPromotionService.OutcomeGuarantee` ConfirmedAtomic
  only for a Store advertising ConfirmedAtomic and implementing the now-complete
  conditional interface.
- Make `AgentMemoryCanonicalHashProjector` a required, non-null constructor
  dependency of `DefaultAgentMemoryPromotionService`; remove nullable checks and
  every `_hashes is null` fallback branch.
- Route every convenience overload through hash projection + conditional
  transition; move any retired standalone legacy file to the recycle bin.
- Preserve the business cancellation token through conditional Store execution
  until a committed/rejected/conflict terminal outcome is known. Do not replace
  it with `CancellationToken.None` before the transition. Cancellation or
  timeout in an indeterminate durable-provider interval remains unknown outcome.
- Split registration explicitly:
  `AddAgentMemoryReadRuntime()` registers `TimeProvider`, the default operation
  identity factory, the null Accountability producer, hashing, sanitization,
  retriever/expander/base Stores, and other non-curation primitives, but no
  Promotion Service, formal-curation marker, or curation validator;
  `AddAgentMemoryCuration()` registers the canonical Promotion Service,
  capabilities, a formal-curation marker, and its startup validator;
  existing `AddAgentMemoryRuntime()` composes both and therefore means complete
  formal curation. Do not infer enablement from an incidental
  `IAgentMemoryPromotionService` registration.
- Implement `AgentMemoryCurationCompositionValidator` as both
  `IBootstrapValidator` and `IHostedService`, register the same singleton on
  both surfaces, and route both entry points through one validation core. Add a
  direct `Microsoft.Extensions.Hosting.Abstractions` package reference to the
  runtime project; do not rely on a transitive reference or invent a third
  startup mechanism.
- Run ConfirmedAtomic validation only when the explicit curation marker is
  present, independent of real/null Accountability producer. Keep the Agent
  Tool check as a repeated composition assertion only.
- Before Green, run `rtk rg -n 'AddAgentMemoryRuntime\('` across source,
  tests, and samples. Classify every result by actual Recall/Expansion versus
  curation use, migrate each truly read-only host/fixture to
  `AddAgentMemoryReadRuntime()` in this Task, and compile/test every modified
  caller project. Do not migrate lifecycle/fixture cases that actually resolve
  or execute the Promotion Service.
- The pre-Plan inventory already identifies MCP Memory E2E/AOT Recall/
  Expansion composition and the non-lifecycle LLM registration cases as
  read-only candidates. Re-check their behavior at implementation time; keep
  the LLM full lifecycle, Agent Tool curation, and any Control Plane scenario
  that resolves Promotion on the aggregate runtime.

### Verify

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests --filter "FullyQualifiedName~Startup"
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk git diff --check
```

---

## 6. Task 3 — Build the Real Accountability Bridge

**Files:** Create the production/test projects from §3; modify both solution
files and only the existing Accountability registration tests needed to verify
extension-rule discovery.

### Red

- Add the deferred identity/record tests from Spec §15.1:
  `SameOperationSameFact_Should_BeStable`,
  `SameOperationDifferentFact_Should_UseSameAuditId`,
  `EstablishedFactRepublish_Should_ReuseOperationIdAndOccurredAt`,
  `ChangedOccurredAtForSameOperationId_Should_Conflict`,
  `OccurredAt_Should_BeExcludedFromAuditIdAndIncludedInRecordHash`, Tenant and
  payload-version isolation, correlation-not-operation-identity, and canonical
  AuditId projection.
- Prove exact established fact republication reaches Duplicate and any changed
  complete Envelope field—including OccurredAt, correlation, cause, parent,
  Actor, Runtime, references, or payload—reaches Conflict.
- Add exact rule tests for all field matrices, protected fields, bounds,
  unknown members/kinds/versions, sorted codes, and generated JSON only.
- Add producer tests for Accepted, Duplicate, Conflict, Rejected, NoSink,
  timeout, thrown recorder, original-result isolation, and safe diagnostics.
- Add composition tests proving enabling the bridge with null producer or
  missing Accountability runtime fails startup.
- Prove `AgentMemoryAccountabilityCompositionValidator` fails closed with the
  same result through direct `IBootstrapValidator.Validate()` and hosted
  `IHostedService.StartAsync()` execution.

### Green

- Implement the canonical AuditId projector using `ICanonicalHashComputer` and
  the exact Spec §9.1 shape.
- Implement three exact `IAuditPayloadSanitizationRule` types; never fall back
  to candidate JSON after rejection.
- Implement one real producer mapping typed Memory facts into AuditEnvelope,
  validating payload/context/identity agreement before Recorder.
- Implement one independent finite `WriteTimeout` attempt without reusing the
  business token and without an internal retry loop.
- `AddAgentMemoryAccountability()` replaces the null producer, registers the
  AuditId projector/rules/options/validator, requires the runtime marker and
  Recorder, and never registers a sink or calls `AddAccountability()`.
- Implement `AgentMemoryAccountabilityCompositionValidator` as both
  `IBootstrapValidator` and `IHostedService`; register one singleton on both
  surfaces and route them through one validation core, matching the established
  Capability/Workflow startup pattern.

### Verify

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests
rtk dotnet test tests/Runtime/Audit/CrestCreates.Accountability.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk git diff --check
```

---

## 7. Task 4 — Integrate Recall at the Effective Result Boundary

**Files:**

- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions/ReadCore/AgentMemoryReadCore.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore/Accountability/AgentMemoryEffectiveResultHashProjector.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore/ReadCore/AgentMemoryReadCore.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore/ReadCoreServiceCollectionExtensions.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/Handlers/BuildAgentMemoryPackHandler.cs`
- Modify: `src/Integrations/CrestCreates.Mcp.Memory/Handlers/MemoryRecallHandler.cs`
- Modify: any additional production `IAgentMemoryReadCore.RecallAsync` caller
  found by the mandatory pre-cutover `rtk rg`
- Modify/Create: `tests/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore.Tests/ReadCore/AgentMemoryRecallAccountabilityTests.cs`

### Red

- Add all Recall cases from Spec §15.2, especially:
  `HiddenQueryMemoryId_Should_NotEnterAnyAccountabilityHash`,
  `InternalMemoryId_Should_NotEnterEffectivePackProjection`,
  `RetrieverHashes_Should_NotBeReused`, and domain-provenance hash separation.
- Add `RecallProjectionFailure_Should_NotChangeEstablishedResult`, injecting a
  failure before the producer call and proving the exact Recall result survives.
- Assert the effective-content shape contains only TenantId + exact returned
  Content and that EffectivePackHash contains only Spec §7.6 fields.

### Green

- Replace the loose ReadCore signature with
  `AgentMemoryRecallOperationRequest`, retaining ArtifactOrigin separately from
  Memory Accountability identity/context.
- In this same Task, migrate every Agent Tool/MCP/host production caller to the
  operation request. Each admitted Capability handler uses the registered
  identity factory exactly once and maps invocation context from the current
  authoritative Capability context; no compatibility overload remains.
- After defense filtering, artifact preparation, and final result construction,
  enter the post-result best-effort fence and project effective-visible hashes
  from exact returned Content in result order.
- Inside that same fence, build EffectivePackHash without MemoryId, Handle,
  SourceRef, DescriptorRef, any Retriever hash, or domain CanonicalContentHash;
  then construct the typed fact/payload and invoke the producer.
- Construct completed/rejected Recall Accountability only from trusted bounded
  inputs. Provider/unknown failures emit no fabricated result fact, and no
  effective-hash, payload/fact-projector, or producer exception may replace the
  exact established result/original exception.

### Verify

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore.Tests --filter "FullyQualifiedName~Recall"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore.Tests
rtk dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Tools
rtk dotnet build src/Integrations/CrestCreates.Mcp.Memory
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.Memory.Tests
rtk git diff --check
```

---

## 8. Task 5 — Close Source Expansion Safety and Projection

**Files:**

- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Projection.Abstractions/ReadCore/AgentMemoryReadCore.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore/ReadCore/AgentMemorySourceExpandCore.cs`
- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/Handlers/ExpandAgentMemorySourceHandler.cs`
- Modify: `src/Integrations/CrestCreates.Mcp.Memory/Handlers/McpSourceExpandHandler.cs`
- Modify: any additional production `IAgentMemorySourceExpandCore.ExpandAsync`
  caller found by the mandatory pre-cutover `rtk rg`
- Create/Modify: `tests/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore.Tests/ReadCore/AgentMemorySourceExpansionAccountabilityTests.cs`

### Red

- Add Spec §15.4 cases for real sanitizer invocation, rejected -> Redacted,
  exact post-truncation hash, pre-truncation sanitizer hash exclusion, hidden
  provenance invariance, valid-Grant-only Source identity, and recorder
  isolation.
- Prove sanitizer, truncation, or caller-visible effective-hash failure is
  fail-closed before a final Expansion result exists, while later
  Accountability projection failure preserves the established result.

### Green

- Replace the loose expansion signature with
  `AgentMemorySourceExpansionOperationRequest`.
- In this same Task, migrate every Tool/MCP/host production caller to the new
  request with one factory allocation and the authoritative Capability-derived
  invocation context; remove the old overload rather than retaining a bridge.
- Resolve Grant before exposing SourceRef; sanitize expander content again via
  `IAgentMemoryContentSanitizer`.
- Map sanitizer rejection to caller-visible Redacted, truncate sanitized
  content, then project the exact final value with the shared
  EffectiveVisibleContentHash shape.
- Populate the existing result hash with that effective hash and publish the
  typed payload field named `EffectiveVisibleContentHash`.
- Record no RuleSet/version and never reuse the sanitizer/domain hash.
- Treat sanitize -> truncate -> caller-visible effective hash/result as safety
  result construction outside the best-effort fence. Only after the exact final
  Expansion result exists may typed Accountability payload/fact construction
  and producer invocation enter the post-result fence.

### Verify

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore.Tests --filter "FullyQualifiedName~SourceExpansion|FullyQualifiedName~SourceExpandCore"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore.Tests
rtk dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory.Tools
rtk dotnet build src/Integrations/CrestCreates.Mcp.Memory
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.Memory.Tests
rtk git diff --check
```

---

## 9. Task 6 — Integrate Known Curation Outcomes

**Files:**

- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory/Promotion/DefaultAgentMemoryPromotionService.cs`
- Create: `src/Runtime/Agent/CrestCreates.Agent.Memory/Accountability/AgentMemoryCurationFactProjector.cs`
- Create/Modify: `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/AgentMemoryCurationAccountabilityTests.cs`

### Red

- Add committed Promote/Reject/Supersede/Archive facts, typed rejection/
  conflict facts, unknown Store outcome silence, forbidden Reason/Explanation,
  cancelled-after-commit recording, and recorder isolation cases.
- Add `CommittedCurationProjectionFailure_Should_NotChangeCommittedResult`,
  injecting a curation fact-projector failure after confirmed commit.
- Add `CancelledBeforeTransition_Should_NotCommitOrEmitFact`: cancellation
  observed before a terminal Store outcome preserves the business cancellation,
  performs no commit, and makes no Accountability attempt.
- Add an indeterminate-provider cancellation/timeout case proving it remains an
  unknown mutation outcome with no false committed/rejected/failed fact.

### Green

- Publish only after confirmed conditional commit or from a complete typed
  rejection/conflict context.
- Preserve expected CAS hashes and transition identities; never serialize raw
  reason/explanation/source content.
- Re-throw the original `AgentMemoryOperationException`; leave arbitrary
  provider exception or indeterminate cancellation/timeout outcome unclassified
  and unrecorded.
- Keep the business token through the Store transition until terminal outcome.
  Only after committed/rejected/conflict is established may projection switch
  to the producer-owned independent timeout/token.
- Fence the entire post-terminal Accountability sequence—curation hash/fact
  projection, payload construction, and producer invocation—so none of its
  exceptions can replace a confirmed committed/rejected/conflict result or its
  original typed exception.
- Use Identity.OccurredAt for promoted state and Envelope occurrence; do not
  sample another curation time.

### Verify

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests --filter "FullyQualifiedName~CurationAccountability|FullyQualifiedName~ConditionalArchive"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
rtk git diff --check
```

---

## 10. Task 7 — Compose Agent Tool, MCP, and Direct Host Causality

**Files:**

- Modify: `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/Handlers/AgentMemoryCurationHandlerHelpers.cs`
- Modify only the Recall/Expansion/Curation handlers under
  `CrestCreates.Agent.Memory.Tools/Handlers/` needed to centralize validation;
  their new request signatures already compile from Tasks 1/4/5
- Modify: `src/Integrations/CrestCreates.Mcp.Memory/Security/McpMemoryArtifactOriginFactory.cs`
- Modify: `src/Integrations/CrestCreates.Mcp.Memory/Handlers/MemoryRecallHandler.cs`
- Modify: `src/Integrations/CrestCreates.Mcp.Memory/Handlers/McpSourceExpandHandler.cs`
- Modify: Tool/MCP service registration and startup validators
- Modify: Tool unit/E2E tests and `tests/Integrations/CrestCreates.Mcp.Memory.Tests/`

### Red

- Add C01/C02/C10, B08, F12, and F13.
- Prove Memory CorrelationId equals current Capability CorrelationId;
  CausationId equals Capability ExecutionId; ParentAuditId equals the matching
  ambient Capability AuditId.
- Prove logical Agent/MCP InvocationId remains ArtifactOrigin/Runtime reference,
  not Memory OperationId.
- Prove every fresh Capability execution allocates a fresh Memory identity.
- Add `SameOperationIdUnderDifferentCapabilityExecution`: forced accidental
  cross-execution OperationId reuse produces the same AuditId, a changed
  complete RecordHash, and `Conflict` from Recorder with the InMemory sink.
  Task 7 proves only the semantic/composition contract; it does not start or
  depend on PostgreSQL.

### Green

- Consolidate and verify the already-migrated identity admission from Tasks
  1/4/5; Task 7 must not introduce a new compatibility overload or perform
  first-time caller compilation repairs.
- Replace any remaining handler-local Agent/MCP correlation derivation with one
  shared mapping from current authoritative Capability context plus validated
  Agent/MCP binding metadata.
- Reject missing/mismatched Capability ambient Tenant, actor, correlation,
  execution ID, or binding before Memory domain execution.
- Keep direct trusted-host calls explicit: caller supplies identity/context;
  unrelated ambient scope yields no ParentAuditId.

### Verify

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.E2E.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.Memory.Tests
rtk dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests --filter "FullyQualifiedName~AuditMiddleware|FullyQualifiedName~CapabilityPipeline"
rtk git diff --check
```

---

## 11. Task 8 — Durable Sink, NativeAOT, and Closure Evidence

**Files:**

- Modify/Create PostgreSQL Audit sink composition cases under
  `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/`
- Modify Agent Memory Tool and MCP Memory AOT fixtures and fixture tests
- Modify dependency-boundary tests
- Modify `CrestCreates.slnx`, `solutions/CrestCreates.All.slnx`, and `memory.md`

### Red

- Add PostgreSQL Accepted/Duplicate/Conflict/first-snapshot/tenant-isolation
  cases for Memory facts. Include
  `SameOperationIdUnderDifferentCapabilityExecution_PostgreSql`, proving
  durable `Conflict` and retention of the first snapshot when the same
  OperationId is forced under a different Capability execution.
- Extend architecture tests for every forbidden dependency/serialization path.
- Extend the Agent Memory Tool AOT fixture (or one dedicated bridge fixture) to
  require Recall, Source Expansion, and at least one committed Curation before
  its sentinel.
- Extend the MCP Memory AOT fixture only for the surface it actually exposes:
  Recall, Source Expansion, and Capability -> Memory causal containment. Do not
  add an MCP curation Capability/API for evidence convenience.

### Green

- Compose the existing `PostgreSqlAuditSink`; add no Memory schema/table or
  provider business contract.
- In the Agent Memory Tool/dedicated bridge fixture, run Recall, Source
  Expansion, and committed Curation through the real bridge, exact sanitizer
  rules, Recorder, integrity hashing, and configured sink.
- In the MCP fixture, run only MCP Recall/Expansion through the same bridge and
  assert the authoritative Capability correlation/cause/parent mapping.
- Preserve generated JSON only; no reflection resolver, object payload,
  trimming suppression, or managed-binary substitute.
- Update `memory.md` only after all evidence is green, explicitly leaving
  reliable delivery/atomicity to #25/#55.

### Verify

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~Audit|FullyQualifiedName~AgentMemory"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.AotFixture.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.Memory.AotFixture.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk dotnet build CrestCreates.slnx
rtk git diff --check
```

The AOT fixture tests must publish with
`-p:CrestCreatesPublishMode=aot -c Release -r linux-x64 --self-contained true`,
complete native link, and execute the original native binaries. No
NativeAOT-verified claim is accepted from analyzer, trim, or source-generated
JSON unit tests alone.

---

## 12. Completion Ledger

The implementation-side evidence below is supplemented by the linked GitHub
Actions run because the local environment has no Docker or PostgreSQL service.
Run `31663216616` passed every provider, Capability, E2E, and NativeAOT gate on
reviewed implementation head `2ba2f775`.

| Evidence | Command | Result / sentinel |
|---|---|---|
| Agent Memory Abstractions/Runtime | `rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/CrestCreates.Agent.Memory.Tests.csproj --no-restore` | exit 0; 112 passed |
| Agent Memory ReadCore | `rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore.Tests/CrestCreates.Agent.Memory.ReadCore.Tests.csproj --no-restore` | exit 0; 135 passed |
| Accountability bridge | `rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests/CrestCreates.Agent.Memory.Accountability.Tests.csproj --no-restore` | exit 0; 114 passed |
| Agent Memory Tool unit | `rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests/CrestCreates.Agent.Memory.Tools.Tests.csproj --no-restore` | exit 0; 23 passed |
| MCP Memory unit | `rtk dotnet test tests/Integrations/CrestCreates.Mcp.Memory.Tests/CrestCreates.Mcp.Memory.Tests.csproj --no-restore` | exit 0; 35 passed |
| Capability causal regressions | covered by Tool/MCP and CI Capability suites | exit 0 in CI run `31663216616` |
| PostgreSQL Audit sink Memory composition | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests --filter "FullyQualifiedName~Audit|FullyQualifiedName~AgentMemory"` | exit 0 in CI run `31663216616`; no local PostgreSQL service |
| Dependency boundaries | `rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj --no-restore` | exit 0; 93 passed |
| Agent Memory Tool NativeAOT publish-link-run | `rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.AotFixture.Tests/CrestCreates.Agent.Memory.Tools.AotFixture.Tests.csproj --no-restore` | exit 0; native sentinel passed |
| MCP Memory NativeAOT publish-link-run | `rtk dotnet test tests/Integrations/CrestCreates.Mcp.Memory.AotFixture.Tests/CrestCreates.Mcp.Memory.AotFixture.Tests.csproj --no-restore` | exit 0; native sentinel passed |
| Canonical solution build | `rtk dotnet build solutions/CrestCreates.All.slnx --no-restore --no-incremental` | exit 0; 239 projects, 0 errors |

```text
Final closure rule: this ledger is complete because reviewed implementation head
`2ba2f775` has green PostgreSQL, Capability, E2E, and original-binary AOT jobs
in GitHub Actions run `31663216616`.
```

Completion is blocked if any evidence relies on a hidden MemoryId/set hash,
provenance-aware content hash, new Capability identity replay rule, legacy
curation fallback, reflection serialization, or a Memory-specific audit store.
