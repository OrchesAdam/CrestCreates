# Phase 8d+ Governed Agent Memory Tool Projection Implementation Plan

> Implement one bounded task at a time. Do not start Slices 1–5 until every
> Slice 0 stop-gate command and invariant passes. The primary agent owns shared
> project/solution wiring, cross-project integration, architecture review, and
> final verification.

**Status:** Implementation complete — Slices 0–5 are implemented and
verified; final acceptance evidence is recorded below.

**Goal:** Project governed Agent Memory operations as seven exact,
source-generated Agent Tools that execute only through the Phase 8f Capability
mainline, preserve closed-world visibility and provenance, and remain safe
across replay, lifecycle conflict, security-artifact preparation, and
NativeAOT publication.

**Design:**
`docs/superpowers/specs/2026-07-17-phase-8dplus-governed-agent-memory-tool-projection-design.md`

**Tech stack:** .NET 10, incremental Roslyn generators targeting
netstandard2.0, System.Text.Json source generation, bounded JSON Schema v3,
immutable/frozen collections, xUnit 2.9.3, FluentAssertions, deterministic
in-memory stores, linux-x64 NativeAOT publish-and-run.

## Architecture mainline

```text
[AgentToolSpec]
  -> generated exact Schema/Capability/Tool descriptors and JSON bindings
  -> Phase 8f Agent Tool governance and invocation gate
  -> ICapabilityDispatcher(captured CapabilityDescriptor, InvocationSource.Agent)
  -> Capability Pipeline
  -> scoped generated Memory Capability Handler
  -> Agent Memory runtime service
```

There is no direct Agent Tool → Memory store, direct Handler, MCP, Dynamic API,
AppService, Control Plane, provider SDK, reflection JSON, runtime scan, or
dictionary fallback path.

## Global constraints

- Preserve the seven operations and exact names from the Spec:
  `BuildAgentMemoryPack`, `ExpandAgentMemorySource`, `CompressAgentHistory`,
  `ExtractMemoryCandidates`, `PromoteMemoryCandidate`,
  `RejectMemoryCandidate`, and `SupersedeMemoryItem`.
- Tool inputs never accept tenant, user, Agent, execution, actor, visibility,
  approval, governance outcome, persistent Memory ids, raw history, raw source
  ids, or domain operation requests.
- Tool outputs expose only opaque handles/grants and dedicated Tool-safe DTOs.
  They never expose ContextId, BlockId, CandidateId, MemoryId, SourceId,
  ScopeFingerprint, VisibleMemorySetHash, CanonicalPackHash, or an unexpanded
  source-content hash.
- Every Tool has its own concrete result root with required
  `OperationStatus`. Lifecycle state is represented separately by
  `MemoryStatus` or `CandidateStatus`.
- Every Tool enum has `Unknown = 0`; zero has no wire value. Wire values are
  frozen lowercase semantic strings, integers and aliases are rejected, and
  domain enums are mapped by exhaustive switches rather than casts.
- All DescriptorRefs on the Tool path are exact `Namespace + Id + Version`
  tuples with `Version > 0`. Do not infer DescriptorKind or resolve latest/
  compatible versions in the adapter.
- IDs persisted by Memory stores are framework-generated opaque identifiers.
  Provider labels are response-local correlation labels only.
- Provider output may reference trusted input provenance but cannot create or
  modify provenance, DescriptorRefs, ranges, or persistent identities.
- Security handles/grants and every permitted output branch are prepared and
  exact-preflighted before the first Memory mutation.
- Curation uses only the actual selected `IAgentMemoryPromotionService`. The
  same service instance must prove `ConfirmedAtomic`; a Store declaration alone
  is insufficient.
- A typed, confirmed zero-write Conflict/Unavailable is a normal inner Tool
  result. Unknown commit state is never mapped to ordinary failure and never
  causes artifact revocation merely because an exception was observed.
- Revision 7 is authoritative: mutating handlers prepublish one bounded allowed
  outcome set; finalization must match exactly one receipt. The common fact
  sidecar contains only branch-invariant facts.
- `AddCrestAgentTools()` owns invocation binding, fact/outcome buffers,
  preflight infrastructure, and global governance-outcome-v2 registration.
  `AddAgentMemoryTools()` only consumes and validates those shared features.
- Tool budget and Memory data/cardinality budgets remain independent.
- Completed replay performs no provider call, security-artifact issuance or
  renewal, persistence, or lifecycle mutation. Indeterminate remains fenced.
- Do not claim distributed exactly-once or undeclared durable-store atomicity.
- Preserve existing MCP/Agent Tool flat-schema bytes and hash vectors while
  adding Schema v3 nested support.
- Do not update `memory.md` to Implemented until all exit gates pass.
- Never delete files directly; move obsolete artifacts to `99_RecycleBin/`.

## Execution and ownership policy

Shared files are deliberately sequenced. Tasks that modify the same project,
generator, DI extension, or canonical hash contract run serially in the order
listed. A task is complete only after its focused tests pass and its diff has
been reviewed for fallback paths and unrelated changes.

The primary agent exclusively owns:

- all `.csproj` changes and canonical solution files;
- `AgentToolServiceCollectionExtensions.cs` and
  `CapabilityServiceCollectionExtensions.cs` final integration;
- canonical shape-version changes shared across tasks;
- `memory.md`, plan checkboxes, usage documentation, and final acceptance
  evidence.

Do not fold unrelated cleanup into this phase. Existing user changes in a dirty
worktree must be preserved.

## Dependency graph

```text
Task 0 baseline
  -> Task 1 Schema v3
  -> Task 2 composable Agent JSON
  -> Task 3 DI-safe Capability handlers
  -> Task 4 Phase 8f binding/facts/preflight/audit v2
  -> Task 5 Memory integrity/hash/range/identity
  -> Task 6 confirmed-atomic lifecycle transitions
  -> Task 7 security-artifact preparation
  -> Task 8 Slice 0 integration stop gate

Task 8
  -> Tasks 9-10 contracts/descriptors
  -> Tasks 11-12 read path
  -> Tasks 13-14 processing path
  -> Tasks 15-16 curation path
  -> Tasks 17-19 executable closure
```

Tasks after Task 8 may be implemented in file-isolated batches, but integration
remains ordered by Tool path. No later task may weaken a failed Slice 0
invariant.

---

## Slice 0 — Shared mainline prerequisites (hard stop gate)

### Task 0 — Freeze the current flat-contract and runtime baseline

**Ownership:** tests and plan evidence only; no production behavior changes.

**Files:**

- Modify focused golden/baseline tests only where an explicit pre-change vector
  is missing under:
  - `tests/Metadata/Core/CrestCreates.Schema.Tests/`
  - `tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/`
  - `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/`
  - `tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests/`
  - `tests/Runtime/Capability/CrestCreates.Capability.Tests/`

**Work:**

- [ ] Record existing Schema v2 flat-object projection, canonical bytes, and
      Contract/Definition hash vectors before introducing v3.
- [ ] Record existing MCP and Agent Tool flat input/output projection parity.
- [ ] Add regression tests that expose the current non-zero range slicing and
      compressed-context whole-block expansion bugs.
- [ ] Add characterization tests for current Memory v1 content/scope/pack
      hashes and existing lifecycle behavior without treating defects as the
      new expected contract.
- [ ] Capture focused test counts and commands in the task handoff.

**Focused verification:** Schema, Agent Tools, Memory, Memory.Llm, Capability,
and MCP focused suites are green except for newly added tests that deliberately
describe the approved new behavior.

### Task 1 — Bounded nested Schema/JSON projection and Schema hash v3

**Ownership:**

- `src/Metadata/CrestCreates.Schema.Abstractions/`
- `src/Metadata/CrestCreates.Schema/`
- Schema canonical profiles under `src/Metadata/CrestCreates.Metadata/`
- Schema tests and only the MCP/Agent compatibility tests required by the
  shared projection change

**Work:**

- [ ] Extend the exact schema subset with the neutral `FieldType = "object"`
      marker, bounded object/object-collection graphs, direct
      `SchemaDescriptor.References`, deterministic `$defs/$ref`, and root depth
      zero.
- [ ] Freeze graph limits: maximum depth, descriptor count, field count,
      collection element nullability, exact positive versions, Ordinal keys,
      cycle rejection, and no unresolved/transitive implicit refs.
- [ ] Extend validation for nested duplicate/unknown/required/nullability rules
      without reflection or open-ended dictionary traversal.
- [ ] Extend recursive `JsonTypeInfo` directional parity for nested objects and
      collections.
- [ ] Add Schema canonical shape v3 for nested contracts while retaining v2
      historical verification/read compatibility. Never rewrite historical
      vectors.
- [ ] Prove flat Schema v2 MCP and Agent output bytes/hashes remain stable.

**Required tests:** nested scalar object, object collection, `$defs` ordering,
direct references, root-depth boundaries, non-null elements, cycle/limit
rejection, recursive parity, v2/v3 golden vectors, MCP runtime/E2E/AOT
regressions.

**Focused verification:**

```bash
dotnet test tests/Metadata/Core/CrestCreates.Schema.Tests
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests --filter "FullyQualifiedName~Schema|FullyQualifiedName~CanonicalHash"
dotnet test tests/Integrations/CrestCreates.Mcp.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests --filter "FullyQualifiedName~Schema"
```

### Task 2 — Composable source-generated Agent Tool JSON contexts

**Ownership:**

- `src/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions/Binding/`
- `src/Runtime/Agent/CrestCreates.Agent.Tools/Json/`
- Agent Tool snapshot/startup files required for JSON composition
- `src/Tooling/CrestCreates.CodeGenerator/AgentToolGenerator/` JSON contribution
  emission
- matching Agent Tool and generator tests

**Work:**

- [ ] Add `IAgentToolJsonContextContributor` to Agent.Tools.Abstractions with
      stable Id/Order and `Create(JsonSerializerOptions sharedOptions)`.
- [x] Create one normalized Options profile, configure/freeze it before
      contributor creation, reject later mutation/reflection resolvers, and
      enforce deterministic contributor order. Because .NET 10
      `JsonSerializerContext` owns and seals its options during construction,
      each generated contributor receives an equivalent frozen template; the
      published runtime snapshot uses only generated `JsonTypeInfo` and a
      non-reflection empty resolver on the profile options.
- [ ] Generate contributor definitions and explicit module selection; do not
      make loaded-assembly discovery a runtime registration path.
- [ ] Give each binding input/output root exactly one owner.
- [ ] Permit repeated nested CLR metadata only when normalized property,
      nullability/required, converter/enum, SchemaRef, parity, and canonical
      `JsonContractFingerprint` contracts are identical.
- [ ] Ensure the resolver used for a binding root comes from its declared root
      owner; nested equivalence never becomes implicit root precedence.
- [ ] Keep existing single-context applications source compatible through the
      same generated contributor path, not through a second options path.

**Required tests:** duplicate root failure, equivalent nested type allowed,
different nested contract failure, shared Options identity, contributor order,
two-module isolation, no reflection fallback, startup snapshot stability.

**Focused verification:** Agent.Tools abstractions/runtime tests, generator
AgentTool tests, existing Agent E2E and AOT fixture.

### Task 3 — DI-safe generated Capability handler activation

**Ownership:**

- `src/Runtime/Capability/CrestCreates.Capability.Abstractions/`
- `src/Runtime/Capability/CrestCreates.Capability/`
- `src/Tooling/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/`
- `src/Tooling/CrestCreates.CodeGenerator/AppServiceCompatibilityGenerator/`
- matching Capability and generator tests

**Work:**

- [ ] Replace generated `new Handler()` invocation with a generated
      `ICapabilityContextAwareHandlerInvoker` that resolves the exact scoped
      handler from `CapabilityExecutionContext.ServiceProvider`. The legacy
      context-free method must not become a service-locator fallback.
- [ ] Generate provider definitions containing handler service registrations
      and resolver entries; ModuleInitializer may register definitions only.
- [ ] Make each `IServiceCollection` explicitly select ProviderId/ModuleId.
      `AddCapabilityRuntime()` applies only selected providers to that Host.
- [ ] Register generated handler classes as Scoped and invoker definitions as
      Singleton-safe metadata. Reject duplicate Capability names, duplicate
      providers, ambiguous multi-interface handlers, or mismatched services.
- [ ] Eliminate the process-global resolver as the formal execution source.
      Do not preserve it as a fallback when no provider is selected.
- [ ] Preserve AppService compatibility through the same selected-provider
      route rather than retaining a second registration mechanism.

**Required tests:** constructor injection, scoped lifetime, no `new Handler()`,
two Hosts with disjoint selected providers, referenced-unselected/test handlers
absent, duplicate Capability rejection, NativeAOT source guards.

**Focused verification:** Capability tests, SchemaCapability/AppService
generator suites, Dynamic API compatibility suite, existing Agent/MCP E2E.

### Task 4 — Phase 8f invocation binding, audit v2, fact buffers, and output preflight

**Ownership:**

- `src/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions/Governance/`
- `src/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions/Invocation/`
- `src/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions/Binding/`
- `src/Runtime/Agent/CrestCreates.Agent.Tools/Governance/`
- `src/Runtime/Agent/CrestCreates.Agent.Tools/Invocation/`
- `src/Runtime/Agent/CrestCreates.Agent.Tools/AgentToolServiceCollectionExtensions.cs`
- Agent Tool generator binding/preflight emission and matching tests

**Work:**

- [ ] Add immutable `AgentToolInvocationBindingSnapshot` carrying the exact
      structured five-field logical key and the exact Phase 8f invocation
      fingerprint. The Invoker creates it once and propagates the same object
      through `CapabilityExecutionContext.Items`.
- [ ] Add Invoker-owned common fact buffer. Handlers receive only a sink,
      cannot seal/finalize/audit, and cannot use AsyncLocal or static state.
- [ ] Restrict common facts to branch-invariant safe values. Reject text, raw
      ids, HandleId/GrantId, operation status, counts, lifecycle status,
      truncation, and returned content-hash facts.
- [ ] Add generated `IAgentToolOutputPreflight<TOutput>` using the exact binding
      root `JsonTypeInfo`, shared Options, enum converters, OutputSchema, and
      validator used by final output serialization.
- [ ] Add bounded prepared-outcome publication. Curation handlers publish one
      immutable set of unique outcome codes and receipts before mutation;
      optional per-branch internal facts are bound to their receipt.
- [ ] Final Invoker preflight must match exactly one receipt by Tool/version,
      contract fingerprint, and structured-output hash. Zero/multiple matches,
      unprepared status, changed bytes, missing sink, or changed contract enter
      `output_finalization_failure`.
- [ ] Output projectors own actual OperationStatus, returned/persisted counts,
      lifecycle, truncation, and model-visible content-hash facts. The Invoker
      selects branch facts only after the unique receipt match.
- [ ] Capability failure discards all output/internal candidate facts and keeps
      only validated safe input facts. Apply the smaller trusted/global fact
      cap before finalization.
- [ ] Replace new writes globally with
      `agent-tool-governance-outcome-v2`: hash Kind, Code, safe issue code/path,
      and validated facts only. Exclude Message, StructuredOutput, explanation,
      and every content field. v1 is historical read-only.
- [ ] Keep full replay Outcome solely in the Invocation Gate and ensure audit
      query APIs cannot retrieve it.

**Required tests:** exact binding propagation/no handler recomputation,
single-seal buffers, two-Host isolation, all prepared receipt error modes,
mutated-list byte mismatch, legal Completed/Conflict/Unavailable matching,
branch-correct fact selection, v2 text independence, ordinary Tool empty facts,
no raw tokens.

**Focused verification:** Agent.Tools abstractions/runtime/E2E/AOT and
CodeGenerator AgentTool suites.

### Task 5 — Memory range, visibility, identity, provenance, and canonical hashes

**Ownership:**

- `src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/`
- Memory runtime files under `CanonicalHashing/`, `Recall/`, `Sanitization/`,
  `Compression/`, and `Extraction/`
- compatible changes in `CrestCreates.Agent.Memory.Llm/`
- Memory and Memory.Llm tests

**Work:**

- [ ] Add the closed-world visibility boundary and comparer using only exact
      Namespace/Id/Version tuples. Include effective Descriptor closure across
      the artifact and every nested SourceRef; empty universe fails closed for
      bound memory and unscoped visibility requires explicit permission.
- [ ] Fix conversation/task expansion with original-index slicing. Validate
      non-negative, ordered, in-bounds ranges before reading content.
- [ ] Resolve compressed-context grants to one exact BlockId and never return
      sibling blocks or interpret ContextId as the grant resource.
- [ ] Add a framework artifact id factory for Context/Block/Candidate/Memory
      ids. IDs must be opaque, batch-unique, collision checked, and independent
      of tenant/history/provider/content values.
- [ ] Treat compressor/extractor Provider ids as local labels only and remap all
      persistent/source identities through trusted framework ids.
- [ ] Enforce compression provenance as a subset of loaded history-generated
      refs plus refs already on trusted turns/events. Enforce extraction refs as
      subsets of input blocks and DescriptorRefs as subsets of the effective
      input closure.
- [ ] Validate the entire result graph before the first create. Reject modified
      SourceKind/SourceId/range/DescriptorRefs/correlation/causation/hash.
- [ ] Implement `memory-content-hash-v2` over tenant, sanitized content, and a
      canonical deduplicated/sorted full SourceRef projection. Use an explicit
      empty array, reject self/cyclic hash provenance, and keep v1 only for
      historical verification/recomputation.
- [ ] Implement `memory-scope-hash-v2` and `memory-pack-hash-v2`, including all
      behavior-affecting fields, exact DescriptorRefs, IncludeArchived,
      eligibility ids, truncation, and returned order. Internal scope/set/pack
      hashes never enter model output.
- [ ] Freeze Candidate/Memory transition-state hashes used by expected-state
      plans.

**Required tests:** range matrix, exact block lookup, opacity/collision,
provider-label rejection, provenance subset mutation matrix, multi-source hash
ordering/add/remove/range/closure changes, empty sources, v1/v2 golden vectors,
closed-world visibility, LLM/deterministic adapter parity.

**Focused verification:** Memory and Memory.Llm full suites plus Metadata
canonical hash tests.

### Task 6 — Immutable Candidate payload and confirmed-atomic curation service

**Ownership:**

- Memory lifecycle/store/service contracts in
  `CrestCreates.Agent.Memory.Abstractions`
- `Stores/InMemoryAgentMemoryStore.cs`
- `Promotion/DefaultAgentMemoryPromotionService.cs`
- `AgentMemoryServiceCollectionExtensions.cs`
- focused Memory lifecycle/concurrency tests

**Work:**

- [ ] Replace general mutable Candidate/Memory save on the production curation
      mainline with create-only methods and typed expected-state transitions.
      Candidate payload becomes immutable after create.
- [ ] Add typed transition outcomes for success, Conflict,
      ResourceUnavailable, validation failure, and unknown commit state without
      exception-message projection.
- [ ] Add candidate and target-memory expectations/state hashes. Promote and
      Supersede plans carry framework-assigned `NewMemoryId` and exact expected
      committed hash/graph.
- [ ] Make Candidate consumption one store-owned conditional transition shared
      by Promote, Reject, and Supersede. First-party InMemory operations perform
      check and complete state transition under one lock/primitive.
- [ ] Make Supersede service-owned: reload target/replacement, verify same
      tenant and states, consume replacement once, create an independent new
      MemoryId, and persist matching forward/back links.
- [ ] Add `IAgentMemoryStoreCapabilities` as an internal proof and
      `IAgentMemoryCurationServiceCapabilities` at the actual Tool call
      boundary. The selected Promotion Service instance must implement both
      `IAgentMemoryPromotionService` and the latter capability contract.
- [ ] First-party service reports `ConfirmedAtomic` only when its exact store
      primitive is ConfirmedAtomic, it performs no fallible post-commit work,
      does not surface post-commit cancellation as failure, and returns the
      committed object from the same transition.
- [ ] Fail startup for custom/unknown/different-instance services that cannot
      prove the guarantee. Durable non-confirming providers cannot enable the
      three curation Tools in this phase.

**Required tests:** double/concurrent promotion, promote-vs-supersede,
reject-vs-promote, candidate state-hash conflict, cross-tenant/non-candidate
replacement, candidate consumption, graph equality, unsafe custom service
startup failure, post-commit cancellation returns success, fault-injection
zero-write failures.

**Focused verification:** Agent.Memory full suite and DI/boundary tests.

### Task 7 — Dual-origin, plan-bound security artifact infrastructure

**Ownership:**

- Create the project shells for
  `CrestCreates.Agent.Memory.Tools.Abstractions` and
  `CrestCreates.Agent.Memory.Tools`.
- Put Host/access/grant/handle/batch store contracts in
  `CrestCreates.Agent.Memory.Tools.Abstractions`.
- Put the coordinator, Agent-Tool binding-hash projector, resolution services,
  and deterministic in-memory development adapters in
  `CrestCreates.Agent.Memory.Tools`.
- Add focused security tests under
  `CrestCreates.Agent.Memory.Tools.Tests`.

The project shells are created in Slice 0 because the security preparation
protocol is a stop-gate prerequisite. Task 9 completes their public Tool DTO
surface; it does not relocate these contracts into Memory core. Memory core and
Memory.Llm remain unaware of Agent Tools and security-artifact tokens.

**Work:**

- [ ] Define distinct Agent Tool and trusted Host batch origins. Agent Tool
      origin consumes the exact propagated InvocationBinding snapshot; History
      origin uses versioned HostOperationId/OperationFingerprint/Purpose.
- [ ] Add opaque history/resource handles and source grants with tenant/user/
      Agent/execution/scope/resource-kind binding, expiration, state, and exact
      Descriptor closure. Model-visible grant DTO will later omit internal
      source hash.
- [ ] Add `ArtifactPlanHash` over resource kind/internal id, principal,
      Agent/execution, scope fingerprint, closure, unscoped flag, exact
      SourceRef binding, purpose, and lifetime policy. Exclude random token ids,
      times, absolute expiry, and provider text.
- [ ] Make batch preparation idempotent: same origin/purpose/ordinal and same
      plan returns the original authoritative artifacts; a changed plan
      conflicts.
- [ ] Track `CreatedByBatch` versus `ReusedExisting`. Abort/revoke affects only
      artifacts created by that batch and never renews reused expiry.
- [ ] Enforce per-invocation, per-resource, active-handle, grant, descriptor,
      SourceRef, tag, and audit-fact cardinality caps before partial creation.
- [ ] Permit handle/grant reuse across InvocationIds only inside the same
      Execution and binding. Never allow cross-execution use.
- [ ] PreparationOrdinal advances only after a confirmed zero-write identity
      collision, with prior created artifacts revoked before retry.
- [ ] Keep artifact Prepared/Committed/Aborted bookkeeping off the synchronous
      post-Memory-success path; authorization relies on the prepared record and
      resource existence, with nonexistent targets returning Unavailable.

**Required tests:** Host issuer independent of Phase 8f context, exact binding
propagation, plan hash vectors, identical retry, changed-plan conflict, quota
exhaustion with zero partial writes, active handle reuse, created/reused abort,
expiry/revoke/wrong principal/kind/scope anti-probing.

**Focused verification:** Memory security tests and Agent.Tools invocation
binding tests.

### Task 8 — Slice 0 integration and stop-gate review

**Primary-agent ownership:** all shared DI/project/solution wiring and the Slice
0 evidence record.

**Work:**

- [ ] Wire shared abstractions without introducing the Memory Tools projects
      yet. Validate dependency directions before contract work begins.
- [ ] Run a source search proving there is no generated `new Handler()`,
      reflection resolver, handler service locator, process-global applied
      provider set, v1 governance outcome writer, chained reset-index range
      filter, provider-owned persistent id, or mutable Candidate curation save
      mainline.
- [ ] Review every shared public API against Spec sections 5.1–5.11.
- [ ] Run all prerequisite golden vectors and deterministic concurrency tests.
- [ ] Run existing Schema, Metadata, Capability, Agent Tools, MCP, Memory,
      Memory.Llm, Control Plane, CodeGenerator, and dependency-boundary suites.
- [ ] Run existing MCP and Agent Tool linux-x64 NativeAOT fixtures after shared
      generator/runtime changes.
- [ ] Stop if any prerequisite is incomplete. Do not start Task 9 with a TODO,
      permissive fallback, test-only bypass, or compatibility second path.

**Slice 0 exit evidence:** all Spec section 18.1 tests pass; shared flat
compatibility vectors are unchanged; exact failure/rollback/concurrency
semantics are executable; both existing NativeAOT gates still pass.

---

## Slice 1 — Contracts, descriptors, projects, and generated artifacts

### Task 9 — Complete the Memory Tool projects and exact Tool-safe contracts

**Files:**

- Complete `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Abstractions/`
- Complete `src/Runtime/Agent/CrestCreates.Agent.Memory.Tools/`
- Complete `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests/`
- Modify only required project/solution and boundary files

**Work:**

- [ ] Add abstractions for access scope, visibility boundary, budgets, security
      artifact requests/results, fact projectors, and dedicated Tool DTOs.
- [ ] Add seven concrete input roots and seven concrete result envelopes.
      Avoid public generic envelopes and domain operation-request exposure.
- [ ] Add exact Tool enums and converters for OperationStatus, source/memory/
      candidate status, kind, confidence, and diagnostic severity.
- [ ] Map domain Unknown confidence to Tool `Unspecified = 1` / `"unknown"`;
      Tool Unknown=0 remains unwireable.
- [ ] Add `AgentMemoryToolCanonicalHashDto` with Value, AlgorithmVersion,
      ContractVersion, and CanonicalShapeVersion only.
- [ ] Add `AgentMemorySourceGrantDto` with GrantId, SourceKind, and ExpiresAt
      only. Full Expand returns a Tool-safe content hash; truncated Expand
      returns null.
- [ ] Add a source-generated Memory Tool JSON context contributor containing
      every root and nested DTO.
- [ ] Freeze options/limit validation and startup diagnostics. Inputs omit
      IntentText in v1.

**Required tests:** DTO surface guards, no persistent ids/internal hashes,
string-only enum binding/output, hash metadata omission, diagnostic mapping,
required OperationStatus, non-Completed empty payloads, source grant shape,
all public DTOs in generated context.

### Task 10 — Generate exact Schemas, Capabilities, Tools, roles, and permissions

**Ownership:** Memory Tool descriptor authoring files, generated artifacts,
CodeGenerator tests, Metadata hash/package tests, and boundary tests.

**Work:**

- [ ] Author seven nested input/output Schema specs using the Schema v3 mainline.
- [ ] Author seven native Capability handlers and exact Capability descriptors.
- [ ] Author seven Agent Tool specs with the section 9 permission/role/risk/
      approval/audit/selection/budget matrix. Supersede is High + Required;
      all seven are Audit Required.
- [ ] Generate bindings, JSON contributor, scoped handler provider, exact
      output preflights, descriptors, and bootstraps without reflection.
- [ ] Add canonical ContractHash/DefinitionHash golden vectors and package/
      snapshot round trips.
- [ ] Register the Memory module/provider only through
      `AddAgentMemoryTools()`, validate shared Agent Tools infrastructure, and
      keep Agent Memory core/LLM unaware of the projection.
- [ ] Enforce dependency guards from Spec section 18.6 and inspect generated
      source for direct runtime Handler/store/Control Plane references.

**Focused verification:** Memory.Tools tests, CodeGenerator AgentTool/
SchemaCapability tests, Metadata canonical/package tests, boundary tests, and
startup build.

---

## Slice 2 — Governed read path

### Task 11 — Implement BuildAgentMemoryPack

**Ownership:** Memory Tool trusted-context/scope factories, Build handler,
projection, audit-fact projector, and focused tests.

**Work:**

- [ ] Build tenant/user/Agent/execution/time/governance scope exclusively from
      trusted Agent/Capability context and exact InvocationBinding.
- [ ] Resolve closed-world visibility and independent Tool/data/cardinality
      budgets before recall.
- [ ] Call `IAgentMemoryRetriever` and revalidate tenant, active visibility,
      Descriptor closure, sanitation, character/count limits, and non-
      authoritative semantics.
- [ ] Prepare/reuse opaque Memory handles and source grants as one plan-bound
      batch before publishing the result.
- [ ] Return Items, ReturnedCount, WasTruncated, IsAuthoritative=false, safe
      diagnostics, and visible per-item content hashes only. Keep scope/set/pack
      hashes internal audit/cache facts.
- [ ] Ensure completed replay reuses the Phase 8f result and performs no recall,
      grant/handle creation, or renewal.

**Required tests:** empty/mixed/unscoped visibility, count/character budgets,
anti-probing, quota zero-partial creation, handle reuse, model-output surface,
internal fact cap, completed replay.

### Task 12 — Implement ExpandAgentMemorySource

**Ownership:** Expand handler, grant/handle resolver orchestration, projection,
and focused tests.

**Work:**

- [ ] Resolve a grant under the trusted principal/execution/scope and recheck
      current exact Descriptor closure, resource existence, range, and content
      integrity.
- [ ] Expand only stored sanitized content using the fixed runtime expander.
- [ ] Normalize nonexistent, invisible, expired, revoked, forged, wrong-kind,
      stale-visibility, and invalid-range cases to byte-equivalent Unavailable.
- [ ] Map unsupported source kinds to NotExpandable and sanitizer rejection to
      Redacted without revealing internal errors.
- [ ] Require content hash only for a complete non-truncated result. Set null
      for every truncated response; do not reuse `memory-content-hash-v2` for a
      returned prefix.

**Required tests:** non-zero singleton/tail ranges, exact compressed block,
adjacent-content non-disclosure, full/truncated hash rules, every anti-probing
case, independent budgets, no Control Plane lookup.

---

## Slice 3 — Governed processing path

### Task 13 — Implement CompressAgentHistory

**Ownership:** history-handle resolution, compression handler/orchestrator,
projection/facts, and focused tests.

**Work:**

- [ ] Accept only an opaque Host-issued history handle; never accept model raw
      history or source ids.
- [ ] Load the trusted Conversation/Task record and derive the complete allowed
      provenance set before calling the configured compressor.
- [ ] Allocate opaque Context/Block ids, remap Provider labels, sanitize output,
      validate provenance/tenant/limits/hash/closure for the complete graph, and
      collision check ids before persistence.
- [ ] Prepare required Context/Block handles, source grants, and the Completed
      output receipt before the first create.
- [ ] Create the compressed context through create-only persistence only after
      every artifact and output check succeeds; on confirmed no-write failure
      revoke only CreatedByBatch artifacts.
- [ ] Support deterministic and LLM compressors through the same handler and
      validation path.

**Required tests:** unauthorized handle calls compressor zero times, Provider
label/provenance mutation rejection, all-result validation before save,
handle/grant/preflight failure zero-write, create collision retry, deterministic
and LLM implementations, replay no second provider/persistence call.

### Task 14 — Implement ExtractMemoryCandidates

**Ownership:** Context-handle resolution, extraction handler/orchestrator,
projection/facts, and focused tests.

**Work:**

- [ ] Resolve and load one stored CompressedContext by opaque handle.
- [ ] Allocate framework Candidate ids and accept only Provider-local labels.
- [ ] Sanitize every candidate and validate tenant, Candidate status, count/
      characters/tags, SourceRef subset, DescriptorRef closure, and v2 hash for
      the complete batch before any create.
- [ ] Prepare Candidate handles/grants and the exact Completed result receipt
      before the first Candidate create.
- [ ] Create Candidate-only results; extraction never promotes or marks
      authoritative.
- [ ] Keep deterministic and LLM extractors behind the same validation,
      persistence, facts, replay, and rollback path.

**Required tests:** unknown/modified provenance, out-of-closure refs, all-before-
first-save, artifact/preflight zero-write, Candidate-only lifecycle, opaque ids,
same-batch replay, provider-neutral selection.

---

## Slice 4 — Confirmed-atomic curation path

### Task 15 — Implement PromoteMemoryCandidate and RejectMemoryCandidate

**Ownership:** Promote/Reject handlers, trusted operation-request factory,
prepared branch envelopes/facts, and focused tests.

**Work:**

- [ ] Validate the selected Promotion Service instance has
      `ConfirmedAtomic` before these Tools enter discovery.
- [ ] Resolve authorized Candidate handle, load the immutable snapshot, build
      its expectation, and derive actor/time/reason/explanation from trusted
      context and safe input.
- [ ] For Promote, assign the new MemoryId, prepare its handle/grants, and
      construct/preflight Completed, Conflict, and contract-permitted
      Unavailable envelopes before the service call.
- [ ] For Reject, construct/preflight the same bounded legal branch set before
      conditional transition; Reject creates no new handle/grant and returns no
      Candidate content.
- [ ] Publish one allowed-outcome set once. After the service call, return only
      the corresponding prepared immutable envelope.
- [ ] On confirmed Conflict/Unavailable, revoke only artifacts created for the
      failed Promote branch and select payload-empty output. Do not turn normal
      conflicts into InvocationIndeterminate.
- [ ] Do not revoke or return ordinary failure for unknown commit outcomes.

**Required tests:** preparation/preflight zero mutation, receipt match for all
branches, conflict not Indeterminate, same Candidate concurrent promotion,
reject-vs-promote winner, committed graph equals prepared result, branch facts,
completed replay, post-commit cancellation.

### Task 16 — Implement SupersedeMemoryItem

**Ownership:** Supersede handler/orchestrator, branch envelopes/facts, approval
integration, and focused tests.

**Work:**

- [ ] Resolve target Memory and replacement Candidate opaque handles under the
      same trusted tenant/principal/execution/scope.
- [ ] Bind both exact expectations and a framework-assigned independent new
      MemoryId before security/output preparation.
- [ ] Prepare new Memory handle/grants and preflight the bounded Completed,
      Conflict, and contract-permitted Unavailable set before the service call.
- [ ] Call only `IAgentMemoryPromotionService.SupersedeAsync`; the service owns
      tenant/status checks, single Candidate consumption, target transition,
      create-only replacement, and forward/back links.
- [ ] Enforce High risk, Required approval, claimed evidence, Required audit,
      and independent Capability permission.
- [ ] Map conflict/unavailable without probing and select only matching branch
      facts. Unknown commit state remains fenced.

**Required tests:** same Candidate cannot supersede two memories, cross-tenant/
non-candidate rejection, concurrent promote-vs-supersede, either expectation
conflict zero writes, successful links/consumption, approval denial, branch
receipts/facts, replay no second mutation.

---

## Slice 5 — Executable closure

### Task 17 — Generator-backed Memory Tool E2E host

**Files:**

- Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.E2E.Tests/`
- Modify canonical solutions only through primary integration

**Work:**

- [x] Build a real generated Host using two selected JSON contributors/modules,
      Capability Pipeline, Phase 8f governance, Memory Tools,
      deterministic Memory runtime, and ConfirmedAtomic in-memory curation.
- [x] Execute Build → exact-range Expand.
- [x] Execute Host history handle → Compress → Extract → Promote → Build.
- [x] Execute approved Supersede and verify Candidate consumption and links.
- [x] Prove permission, selection/role, approval, Tool budget, data budget,
      visibility, anti-probing, safe inner OperationStatus, outer Capability/
      Phase 8f outcomes, completed replay, and Indeterminate fencing.
- [x] Assert non-zero singleton turn and exact compressed block expansions do
      not return adjacent content.

**Focused verification:** the complete new E2E project plus existing Agent Tool
and Memory suites.

### Task 18 — linux-x64 NativeAOT publish-link-run fixture

**Files:**

- Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.AotFixture/`
- Create `tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.AotFixture.Tests/`

**Work:**

- [x] Publish the fixture with `PublishAot` for linux-x64, complete native link,
      and execute the original native binary.
- [x] Cover generated nested Schemas/Capabilities/Tools, two module JSON
      contributors over shared Options, selected-provider handler DI,
      ConfirmedAtomic service validation, discovery, Build, exact nested output
      receipt matching, branch-correct audit facts, Promote, and completed
      replay with one mutation.
- [x] Treat IL2026/IL3050 on the first-party Memory Tool mainline as failures.
- [x] Do not claim Memory.Llm AOT capability from this deterministic fixture.

**Focused verification:** new AOT fixture test, existing Agent Tool AOT, and MCP
AOT regression.

### Task 19 — Final integration, documentation, and acceptance

**Primary-agent ownership:**

- `CrestCreates.slnx`
- `solutions/CrestCreates.All.slnx`
- Runtime solution files as applicable
- Memory Tool usage documentation
- `memory.md`
- this plan and Issue #53 acceptance evidence

**Work:**

- [x] Add every production/test/fixture project to canonical solutions and
      verify project-reference direction.
- [x] Document the short application path, Host history-handle issuance,
      selected handler/module providers, trusted context responsibilities,
      exact-version visibility, non-authoritative outputs, security-artifact
      ordering, Gate replay sensitivity, and durability/atomicity limitations.
- [x] Review all public contracts, generated sources, canonical vectors,
      diagnostics, permissions, risk/approval/audit values, and model-visible
      JSON against the Spec.
- [x] Search source/generated output for forbidden fallback paths and leaked
      persistent ids/tokens/text audit facts.
- [x] Run focused suites, canonical Runtime/All builds, dependency boundaries,
      E2E, and all three relevant NativeAOT gates.
- [x] Update `memory.md` to Implemented only after executable evidence passes.
- [x] Record exact command output and Issue #53 acceptance mapping; then mark
      plan tasks complete and commit the implementation in reviewed slices.

## Final verification commands

## Implementation evidence (2026-07-20)

### Final verification rerun (2026-07-20)

The post-implementation rerun completed successfully with the approved
escalated test runner (the sandbox-only runner cannot open the VSTest IPC
socket):

```text
CrestCreates.Agent.Memory.Tests                         50 passed
CrestCreates.Agent.Tools.Tests                          116 passed
CrestCreates.Capability.Tests                           139 passed
CrestCreates.CodeGenerator.Tests                        277 passed
CrestCreates.DependencyBoundaries.Tests                  40 passed
CrestCreates.Agent.Memory.Tools.Tests                     6 passed
CrestCreates.Agent.Memory.Tools.E2E.Tests                 1 passed
CrestCreates.Agent.Memory.Tools.AotFixture.Tests          1 passed
CrestCreates.slnx build                                  0 errors
```

The AOT fixture completed native publish, link, and execution; the build
reported warnings only. No new fallback or source-generated JSON regressions
were observed in this rerun.

### Audit delta (2026-07-20)

The post-approval audit closed the remaining execution-boundary gaps before
commit:

- Generated Capability handlers now expose an explicit `Apply(IServiceCollection)`
  provider path. `AddAgentMemoryTools()` selects that provider into a Host-owned
  resolver and registers handlers as scoped services; the old module initializer
  registration is compatibility-only and is not used by the Memory Tool path.
- Artifact preparation failures revoke only `CreatedByBatch` handles/grants.
  Reused artifacts remain active and retain their original expiry.
- Compression and extraction now validate trusted provenance closure before the
  first create, sanitize final provider content, recompute v2 content hashes,
  and remap provider labels to framework-owned Context/Block/Candidate ids.
- Promotion and supersession use the complete plan-bound artifact hash and
  return the single preflighted envelope unchanged after a confirmed commit.
- Preflight receipts expose whether an allowed outcome set was published. A
  later handler exception is fenced as `output_finalization_failure`, while a
  typed `Conflict` or `Unavailable` result matches its preflighted branch.
- Added Host-isolation and generated-provider assertions to the dedicated
  Memory Tool and CodeGenerator suites.

Completed in the current implementation:

- Schema v3 bounded nested projection, exact-version references, range-safe
  expansion, opaque artifact identity, provenance closure, and content/scope/
  pack hash v2 contracts.
- Shared Agent Tool JSON contributor/options path, DI-resolved generated
  handlers, exact invocation binding propagation, governance outcome v2,
  invocation fact sidecar, and bounded multi-outcome preflight receipts.
- Memory Tool abstractions and seven concrete operation envelopes, generated
  JSON context, descriptor/capability/tool registration, closed-world Build /
  Expand / Compress / Extract paths, and confirmed-atomic curation handlers.
- Plan-bound security artifact stores with idempotent retry, changed-plan
  conflict, quota checks, and CreatedByBatch/ReusedExisting rollback rules.
- Dedicated Memory Tool contract/security/startup tests (6/6), generator-backed
  Memory Tool E2E (1/1), and deterministic Memory Tool linux-x64 NativeAOT
  publish-link-run (1/1).
- Existing focused
  suites (Memory 8/8, Memory.Llm 50/50, Agent Tools 116/116, Capability
  139/139, MCP 66/66, CodeGenerator net10 17/17), full solution build (0
  errors), Runtime/All solution builds (0 errors), dependency boundaries
  (40/40), and existing Agent Tool/MCP linux-x64 NativeAOT publish-and-run
  gates.

Final acceptance completed:

- The E2E host covers Build, exact-range Expand, Host history handle, Compress,
  Extract, Promote, Supersede, and completed replay without a second mutation.
- The linux-x64 NativeAOT fixture covers generated nested contracts, Build,
  exact-range Expand, Compress, Extract, Promote, and completed replay; it does
  not claim Memory.Llm AOT capability.
- Canonical solution wiring, focused suites, full `CrestCreates.slnx` build,
  and all three Agent/MCP/Memory Tool NativeAOT gates pass. `memory.md` is
  updated only after this evidence is committed.

Exact project paths may be added to the canonical solution during Task 19; the
final gate must include at least:

```bash
dotnet test tests/Metadata/Core/CrestCreates.Schema.Tests
dotnet test tests/Metadata/Core/CrestCreates.Metadata.Tests
dotnet test tests/Runtime/Capability/CrestCreates.Capability.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Llm.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.E2E.Tests
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
dotnet test tests/Integrations/CrestCreates.Mcp.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.E2E.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.AotFixture.Tests
dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.AotFixture.Tests
dotnet test tests/Integrations/CrestCreates.Mcp.AotFixture.Tests
dotnet build solutions/CrestCreates.Runtime.slnx
dotnet build solutions/CrestCreates.All.slnx
```

If full-solution tests surface an unrelated environment-dependent integration
suite, report it separately; it does not replace any focused or NativeAOT gate.

## Final review checklist

Before declaring Phase 8d+ complete, explicitly verify:

1. the seven Tool operations use one generated Capability execution path;
2. nested JSON uses Schema v3 and source-generated contexts only;
3. selected Provider/Module handler DI is isolated per Host;
4. exact InvocationBinding is propagated, never recomputed by Memory;
5. closed-world exact-version visibility and anti-probing are enforced;
6. persistent identities and provenance are framework-owned and opaque;
7. security artifacts and all legal output branches are prepared before writes;
8. final output uniquely matches one prepared receipt;
9. common/output/branch audit facts have the Revision 7 ownership split;
10. global governance-outcome-v2 contains no replay payload or Memory text;
11. curation trusts the selected Promotion Service's ConfirmedAtomic proof;
12. concurrent Candidate consumption yields one winner;
13. Completed replay repeats no side effect and Indeterminate remains fenced;
14. existing Agent Tool/MCP contracts and AOT gates remain green; and
15. the new Memory Tool E2E and linux-x64 native binary provide executable
    evidence for Build, Promote, audit facts, and replay.
