# Phase 9b+ Durable Agent Memory Store Provider Implementation Plan

> Implement Issue #55 through ordered Case-first TDD slices. The approved R3
> Spec is normative. This Plan fixes project placement, provider-neutral
> semantics, migration/SQL ownership, transaction modes, DI composition,
> executable case mapping, crash/restart evidence, and NativeAOT closure so an
> implementation Agent does not need to reopen architecture decisions.

**Goal:** Add one direct-Npgsql durable implementation for each existing Agent
Memory Store contract, preserve the existing domain/read/accountability
mainlines, share one curation truth across InMemory and PostgreSQL, and prove
tenant isolation, restart durability, atomic lifecycle/graph mutation,
failure taxonomy, and original-binary NativeAOT execution.

**Spec:** `docs/superpowers/specs/2026-08-13-phase-9bplus-durable-agent-memory-store-provider-design.md`

**Issue:** #55

**Branch:** `codex/issue-55-durable-agent-memory-store-spec`

**Spec status:** APPROVED — R3 review passed

**Plan status:** IMPLEMENTED — PR #77 (codex/issue-55-durable-agent-memory-store-spec); review round 1 P0/P1 remediations applied (persisted Candidate integrity, evidence semantic audit, reciprocal graph validation, deep replay equality, sanitization boundary, batch composition, identity boundary, AOT #56 closure)

**Review revision:** Preserves the Spec §18 acceptance skeleton as an independent
44-name contract, separates discovery/activation guards from owning-project
execution evidence, and adds the permanent activation guard command to every
behavioral Slice gate

```text
Durable provider:           existing direct-Npgsql Runtime Persistence project
Migration:                  V010_agent_memory_durable_store
Base provider registration: feature-neutral
Memory provider registration: explicit opt-in
Curation transaction:       provider-owned top-level COMMIT boundary
Shared semantics:           hash projector + curation projector/state machine
SaveMemory:                 Active/non-authoritative/unlinked create or exact replay
Observable list order:      final StringComparer.Ordinal after materialization
Accountability:             unchanged #56 post-result mainline; no Store writes
NativeAOT evidence:         publish + link + execute original linux-x64 binary
```

---

## 1. Execution and Handoff Rules

### 1.1 Session preflight

Before the first edit or build in every implementation session:

```bash
rtk --version
rtk dotnet --info
rtk git status --short --branch
rtk git rev-parse HEAD
```

Then read, in this order:

1. `AGENTS.md` and `/home/orches/.codex/RTK.md`;
2. the approved #55 Spec named above;
3. this Plan;
4. the immediately preceding Slice handoff and `git diff`/test evidence.

The implementing Agent must record the observed baseline commit in its handoff.
If V010 already exists or V009 is no longer the migration tail, stop and request
a design/merge reconciliation; never renumber or overwrite an applied migration
silently.

### 1.2 Change discipline

- Use `rtk` for every shell command and `apply_patch` for source/document edits.
- Preserve unrelated worktree changes. Stage only the active Slice's files.
- Never delete directly. Move retired files to
  `99_RecycleBin/Phase9bPlusDurableAgentMemory/` and update references.
- Activate every behavior in its owning Slice by adding the named Red test(s).
  A Red must fail because the
  contract/behavior is missing, not because the fixture or DI container cannot
  start for an unrelated reason.
- Slice 1 freezes inactive manifest evidence and runner shapes; it does not add
  future xUnit wrappers or shared case methods that deliberately fail across
  Slice boundaries. A future evidence requirement becomes executable only in
  its owning Slice: add wrapper/case → observe Red → implement → Green.
- Make the smallest coherent mainline change that turns the focused cases
  Green. Do not add a temporary provider fallback or duplicate state machine to
  make an intermediate Slice pass.
- After Green, run the Slice regression set, dependency boundaries where
  relevant, `rtk dotnet build` for every changed production project, and
  `rtk git diff --check`.
- Each Slice ends with one reviewable commit. If a Slice must be split for size,
  all intermediate commits must compile and the final Slice commit must satisfy
  the complete Slice gate.
- Do not update `memory.md` to Implemented or NativeAOT-verified before Slice 11
  executes the newly published original binary and records the actual evidence.

### 1.3 Strict ownership rules

- `CrestCreates.Agent.Memory.Abstractions` owns only provider-neutral contracts,
  mutation snapshots, and comparer/projector interfaces. It never references
  PostgreSQL, Npgsql, concrete Agent Memory runtime, ReadCore, Tools, or
  Accountability runtime.
- `CrestCreates.Agent.Memory` owns the default hash/curation/comparer
  implementations, runtime registration, InMemory alignment, sanitization,
  recall, expansion, and formal promotion orchestration.
- `CrestCreates.Runtime.Persistence.PostgreSql` owns SQL, migration V010,
  structured columns, row/advisory locking, persisted-invariant validation,
  transaction/commit taxonomy, durable Store implementations, and explicit
  provider registration. It references only
  `CrestCreates.Agent.Memory.Abstractions`, never the concrete Memory runtime.
- `CrestCreates.Agent.Memory.Persistence.Testing` is runner-free and references
  only Agent Memory Abstractions. It owns reusable semantic Store/curation
  contract cases and test-driver contracts, not xUnit or a provider.
- #56 remains the only semantic Memory Accountability bridge. No Store injects
  or calls `IAgentMemoryAccountabilityProducer`, `IAuditRecorder`, or
  `IAuditSink`.

### 1.4 Prohibited shortcuts

Do not add:

- EF Core, a second connection pool, a second transaction coordinator, or a
  second migration history table;
- reflection JSON, runtime `Type`, `object?` payload roots, dynamic Npgsql JSON,
  `DefaultJsonTypeInfoResolver`, or trimming/AOT suppressions;
- provider-local canonical hash writers, curation lifecycle logic, promotion
  projection, recall ranking, budget/visibility logic, or pack hashing;
- a privileged `SaveMemoryAsync` import bypass;
- Store-emitted Accountability, outbox delivery, operation receipts, automatic
  retry after commit-unknown, vector search, retention, TTL, GC, or Stale state;
- ambient-transaction suspension or hidden second-connection commit for formal
  curation;
- SQL exception catch-alls that manufacture domain conflicts;
- SQL ordering as the final public ordering contract.

### 1.5 Multi-Agent handoff protocol

Implementation may be handed between Agents, but the Slices are sequential.
Do not let two Agents modify any of these shared hotspots concurrently:

```text
AgentMemoryInterfaces.cs
AgentMemoryServiceCollectionExtensions.cs
PostgreSqlRuntimeTransactionCoordinator.cs
PostgreSqlRuntimeMigrationRunner.cs
PostgreSqlRuntimeJsonSerializerContext.cs
PostgreSqlRuntimePersistenceServiceCollectionExtensions.cs
CrestCreates.slnx / solutions/CrestCreates.Runtime.slnx /
solutions/CrestCreates.All.slnx
```

Every handoff message must contain:

```text
Slice completed / Slice next
commit SHA
files changed
Red tests observed
Green commands and exact pass counts
known environment limitations
unresolved review items (must be zero to advance)
evidence tuples closed by the Slice: (CaseId, EvidenceKind, TestName)
```

An Agent receiving a Slice must verify the predecessor commit and rerun at least
the predecessor's focused Green command before editing a shared hotspot.

---

## 2. Ordered Delivery Map

| Slice | Deliverable | Required case groups | Must not include |
|---|---|---|---|
| 1 | Inactive acceptance/evidence ledger, runner-free shapes, project graph | structural coverage for H01–H09, B01–B18, F01–F16, C01–C16; no future Reds | production Store SQL or future wrappers |
| 2 | Shared hash/curation/comparer semantics and InMemory alignment | every IMS@2 evidence tuple in §17, including Store basics and H04–H06/H09, B09/B14/B16–B18, F01/F02/F04, C16 | PostgreSQL mapping or durable/concurrency closure |
| 3 | V010, schema validator closure, JSON roots, explicit DI shape | Migration/Boundary/JsonArchitecture and DI-selection evidence for C03/C09/C10/C14/C15; PostgreSQL curation remains Unknown/fail-closed | truthful ConfirmedAtomic or Green curation validator |
| 4 | Durable Conversation and Task Stores | PGS/PGR@4 H01/H02, PGC/PGR@4 B07/B08, PGS@4 B17, PGF@4 F12 | Context/Memory SQL |
| 5 | Durable Context and Block projection | PGS/PGR@5 H03, PGS@5 B03-B06, PGF@5 F13/F16 | curation SQL |
| 6 | Candidate/Memory base Store and query parity | PGS@6 H09, B01/B02/B09/B10/B12/B14/B16/B18 | formal curation writes |
| 7 | Atomic Promote and Reject | PGS@7 H04/F01/F02/F04/C16, PGF@7 F15, ACC@7 C13; capability stays Unknown | ConfirmedAtomic, Green validator, Supersede/Archive |
| 8 | Atomic Supersede and Archive; truthful capability activation | PGS@8 H05/H06, PGR@8 H06, PGD@8 C01/C02 | crash/AOT claims |
| 9 | Concurrency, failure injection, real process crash | remaining PGC@9/PGF@9/CW@9 tuples from §17 | Recall/AOT closure |
| 10 | Restart Recall/Expansion/#56 composition parity | all REP@10/PGR@10/ACC@10/BND@10 tuples from §17 | new read/accountability semantics |
| 11 | NativeAOT, canonical build, complete evidence ledger, docs | C11/C12 plus every remaining required evidence tuple across all 59 cases | scope expansion |

Each Slice is independently buildable and reviewable. Do not begin a later Slice
while an earlier Slice has an unresolved *activated* Red, an unmigrated production caller,
a failing architecture guard, or unreviewed changes in a shared hotspot.
Inactive future evidence in the manifest is not a test result and is never
reported as Red, skipped, or Green.

---

## 3. Final Project and Dependency Graph

### 3.1 Production graph

```text
CrestCreates.Agent.Memory.Abstractions
    -> existing Core/Metadata abstractions
    X  CrestCreates.Agent.Memory
    X  Runtime.Persistence.PostgreSql

CrestCreates.Agent.Memory
    -> CrestCreates.Agent.Memory.Abstractions
    -> existing Metadata canonical hash runtime
    X  Runtime.Persistence.PostgreSql

CrestCreates.Runtime.Persistence.PostgreSql
    -> Runtime.Persistence.Abstractions
    -> Workflow/HumanTask/Accountability/Agent.Tools abstractions (existing)
    -> CrestCreates.Agent.Memory.Abstractions (new)
    -> Npgsql
    X  CrestCreates.Agent.Memory
    X  Agent.Memory.ReadCore/Tools/Accountability
```

The PostgreSQL test/AOT projects may reference the concrete Agent Memory runtime
to compose the real Host. That test dependency does not authorize the production
provider project to do so.

### 3.2 New runner-free shared project

Create:

```text
tests/Shared/CrestCreates.Agent.Memory.Persistence.Testing/
  CrestCreates.Agent.Memory.Persistence.Testing.csproj
  TestingBoundaryMarker.cs
  AgentMemoryStoreContractTestBase.cs
  AgentMemoryCurationStoreContractTestBase.cs
  Drivers/IAgentMemoryStoreContractDriver.cs
  Drivers/IAgentMemoryDurabilityContractDriver.cs
  Fixtures/AgentMemoryPersistenceContractFixture.cs
  Cases/AgentMemoryStoreContractCases.cs
  Cases/AgentMemoryCurationStoreContractCases.cs
  Assertions/AgentMemoryPersistenceContractAssertions.cs
  Assertions/AgentMemoryPersistenceContractAssertionException.cs
  Manifest/DurableAgentMemoryCaseManifest.cs
  Manifest/DurableAgentMemorySpecTestSkeleton.cs
```

Project rules:

```xml
<IsTestProject>false</IsTestProject>
<IsPackable>false</IsPackable>
```

It references only:

```text
src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions
```

It contains no Test SDK, xUnit, FluentAssertions, concrete runtime, Npgsql,
Testcontainers, or provider reference. Concrete xUnit runner projects expose
each required evidence test only when its owning Slice activates it, then
delegate provider-neutral behavior to these cases.

### 3.3 Production files created by the completed implementation

Provider-neutral semantics:

```text
src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/
  CanonicalHashing/IAgentMemoryStateHashProjector.cs
  Curation/IAgentMemoryCurationProjector.cs
  Curation/IAgentMemoryCurationStateMachine.cs
  Curation/AgentMemoryCurationMutationSnapshots.cs
  Persistence/IAgentMemoryPersistenceComparer.cs

src/Runtime/Agent/CrestCreates.Agent.Memory/
  Curation/DefaultAgentMemoryCurationProjector.cs
  Curation/DefaultAgentMemoryCurationStateMachine.cs
  Persistence/DefaultAgentMemoryPersistenceComparer.cs
```

PostgreSQL provider:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  PostgreSqlAgentMemoryPersistenceServiceCollectionExtensions.cs
  PostgreSqlAgentMemoryStoreSupport.cs
  PostgreSqlAgentMemoryRowMapper.cs
  PostgreSqlAgentMemoryLockManager.cs
  PostgreSqlAgentConversationStore.cs
  PostgreSqlAgentTaskHistoryStore.cs
  PostgreSqlAgentCompressedContextStore.cs
  PostgreSqlAgentMemoryStore.cs
```

The exact helper split may be reduced during implementation when two helpers
would be trivial, but responsibilities must not be copied across four Stores.
SQL identifiers, serialization, row validation, revision/timestamp binding,
advisory-lock key creation, and common error construction each have one owner.

### 3.4 Test files created by the completed implementation

InMemory runner additions:

```text
tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/
  Persistence/InMemoryAgentMemoryStoreContractTests.cs
  Persistence/InMemoryAgentMemoryCurationContractTests.cs
  Curation/AgentMemoryCurationProjectorTests.cs
  Curation/AgentMemoryCurationStateMachineTests.cs
  Persistence/AgentMemoryPersistenceComparerTests.cs
  Architecture/DurableAgentMemorySemanticArchitectureTests.cs
```

PostgreSQL runner additions:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlAgentMemoryContractTests.cs
  PostgreSqlAgentMemoryCurationContractTests.cs
  PostgreSqlAgentMemoryRestartTests.cs
  PostgreSqlAgentMemoryConcurrencyTests.cs
  PostgreSqlAgentMemoryCrashTests.cs
  PostgreSqlAgentMemoryFailureTaxonomyTests.cs
  PostgreSqlAgentMemoryMigrationTests.cs
  PostgreSqlAgentMemoryCompositionTests.cs
  PostgreSqlAgentMemoryRecallExpansionTests.cs
  Fixtures/PostgreSqlAgentMemoryContractDriver.cs
```

Extend:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/
  AgentMemoryCrashScenarios.cs
  Program.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/
  Program.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/
  PostgreSqlRuntimeAotFixtureTests.cs
```

Add the shared project to `CrestCreates.slnx`,
`solutions/CrestCreates.Runtime.slnx`, and
`solutions/CrestCreates.All.slnx`. The Runtime solution already owns the Agent
Memory runtime, PostgreSQL provider/tests, Boundary tests, and Runtime
Persistence shared testing; omitting the new contract project would make that
sub-solution incomplete. Existing production and test projects are already
present and must not be duplicated.

---

## 4. Frozen Cross-Slice Implementation Decisions

### 4.1 Shared semantic object model

Use injected interfaces, not static provider helpers:

```csharp
public interface IAgentMemoryStateHashProjector
{
    CanonicalHash ComputeCandidateStateHash(AgentMemoryCandidate candidate);
    CanonicalHash ComputeMemoryStateHash(AgentMemoryItem memory);
}

public interface IAgentMemoryCurationProjector
{
    AgentMemoryItem ProjectPromotedMemory(...);
    AgentMemoryCandidate ProjectCandidateStatus(...);
    AgentMemoryItem ProjectSupersededMemory(...);
    AgentMemoryItem ProjectSupersedingMemory(...);
    AgentMemoryItem ProjectArchivedMemory(...);
}

public interface IAgentMemoryCurationStateMachine
{
    AgentMemoryPromoteMutation PreparePromote(...);
    AgentMemoryRejectMutation PrepareReject(...);
    AgentMemorySupersedeMutation PrepareSupersede(...);
    AgentMemoryArchiveMutation PrepareArchive(...);
}

public interface IAgentMemoryPersistenceComparer
{
    bool Equals(AgentMemoryItem left, AgentMemoryItem right);
}
```

Method parameter details follow the approved Spec and existing Plan/
Expectation records. Mutation records contain detached snapshots only; they do
not contain Npgsql commands, revisions, timestamps, or Accountability data.

`AgentMemoryCanonicalHashProjector` implements
`IAgentMemoryStateHashProjector`. `DefaultAgentMemoryCurationProjector` is the
only Candidate→Memory/lifecycle/graph projector.
`DefaultAgentMemoryCurationStateMachine` combines locked snapshots, exact
expectations, the projector, and hash projector. Both Stores consume it.
`DefaultAgentMemoryPromotionService` consumes the same projector for plan
preparation and deletes its private `CreatePromotedMemory` implementation.

Ownership is strict:

```text
Store:
    lookup/locked-resource existence -> ResourceUnavailable
    new identity availability        -> IdentityConflict

State machine (loaded snapshots only):
    Tenant mismatch                  -> TenantMismatch
    lifecycle mismatch               -> InvalidLifecycleState
    expectation/hash/projection drift -> StateConflict
```

The state machine never receives an optional/missing row and never queries an
identity registry/Store.

### 4.2 Store construction dependencies

Final constructor ownership:

```text
Conversation Store:
    coordinator + sanitizer + JSON context/type info

Task Store:
    coordinator + sanitizer + JSON context/type info

Context Store:
    coordinator + lock manager + JSON context/type info

Memory Store:
    coordinator + lock manager + state hash projector
    + curation state machine + exact persistence comparer
    + JSON context/type info
```

Provider Store constructors do not accept Retriever, Source Expander,
Accountability producer/recorder/sink, Agent Tool handler, MCP handler, or
concrete `AgentMemoryCanonicalHashProjector`.

### 4.3 Transaction modes

Refactor `PostgreSqlRuntimeTransactionCoordinator` around one private owned
transaction implementation:

```text
ExecuteAsync:
    ambient exists -> join it
    otherwise      -> ExecuteOwnedAsync

ExecuteTopLevelAsync:
    ambient exists -> throw AmbientCommitBoundaryUnsupported before work
    otherwise      -> ExecuteOwnedAsync
```

Add `AmbientCommitBoundaryUnsupported = 5` to
`RuntimePersistenceContractErrorCode`; preserve existing numeric values.
Both modes retain the current unavailability, rollback, and commit-unknown
translation. Formal curation calls only `ExecuteTopLevelAsync`; ordinary Store
writes call `ExecuteAsync`.

### 4.4 SQL and locking rules

- Every caller value is an Npgsql parameter. Only the already validated and
  quoted configured schema is interpolated.
- Advisory identity text is exactly
  `agent-memory | tenant | artifact-kind | artifact-id` before the existing
  deterministic lock-key hash/conversion. Multiple identities are ordinally
  sorted and de-duplicated before acquisition.
- Curation lock order is: new identity advisory locks → target Memory rows by
  `MemoryId` ordinal → Candidate rows by `CandidateId` ordinal.
- Task append and all read-modify-write operations use `SELECT ... FOR UPDATE`.
- Domain conflicts use prechecks or `ON CONFLICT DO NOTHING RETURNING`; no
  expected conflict depends on a unique violation escaping the coordinator.
- Every row mutation updates JSON, structured projections, state hash where
  applicable, revision, and `updated_at` in the same statement/transaction.
- Exact Memory replay performs no UPDATE and does not change revision or
  timestamps.

### 4.5 Serialization and read validation

All six aggregate types are exact roots of
`PostgreSqlRuntimeJsonSerializerContext`. Every serialize/deserialize call uses
the generated `JsonTypeInfo<T>` property directly.

Every read validates version, positive revision, top-level Tenant/ID, enum and
structured projection parity, and canonical/state hashes before returning a
detached snapshot. Memory graph reads additionally validate reciprocal links.
Direct Block lookup loads and validates the parent Context and exact ordinal
slot. Corruption always throws
`RuntimePersistenceContractException(PersistedInvariantViolation)`.

### 4.6 DI composition

`AddCrestCreatesPostgreSqlRuntimePersistence(options)` remains unchanged in
feature scope: it registers the existing kernel and existing durable
participants only.

`AddCrestCreatesPostgreSqlAgentMemoryPersistence()`:

1. removes the four development Store contracts;
2. registers three concrete PostgreSQL Stores through their interfaces;
3. registers one `PostgreSqlAgentMemoryStore` singleton as
   `IAgentMemoryStore` only.

The concrete Memory Store implements conditional curation and capabilities.
Consumers and #56 discover those interfaces by casting the selected
`IAgentMemoryStore`; do not register independent conditional/capability service
descriptors.

Capability is truthful by implementation phase:

```text
Slices 3-7:
    PostgreSqlAgentMemoryStore.CurationOutcomeGuarantee == Unknown
    formal-curation Host validator fails closed

Slice 8 onward, only after all four primitives are Green:
    Promote + Reject + Supersede + Archive implemented atomically
    CurationOutcomeGuarantee changes to ConfirmedAtomic
    formal-curation Host validator becomes Green
```

Do not use a build symbol, environment flag, or test override to change the
guarantee. The Slice 8 production change that completes the fourth primitive
also changes the constant implementation to `ConfirmedAtomic`.

### 4.7 Test-only failure hooks

Extend the existing internal `PostgreSqlRuntimeTestHooks` pattern with narrowly
scoped one-shot Agent Memory hooks rather than production behavior switches:

```text
after each curation SQL write point
before provider-owned top-level COMMIT
after COMMIT submission / before acknowledgement result (existing taxonomy path)
```

Hooks are internal, concurrency-safe, reset by `IDisposable`, and visible only
to the existing PostgreSQL tests/CrashWorker through current friend-assembly
mechanisms. No public options or environment-dependent production branch is
added.

Because the current hook registry is process-global static state, every xUnit
test class that installs a hook, coordinates a blocked COMMIT/write point, or
shares a CrashWorker backend gate must use the existing
`[Collection(PostgreSqlRuntimeCollection.Name)]`. Such tests must not run in
parallel with another hook-using class. If implementation instead makes hooks
keyed/test-scoped, the key must be carried explicitly by the Store/session and
tests must prove two keys cannot consume each other's one-shot hook. One of
these two isolation models is mandatory; a process-global uncollected hook is
prohibited.

---
## 5. Slice 1 — Acceptance Ledger and Runner-Free Contract Kit

**Purpose:** Freeze all 59 approved design cases and every required evidence
tuple before production behavior changes, without creating tests that remain
Red beyond this Slice. Establish the provider-neutral driver/case shapes that
later Slices activate incrementally for InMemory and PostgreSQL.

### 5.1 Files

Create the shared project and files listed in §3.2. Modify:

```text
CrestCreates.slnx
solutions/CrestCreates.Runtime.slnx
solutions/CrestCreates.All.slnx
tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/
  CrestCreates.Agent.Memory.Tests.csproj
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj
tests/Boundary/CrestCreates.DependencyBoundaries.Tests/
  DurableAgentMemoryPersistenceArchitectureTests.cs
```

The two concrete test projects reference the shared project. The PostgreSQL
test project also gains a direct reference to
`src/Runtime/Agent/CrestCreates.Agent.Memory/CrestCreates.Agent.Memory.csproj`
because it composes real provider-neutral semantic implementations; the
production PostgreSQL project does not gain that reference.

### 5.2 Driver contract

The shared driver exposes only Store contracts, lifecycle/setup utilities, and
provider-neutral plan preparation:

```text
IAgentMemoryStoreContractDriver
    ConversationStore
    TaskStore
    ContextStore
    MemoryStore
    ResetAsync / DisposeAsync
    CreateFreshReaderAsync (optional durability capability)
    PrepareCandidateExpectation(candidate)
    PrepareMemoryExpectation(memory)
    PreparePromotionPlan(candidate, newMemoryId, operation)
    PrepareSupersessionPlan(targetMemory, replacementCandidate, newMemoryId,
                            operation)

IAgentMemoryDurabilityContractDriver
    inherits/contains store driver semantics
    RebuildProviderAsync
    ReadRawRevisionAsync only through a typed test observation result
```

Do not expose `NpgsqlConnection`, SQL, schema names, a concrete InMemory Store,
or provider-specific exceptions through the shared interface. Cases that
tamper rows or kill processes remain PostgreSQL-only and are represented in the
case manifest, not forced into the provider-neutral driver.

Shared cases never hard-code a state hash and never reproduce
Candidate→Memory/Supersede projection. Concrete runners implement the four
preparation methods with the real `IAgentMemoryStateHashProjector` and
`IAgentMemoryCurationProjector` registered by Slice 2. This keeps the
runner-free project dependent only on Abstractions while preserving one hash
and projection truth. Until those interfaces exist, Slice 1 freezes only the
driver signatures; it does not activate curation cases.

### 5.3 Case manifest and enforcement

Slice 1 freezes two independent contracts. Neither substitutes for the other:

```text
DurableAgentMemorySpecTestSkeleton
    SharedRequiredMethodNames[31]       // exact Spec §18.1 names
    PostgreSqlRequiredGroupNames[6]     // exact Spec §18.2 class/group names
    PostgreSqlRequiredMethodNames[7]    // exact Spec §18.2 method names
    SpecRequiredTestNames[44]           // exact union of the three arrays
    each entry also records OwningSlice for activation discovery

DurableAgentMemoryCaseManifest
    CaseEvidence[59 Cases / 98 RequiredEvidence tuples]
```

The skeleton contains **44 exact names**. It preserves the approved Design's
minimum test API even when §17 uses a more descriptive evidence test basename.
For example, the evidence name
`Promote_Should_CommitCandidateAndMemoryAtomically` does not replace the
required skeleton method `Promote_Should_Be_Atomic`; both contracts must close.

`DurableAgentMemorySpecTestSkeletonTests` parses only Spec §18.1/§18.2 fenced
name blocks and proves exact equality with the three arrays (31/6/7), including
case and spelling. It also rejects duplicate entries and an owning Slice outside
2-11. The parser must not derive skeleton names from this Plan's §17 tables.

`DurableAgentMemoryCaseManifest` contains one typed entry per Spec §17 ID:

```text
H01-H09
B01-B18
F01-F16
C01-C16
```

The ledger is evidence-oriented, not a single Case-complete flag. Each Case
records:

```text
CaseId
RequiredEvidence[]
    EvidenceKind
    ExactFullyQualifiedTestName
    OwningSlice
```

Allowed `EvidenceKind` values are:

```text
InMemorySemantic
PostgreSqlSemantic
PostgreSqlConcurrency
PostgreSqlRestart
PostgreSqlFailureInjection
CrashWorker
PostgreSqlComposition
AccountabilityComposition
RecallExpansionParity
Migration
JsonArchitecture
Boundary
NativeAot
CanonicalBuild
```

A Case with both semantic and durable/concurrency obligations has multiple
requirements. One Green InMemory wrapper cannot close its PostgreSQL evidence.
Evidence tuples are unique by `(CaseId, EvidenceKind,
ExactFullyQualifiedTestName)`.

Add an architecture test that parses the approved Spec's §17 tables, compares
the exact ID set with the manifest, rejects duplicate/gapped Case IDs, rejects
duplicate evidence tuples, validates every Case has at least one requirement,
validates every owning Slice is 2-11, and asserts the frozen §17 total/per-kind
counts (59 Cases, 98 evidence tuples). The parser must stop at §18 so later
numbers cannot satisfy the ledger accidentally.

Discovery is Slice-aware:

```text
Slice N adds one permanent SliceNEvidenceActivationTests guard:
    every Spec skeleton entry with OwningSlice == N exists as the required
    method or test-group class
    every requirement with OwningSlice == N has an activated concrete test
    with the exact fully-qualified name
    predecessor Slice activation guards remain Green
    requirements with OwningSlice > N are manifest reservations, not tests

Slice 11 gate:
    AllDurableAgentMemoryEvidenceTests proves discovery completeness only:
    every skeleton name and manifest evidence tuple is activated/discovered
```

Do not maintain a mutable `CurrentSlice`/completion flag in production or test
source. Each permanent activation guard checks one frozen ownership cohort;
full closure is the union of guards 2-11.

Every behavioral Slice creates its permanent guard and runs:

```bash
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~SliceNEvidenceActivationTests"
```

Replace `N` with the current Slice number and verify `--list-tests` selects the
guard. Slice 11 additionally runs an `AllDurableAgentMemoryEvidenceTests` union
guard over all 44 skeleton names and 98 evidence tuples.

Activation guards never launch `dotnet test` recursively and never claim that
another test assembly passed. Their only authority is static/metadata discovery
of exact classes and methods. The owning concrete test-project command in each
Slice gate proves execution/Green. The Slice handoff records that exact command,
discovered count, executed count, passed count, and zero skipped/failed count.
Final 98/98 passing status is derived from those actual project runs plus the
discovery-completeness guard, not from the Boundary guard alone.

The manifest never contains a mutable `Implemented`/`Green` boolean. Test
execution is the evidence; source metadata cannot declare itself passing.

### 5.4 Structural scaffold — no behavioral Red

Create the complete inactive evidence manifest, driver interfaces, fixture
types, assertion helpers, and empty/abstract runner base shapes. Do **not** add
future xUnit wrapper methods and do **not** add shared case methods that throw
`"Not implemented"`.

Slice 1 is purely structural: all behavioral evidence is owned by Slices 2-11,
and no Store/curation xUnit wrapper is activated here. No skipped, disabled, or
deliberately failing test may remain at the Slice gate.

Run the structural tests and expect Green:

```bash
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~DurableAgentMemoryPersistenceArchitectureTests"

rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~DurableAgentMemoryEvidenceManifestTests"
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~DurableAgentMemorySpecTestSkeletonTests"
```

Both commands must pass. No future behavioral filter is run because no future
wrapper exists yet.

### 5.5 Green scope

- Complete only project/manifest/driver/fixture structure.
- Missing Task append, SaveMemory hardening, curation preparation, and non-BMP
  semantic wrappers are first added as Red in Slice 2 and made Green there.
- PostgreSQL wrappers are first added as Red in their owning Slices 3-10. There
  is no placeholder wrapper, capability-assertion Red, Skip, or
  `NotImplementedException` standing in for future evidence.

### 5.6 Slice gate

```bash
rtk dotnet build tests/Shared/CrestCreates.Agent.Memory.Persistence.Testing
rtk dotnet build tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
rtk dotnet build tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests
rtk dotnet build solutions/CrestCreates.Runtime.slnx -c Release
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~DurableAgentMemoryPersistenceArchitectureTests"
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~DurableAgentMemoryEvidenceManifestTests"
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~DurableAgentMemorySpecTestSkeletonTests"
rtk git diff --check
```

Review must confirm the shared project has no runner/provider dependency and
all 44 Spec skeleton names, 59 IDs, and 98 §17 evidence tuples are represented,
while no future Red/Skip wrapper has been activated.

**Suggested commit:**

```text
test: scaffold durable agent memory provider contract ledger
```

---

## 6. Slice 2 — Shared Semantics and InMemory Alignment

**Purpose:** Close semantic drift before adding SQL. After this Slice there is
one promotion/lifecycle projection truth, one state-hash interface, one exact
Memory comparer, and InMemory conforms to every provider-neutral case.

### 6.1 Files

Create the provider-neutral/default files listed in §3.3 and the Slice 2 test
files listed in §3.4. Modify:

```text
src/Runtime/Agent/CrestCreates.Agent.Memory/
  CanonicalHashing/AgentMemoryCanonicalHashProjector.cs
  Promotion/DefaultAgentMemoryPromotionService.cs
  Stores/InMemoryAgentMemoryStore.cs
  Stores/InMemoryAgentTaskHistoryStore.cs
  AgentMemoryServiceCollectionExtensions.cs

src/Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/
  AgentMemoryInterfaces.cs (only if interface placement requires it)

tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests/
  MemoryTestFixture.cs
  MainChainTests.cs
  AgentMemoryConditionalArchiveContractTests.cs
  AgentMemoryCurationAccountabilityTests.cs
  AgentMemoryRuntimeRegistrationTests.cs
```

Before editing, enumerate both private projection copies and every construction
site:

```bash
rtk rg -n "CreatePromotedMemory|ComputeCandidateStateHash|ComputeMemoryStateHash|EquivalentMemoryPayload|new InMemoryAgentMemoryStore" src tests
```

Every discovered production caller is migrated in this Slice.

### 6.2 Red — state hash and pure projector

Activate the Slice 2 `InMemorySemantic` evidence wrappers and focused tests;
observe them Red before production edits. Add tests proving:

- `AgentMemoryCanonicalHashProjector` resolves through
  `IAgentMemoryStateHashProjector` as the same singleton;
- Candidate→Memory transfer preserves every approved payload/provenance field;
- `PromotedAt` comes only from `Operation.Identity.OccurredAt`;
- promoted Memory is Active and non-authoritative;
- Candidate transition snapshots do not mutate input;
- Supersede produces reciprocal links and correct statuses;
- Archive retains both pre-existing graph links;
- caller mutation of input collections cannot mutate returned snapshots.

Run and expect Red:

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests \
  --filter "FullyQualifiedName~AgentMemoryCurationProjectorTests"
```

### 6.3 Green — one projector

- Implement `DefaultAgentMemoryCurationProjector` as a pure singleton.
- Replace `DefaultAgentMemoryPromotionService.CreatePromotedMemory` with the
  injected interface when preparing `ExpectedMemoryStateHash`.
- Do not let the Promotion Service call the state machine or Store during plan
  construction.
- Preserve existing operation identity, reason/explanation, and #56 publication
  flow; only projection ownership changes.

### 6.4 Red/Green — state machine

Add tests for exact tenant/state/content/new-state expectation validation and
all legal/illegal lifecycle sources. Expected failures are typed
`AgentMemoryOperationException` values with only:

```text
TenantMismatch
InvalidLifecycleState
StateConflict
```

The Store, not the state machine, owns `ResourceUnavailable` for a missing
locked resource and `IdentityConflict` for an occupied new identity. State
machine inputs are already-loaded current snapshots plus expectations/plans; it
must have no “not found” or identity-availability query/result path. Lock this
ownership into tests: Store contract cases produce ResourceUnavailable/
IdentityConflict, while direct state-machine tests cannot construct those
outcomes.

Implement detached mutation records for Promote, Reject, Supersede, Archive.
The state machine must call both shared projector interfaces and must not know
about revisions, SQL, locking, or Accountability.

Run:

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests \
  --filter "FullyQualifiedName~AgentMemoryCurationStateMachineTests"
```

### 6.5 Red/Green — exact persistence comparer

Parameterize one baseline `AgentMemoryItem`, changing exactly one persisted
field per test. Prove every change is unequal, including collection order and
nested snapshot fields:

```text
TenantId, MemoryId, Kind, Content, CanonicalContentHash, PromotedAt,
Confidence, Status, IsAuthoritative, Tags, DescriptorRefs, SourceRefs,
SupersedesMemoryId, SupersededByMemoryId, RedactionKinds,
SanitizationDiagnostics
```

Equal deep snapshots must compare equal. Do not use state-hash equality as the
implementation.

### 6.6 InMemory cutover

Refactor `InMemoryAgentMemoryStore` to inject and use the shared state machine
and comparer. Remove its private projection, lifecycle validation, expectation
validation, and partial `EquivalentMemoryPayload` copies.

Harden `SaveMemoryAsync`:

```text
missing identity:
    require Active
    require IsAuthoritative == false
    require both graph links null
    otherwise InvalidLifecycleState with zero mutation

existing identity:
    exact comparer equal -> return without replacing stored snapshot
    any difference       -> StateConflict with zero mutation
```

Change `InMemoryAgentTaskHistoryStore.AppendEventAsync` missing-task behavior to
`AgentMemoryOperationException(ResourceUnavailable)`. Close its lost-update
window with the existing per-store lock or an atomic dictionary update whose
delegate cannot execute sanitizer side effects multiple times. The simplest
correct path is a private `_gate` covering lookup, one sanitization, snapshot,
and replacement.

Keep final Task/Memory list ordering explicitly
`StringComparer.Ordinal`; add non-BMP identifiers that distinguish ordinal
UTF-16 ordering from UTF-8 byte ordering.

### 6.7 Runtime registration

`AddAgentMemoryReadRuntime()` registers:

```text
AgentMemoryCanonicalHashProjector concrete singleton
same instance -> IAgentMemoryStateHashProjector
DefaultAgentMemoryCurationProjector -> IAgentMemoryCurationProjector
DefaultAgentMemoryCurationStateMachine -> IAgentMemoryCurationStateMachine
DefaultAgentMemoryPersistenceComparer -> IAgentMemoryPersistenceComparer
```

These are Store prerequisites even in read-only composition. Do not move the
formal curation marker/service/validator out of `AddAgentMemoryCuration()`.

Registration-order tests must prove one instance per interface and no duplicate
descriptors after repeated extension calls.

Activate the InMemory runner's
`CurationCapabilities_Should_Be_ConfirmedAtomic` wrapper here because all four
InMemory primitives are Green in this Slice. This does not satisfy or activate
the PostgreSQL wrapper; that separate evidence belongs to Slice 8.

The concrete InMemory contract driver implements
`PrepareCandidateExpectation`, `PrepareMemoryExpectation`,
`PreparePromotionPlan`, and `PrepareSupersessionPlan` by resolving the real
shared hash/curation projectors. Shared cases never compute or hard-code hashes.

### 6.8 Slice gate

The InMemory runner must now pass every shared case that does not require
process/database durability. At minimum:

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests \
  --filter "FullyQualifiedName~InMemoryAgentMemoryStoreContractTests|FullyQualifiedName~InMemoryAgentMemoryCurationContractTests|FullyQualifiedName~AgentMemoryCurationProjectorTests|FullyQualifiedName~AgentMemoryCurationStateMachineTests|FullyQualifiedName~AgentMemoryPersistenceComparerTests"

rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests
rtk dotnet build src/Runtime/Agent/CrestCreates.Agent.Memory
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~DurableAgentMemory"
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~Slice2EvidenceActivationTests"
rtk git diff --check
```

Close every `IMS@2` evidence tuple enumerated in §17, including basic Store
semantics as well as H04-H06/H09, B09/B14/B16-B18, F01/F02/F04, and C16.
F03/F05/F06 remain inactive until their PostgreSQL concurrency/failure Slices;
C01/C02 remain incomplete for the PostgreSQL Store and cannot be closed by
InMemory evidence.

**Suggested commit:**

```text
refactor: unify agent memory curation store semantics
```

---

## 7. Slice 3 — V010 Schema, Manifest/JSON Closure, and Explicit DI

**Purpose:** Establish the complete durable shape and resolvable provider
composition before implementing Store behavior.

### 7.1 Files

Modify:

```text
src/Runtime/Persistence/CrestCreates.Runtime.Persistence.Abstractions/
  Errors/RuntimePersistenceContractErrorCode.cs

src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  CrestCreates.Runtime.Persistence.PostgreSql.csproj
  PostgreSqlRuntimeMigrationRunner.cs
  PostgreSqlRuntimeJsonSerializerContext.cs
  PostgreSqlRuntimePersistenceServiceCollectionExtensions.cs
  PostgreSqlRuntimeTransactionCoordinator.cs
```

Create:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  PostgreSqlAgentMemoryPersistenceServiceCollectionExtensions.cs
  PostgreSqlAgentMemoryStoreSupport.cs
  PostgreSqlAgentMemoryRowMapper.cs
  PostgreSqlAgentMemoryLockManager.cs
  PostgreSqlAgentConversationStore.cs
  PostgreSqlAgentTaskHistoryStore.cs
  PostgreSqlAgentCompressedContextStore.cs
  PostgreSqlAgentMemoryStore.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlAgentMemoryMigrationTests.cs
  PostgreSqlAgentMemoryCompositionTests.cs
```

The four Store classes may have compiling method shells that throw an explicit
not-yet-implemented exception during this Slice, but composition tests must not
invoke Store operations. `PostgreSqlAgentMemoryStore` implements the two cast
interfaces but reports `CurationOutcomeGuarantee.Unknown`. No production
fallback to InMemory is allowed, and no Host may accept formal curation yet.

### 7.2 Project reference Red/Green

Add only:

```xml
<ProjectReference Include="../../Runtime/Agent/CrestCreates.Agent.Memory.Abstractions/CrestCreates.Agent.Memory.Abstractions.csproj" />
```

to the PostgreSQL production project. Add architecture tests proving no
reference or namespace use of concrete Memory, ReadCore, Tools, or
Accountability classes from Agent Memory Store source/constructors.

### 7.3 Migration catalog Red

Before editing the catalog:

```bash
rtk rg -n "new RuntimeMigration\(\"V[0-9]+\"" \
  src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlRuntimeMigrationRunner.cs
```

Assert V009 is the tail. Add migration tests for:

- V010 appears once with stable checksum and exact name;
- Apply creates all six tables from Spec §9.2;
- Validation-only fails before Apply and succeeds after Apply;
- re-Apply does not mutate migration history or table contents;
- V001-V009 checksums/text remain unchanged;
- exact primary keys, checks, indexes, predicates, self-FKs, Block FK, and
  `state_contract_version` constraints exist;
- every required identity/order column has `C` collation;
- Block FK delete action is exactly CASCADE;
- Memory graph FKs have the approved delete action plus deferrable/initially
  deferred flags;
- no Stale column/status/TTL artifact exists.

The suite must expose the exact Spec §18.2 skeleton method
`V010Manifest_Should_ValidateCollationAndForeignKeyDeleteAction`. It may share
fixtures/assertion helpers with the broader C09 evidence method
`V010Manifest_Should_ValidateApplyChecksumShapeCollationAndForeignKeyDeleteAction`,
but the latter does not replace the frozen skeleton name.

Run and expect Red:

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryMigrationTests"
```

### 7.4 Manifest metadata closure

Refactor the private schema manifest records so columns carry:

```text
data type
nullability
expected collation (nullable)
```

and foreign keys carry:

```text
source/referenced columns
referenced table
deferrable
initially deferred
exact delete action
```

`ValidateTableColumnsAsync` reads `information_schema.columns.collation_name`.
`ValidateForeignKeyAsync` reads PostgreSQL delete-action metadata and maps it to
one normalized internal value. Audit every existing V001-V009 manifest FK and
record its actual intended action explicitly; do not assign a default that can
hide drift.

Add negative tests that create otherwise-valid schemas with:

1. one identity column using a non-`C` collation;
2. the Block FK recreated as `ON DELETE NO ACTION`;
3. one Memory graph FK made non-deferrable.

Each must fail validation-only startup with a bounded schema incompatibility
message.

### 7.5 V010 implementation

Append exactly one checksummed migration after V009. It creates all six tables
in the approved shape. Use named constraints/indexes so the manifest can verify
them. Include TenantId in every primary/unique/foreign key and Memory graph
edge. Do not add GIN/vector/full-text/retention indexes.

Update the expected table list and manifest entries in the same patch. Never
make Apply aware of Agent Memory runtime DI; schema V010 belongs to the provider
catalog once released.

### 7.6 JSON roots

Add exact `[JsonSerializable]` roots for:

```text
AgentConversationRecord
AgentTaskRecord
AgentCompressedContext
AgentCompressedContextBlock
AgentMemoryCandidate
AgentMemoryItem
```

Add tests that enumerate the intended roots and serialize/deserialize each via
its generated `JsonTypeInfo`. Static architecture tests reject bare generic
`JsonSerializer.Serialize/Deserialize`, reflection resolvers, and dynamic JSON
mapping in every `PostgreSqlAgent*Store` file.

### 7.7 Coordinator mode

Add enum value 5 and refactor the coordinator as frozen in §4.3. Unit/integration
tests prove:

- ordinary `ExecuteAsync` still joins an ambient transaction;
- `ExecuteTopLevelAsync` rejects ambient before invoking its delegate;
- top-level no-ambient calls commit before returning;
- existing unavailability and commit-unknown behavior remains unchanged.

Do not yet route curation Store methods through the new mode; that occurs when
the methods are implemented in Slices 7/8.

### 7.8 DI Red/Green

Add composition tests:

```text
PostgreSqlProvider_WithoutAgentMemoryRuntime_Should_ValidateAndBuild
PostgreSqlAgentMemoryPersistence_Should_ReplaceStores_InEitherOrder
```

Use `ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }`.
Also start a Generic Host for the formal-curation validator path and expect it
to fail closed while the PostgreSQL capability is `Unknown`.

Prove:

- base provider alone resolves all existing participants and no Agent Memory
  Store contract;
- runtime then Memory-provider extension selects all four PostgreSQL Stores;
- Memory-provider extension then runtime produces the same selection;
- repeated Memory-provider extension calls do not create duplicate Stores;
- selected `IAgentMemoryStore` casts to conditional/capabilities but reports
  `Unknown` during Slices 3-7;
- a read-only Host using the selected durable Store can Build/Validate without
  formal curation admission;
- a Host with `AddAgentMemoryCuration()` fails the existing validator because
  the selected Store does not yet report `ConfirmedAtomic`;
- no independent DI descriptor exists for
  `IAgentMemoryConditionalCurationStore` or
  `IAgentMemoryStoreCapabilities`;
- feature extension without base provider fails ValidateOnBuild/resolution;
- base provider does not register sanitization, projector/state machine,
  Retriever, Expander, promotion service, or Accountability.

### 7.9 Slice gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryMigrationTests|FullyQualifiedName~PostgreSqlAgentMemoryCompositionTests|FullyQualifiedName~PostgreSqlProviderContractTests"
rtk dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~DurableAgentMemory|FullyQualifiedName~Persistence"
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~Slice3EvidenceActivationTests"
rtk git diff --check
```

Close B10-MIG@3, B15-PGD@3, C03-BND@3, C09-MIG@3, C10-JSON@3,
C14-PGD@3, and C15-PGD@3. Exercise the DI shape portion of C01 as a
non-closing Slice regression; its PGD@8 evidence is not activated until Slice 8
proves the same selected
Store truthfully reports `ConfirmedAtomic`. C02 and the PostgreSQL
`CurationCapabilities_Should_Be_ConfirmedAtomic` wrapper are not activated
until Slice 8. Do not claim any Store behavior or formal-curation composition
Green yet.

**Suggested commit:**

```text
feat: add durable agent memory schema and provider composition
```

---

## 8. Slice 4 — Durable Conversation and Task Stores

**Purpose:** Deliver sanitized, snapshot-safe, tenant-scoped Conversation/Task
durability and close the concurrent append lost-update defect.

### 8.1 Files

Implement/modify:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  PostgreSqlAgentMemoryStoreSupport.cs
  PostgreSqlAgentMemoryRowMapper.cs
  PostgreSqlAgentConversationStore.cs
  PostgreSqlAgentTaskHistoryStore.cs
  PostgreSqlRuntimeTestHooks.cs (parameter observation only if needed)

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlAgentMemoryContractTests.cs
  PostgreSqlAgentMemoryRestartTests.cs
  PostgreSqlAgentMemoryConcurrencyTests.cs
  PostgreSqlAgentMemoryFailureTaxonomyTests.cs
  Fixtures/PostgreSqlAgentMemoryContractDriver.cs
```

### 8.2 Red — Conversation

Run the shared Conversation cases plus PostgreSQL restart cases:

```text
Conversation_Should_Preserve_TenantIsolation
Conversation_Should_Return_Snapshot
Conversation_Should_Persist_Only_Sanitized_Turns
Conversation_Should_Preserve_TurnSequence
```

Add PostgreSQL-specific assertions that:

- first Save inserts revision 1;
- replacement increments revision exactly once and returns only the replacement;
- caller mutation before/after reads cannot alter persisted JSON;
- rejected raw sentinel content is absent from `state_json::text` and the
  internal captured JSON parameter;
- an unavailable database returns `RuntimePersistenceUnavailableException`;
- cancellation before parameter creation/first command writes no row.

### 8.3 Green — Conversation write/read

`SaveConversationAsync` performs all sanitization and snapshot construction
before entering the transaction and before creating a JSON parameter:

```text
copy Turns in submitted order
sanitize each Content
omit rejected Turn + append safe diagnostic
replace accepted Content and diagnostics
deep snapshot
serialize with generated JsonTypeInfo
ExecuteAsync
INSERT ... ON CONFLICT ... DO UPDATE
```

The upsert changes `state_json`, increments revision, and preserves original
`created_at`. TenantId and ConversationId are always SQL predicates/PK values.
`GetConversationAsync` selects the structured identity/version/revision/JSON,
validates through the row mapper, and returns a new snapshot.

Do not log rejected/raw content, Npgsql parameters, SQL text with values, or
full persisted JSON.

### 8.4 Red — Task replacement and append

Run:

```text
Task_Should_Preserve_TenantIsolation
Task_Should_Return_Snapshot
Task_Should_Persist_Only_Sanitized_Content
Task_Should_Preserve_Deterministic_Order
Concurrent_TaskAppend_Should_Not_Lose_Event
TaskAppend_MissingTask_Should_Return_ResourceUnavailable
ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal
```

PostgreSQL-only additions prove:

- rejected Summary becomes null with safe diagnostic;
- rejected Event is neither persisted nor counted;
- append sanitizes exactly once and never accepts a raw fast path;
- two concurrently committed appends are both present exactly once;
- committed append order is stable after rebuilding the provider;
- exact Event input order is not replaced by EventId/CreatedAt order;
- missing Task produces the intentional typed cutover with zero row creation;
- `C` SQL order followed by final detached `StringComparer.Ordinal` matches the
  InMemory result for non-BMP IDs.

### 8.5 Green — Task serialization

Use one deterministic Task identity advisory lock for every Save/Append so a
missing-row create cannot race an append. For an existing row, acquire the
advisory lock then `SELECT ... FOR UPDATE`; no code path reverses that order.

`SaveTaskAsync` prepares/sanitizes the complete replacement outside the
transaction, then atomically inserts/replaces it. `AppendEventAsync`:

```text
observe cancellation
sanitize/copy Event before JSON materialization
ExecuteAsync
acquire Task identity lock
SELECT Task FOR UPDATE
missing -> ResourceUnavailable
rejected Event -> no UPDATE, successful no-op
accepted Event -> append to detached sequence
serialize complete Task
UPDATE JSON + revision + updated_at
```

Event sanitization and detached copying always finish before `ExecuteAsync` is
entered. Inside the transaction the Store only acquires the Task lock, proves
existence, then performs rejected no-op or accepted append. A rejected Event
still enters the transaction and checks Task existence, so missing Task remains
`ResourceUnavailable`; rejection cannot bypass that contract. No sanitizer call
is permitted while the Runtime transaction/session is active.

`ListTasksAsync` selects Tenant rows with explicit `ORDER BY task_id COLLATE
"C"`, validates/materializes every row, then performs final
`OrderBy(TaskId, StringComparer.Ordinal)` and snapshots the returned array.

### 8.6 Failure and sanitizer parameter evidence

If an internal test parameter observer is needed for F12, it must observe only
the already-created safe parameter values and be one-shot/resettable like the
existing command-lease hook. The test provides a raw unique sentinel, causes
sanitization/rejection, captures every value sent by these Stores, then proves
the sentinel is absent from captures and persisted rows. Never add a production
parameter logger.

### 8.7 Slice gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryContractTests&FullyQualifiedName~Conversation|FullyQualifiedName~PostgreSqlAgentMemoryContractTests&FullyQualifiedName~Task|FullyQualifiedName~PostgreSqlAgentMemoryConcurrencyTests&FullyQualifiedName~Task|FullyQualifiedName~PostgreSqlAgentMemoryFailureTaxonomyTests"

rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests \
  --filter "FullyQualifiedName~InMemoryAgentMemoryStoreContractTests"
rtk dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~Slice4EvidenceActivationTests"
rtk git diff --check
```

If the compound filter syntax does not select the expected tests, use separate
commands and record `--list-tests` evidence; do not accept a zero-test pass.

Close PGS@4/PGR@4 for H01/H02, PGC@4/PGR@4 for B07/B08, PGS@4
for B17, and PGF@4 for F12. Conversation/Task sub-assertions for aggregate
AllStores cases remain part of their later aggregate evidence tuple; F09/F14
remain open until PGF@9.

**Suggested commit:**

```text
feat: persist agent conversations and task history in postgresql
```

---

## 9. Slice 5 — Durable Context and Tenant-Wide Block Projection

**Purpose:** Persist Context aggregate JSON and direct Block projection as one
atomic tenant-scoped unit with correct parent-first FK order and corruption
checks.

### 9.1 Files

Implement/modify:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  PostgreSqlAgentMemoryStoreSupport.cs
  PostgreSqlAgentMemoryRowMapper.cs
  PostgreSqlAgentMemoryLockManager.cs
  PostgreSqlAgentCompressedContextStore.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlAgentMemoryContractTests.cs
  PostgreSqlAgentMemoryRestartTests.cs
  PostgreSqlAgentMemoryConcurrencyTests.cs
  PostgreSqlAgentMemoryFailureTaxonomyTests.cs
```

### 9.2 Red — aggregate semantics

Run shared and PostgreSQL cases:

```text
CompressedContext_Should_Return_Snapshot
CompressedContext_Should_Reject_CrossTenant_Block
BlockIdentity_Should_Be_TenantWide_Unique
ReplacingContext_Should_Remove_Old_BlockProjection
ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey
TamperedBlockContextOrOrdinal_Should_FailPersistedInvariantValidation
```

Also prove:

- duplicate BlockId within one submitted Context is `IdentityConflict`;
- same BlockId in different Tenants succeeds;
- same BlockId in different Contexts of one Tenant conflicts;
- Context create with occupied ContextId writes nothing;
- replacement can reuse its own existing BlockIds;
- failed replacement leaves old parent JSON/revision and all old Block rows;
- direct old Block lookup returns null after successful replacement;
- submitted Block sequence/ordinal survives provider rebuild;
- provider never invokes `IAgentMemoryContentSanitizer` for Context/Block;
- cancellation before first write leaves both parent and children absent/old.

### 9.3 Lock/validation algorithm

For Create/Save:

1. snapshot input and reject duplicate Block IDs/cross-tenant Blocks before SQL;
2. enter ordinary `ExecuteAsync`;
3. acquire Context identity advisory lock;
4. lock/load existing Context and its Block IDs when present;
5. acquire the ordinal-sorted union of old/new Block identity advisory locks;
6. query tenant-wide availability for all new IDs;
7. treat IDs belonging to the same replacing Context as reusable; any other
   owner is `IdentityConflict`;
8. for Create, existing Context is `IdentityConflict`;
9. serialize parent and all Blocks with exact generated type info before the
   first mutation;
10. execute the parent INSERT/UPSERT first;
11. delete old Block projections for that Context;
12. insert new Block rows with submitted zero-based ordinal;
13. commit all or roll back all.

Because the Block FK is immediate, parent-first is mandatory even for the first
create. Do not make the FK deferred to accommodate a child-first algorithm.

### 9.4 Read validation

`GetCompressedContextAsync`:

```text
load parent row
load child rows ordered by ordinal
validate version/revision/identity
require child count == parent.Blocks.Count
for each ordinal:
    ordinal is exact contiguous index
    tenant/context/block identities match
    block_json semantic snapshot equals parent.Blocks[ordinal]
return detached parent snapshot
```

`GetCompressedContextBlockAsync`:

```text
load block projection by tenant + block_id
load referenced parent Context
validate parent row
require ordinal in range
require parent.Blocks[ordinal] equals block row semantically
return detached block snapshot
```

Missing row returns null. Missing parent, bad ordinal, duplicate/gapped
ordinal, context mismatch, or semantic mismatch is
`PersistedInvariantViolation`, never null or silent repair.

### 9.5 Deterministic tamper/failure tests

Use direct SQL only in the PostgreSQL test project to tamper one field at a
time, then call the public Store:

```text
block context_id points to another valid parent
block ordinal points outside parent range
block_json differs from parent slot
parent state_json differs from child set
state_contract_version is unknown
revision is zero/invalid where constraints permit controlled setup
```

The setup must restore/drop its isolated schema through the existing lease; it
must not weaken production constraints globally.

Use an internal failure hook after parent upsert and after old-child delete to
force a throw. A fresh connection must observe the complete old version.

### 9.6 Slice gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryContractTests&FullyQualifiedName~Context|FullyQualifiedName~PostgreSqlAgentMemoryRestartTests&FullyQualifiedName~Context|FullyQualifiedName~PostgreSqlAgentMemoryFailureTaxonomyTests&FullyQualifiedName~Block"

rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests \
  --filter "FullyQualifiedName~CompressedContext|FullyQualifiedName~BlockIdentity"
rtk dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~Slice5EvidenceActivationTests"
rtk git diff --check
```

Close PGS@5/PGR@5 for H03, PGS@5 for B03-B06, and PGF@5 for F13/F16.
Aggregate B01/B02/B12 and F14 remain open until their §17 owning Slices.

**Suggested commit:**

```text
feat: persist compressed context and block projection atomically
```

---

## 10. Slice 6 — Candidate/Memory Base Store and Query Parity

**Purpose:** Implement non-curation Candidate/Memory operations, exact replay,
structured read validation, and Store-level filtering without opening a
lifecycle bypass.

### 10.1 Files

Implement/modify:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  PostgreSqlAgentMemoryStoreSupport.cs
  PostgreSqlAgentMemoryRowMapper.cs
  PostgreSqlAgentMemoryLockManager.cs
  PostgreSqlAgentMemoryStore.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlAgentMemoryContractTests.cs
  PostgreSqlAgentMemoryRestartTests.cs
  PostgreSqlAgentMemoryConcurrencyTests.cs
  PostgreSqlAgentMemoryFailureTaxonomyTests.cs
```

### 10.2 Red — Candidate base operations

Add shared/provider cases for:

- `Candidate_Should_Return_Snapshot`;
- Save/Create Candidate inserts a detached snapshot;
- existing Tenant+CandidateId is `IdentityConflict`;
- same CandidateId in another Tenant is independent;
- batch duplicate input or one existing identity writes none;
- concurrent overlapping batches acquire locks in one ordinal order and do not
  partially insert/deadlock;
- Transition missing is `ResourceUnavailable`;
- expected-status mismatch is `InvalidLifecycleState` with zero mutation;
- valid transition updates Status/JSON/state hash/revision together;
- formal curation never calls `TransitionCandidateStatusAsync`;
- Context/Candidate/Memory content is never re-sanitized.

### 10.3 Green — Candidate writes

Before the first INSERT, snapshot all batch inputs and compute their state
hashes through `IAgentMemoryStateHashProjector`. Enter `ExecuteAsync`, acquire
every Candidate identity advisory lock in ordinal order, precheck occupancy,
then insert all rows. Use one transaction for the full batch.

`TransitionCandidateStatusAsync` locks the row, validates expected Status,
projects a detached `with` snapshot, recomputes the hash through the interface,
and updates JSON/status/hash/revision together. It remains compatibility-only;
do not route Promote/Reject through it.

### 10.4 Red — strict Memory create/replay

Run:

```text
Memory_Should_Return_Snapshot
SaveMemory_Should_Be_CreateOrExactReplay
SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected
ListMemories_Should_Be_Ordinally_Deterministic
ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal
Memory_Query_Should_Match_InMemory_Contract
```

Parameterize invalid new snapshots:

```text
Candidate
Rejected
Superseded
Archived
IsAuthoritative = true
SupersedesMemoryId != null
SupersededByMemoryId != null
both links non-null
```

Each must return `InvalidLifecycleState`, create no row, and publish no #56
curation fact. Existing identity plus a one-field difference must be
`StateConflict`; exact replay must not change revision, JSON, or timestamps.

### 10.5 Green — Memory create/replay

Validate the initial shape before SQL. Snapshot and compute state hash through
the shared interface. Inside `ExecuteAsync`:

1. acquire Memory identity advisory lock;
2. `SELECT ... FOR UPDATE` by Tenant+MemoryId;
3. absent → INSERT approved Active/non-authoritative/unlinked snapshot;
4. present → validate persisted row and exact comparer equality;
5. equal → return without UPDATE;
6. unequal → `StateConflict`.

Do not allow exact hash equality to replace the full comparer.

### 10.6 Memory read/graph validation

`GetMemoryAsync` validates all structured fields and recomputed state hash. If
either graph link is present, load the tenant-scoped endpoint and prove the
reciprocal link. An absent/wrong-tenant/non-reciprocal endpoint is persisted
corruption.

For `ListMemoriesAsync`, apply only the frozen Store filters:

```text
TenantId exact in SQL
Kinds/MemoryIds in SQL when non-empty
Status in SQL: Active always; Superseded/Archived only by flags
Candidate/Rejected never
Tags/DescriptorRefs after detached materialization with exact .NET semantics
IncludeStale ignored
```

Load graph endpoints in bounded batch queries for returned linked rows; avoid an
unbounded per-row connection/command loop. Explicit SQL `C` order is followed
by final detached `StringComparer.Ordinal` sort. Snapshot after filtering and
before return.

### 10.7 Persisted corruption cases

Direct SQL tampering must prove failure for:

```text
JSON TenantId/ID mismatch
unknown state_contract_version
non-positive revision setup where possible
undefined/mismatched status or kind
canonical_content_hash mismatch
state_hash mismatch
confidence/promoted_at mismatch
graph column/JSON mismatch
non-reciprocal graph endpoint
```

All map to `RuntimePersistenceContractException(PersistedInvariantViolation)`.
They must not become StateConflict or infrastructure unavailable.

### 10.8 Slice gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryContractTests&FullyQualifiedName~Candidate|FullyQualifiedName~PostgreSqlAgentMemoryContractTests&FullyQualifiedName~Memory|FullyQualifiedName~PostgreSqlAgentMemoryFailureTaxonomyTests"

rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests \
  --filter "FullyQualifiedName~InMemoryAgentMemoryStoreContractTests|FullyQualifiedName~AgentMemoryPersistenceComparerTests"
rtk dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~Slice6EvidenceActivationTests"
rtk git diff --check
```

Close PGS@6 for H09, B01/B02/B09/B10/B12/B14/B16/B18. B11 remains
REP@10, B13 remains PGC@9, and F09/F11/F14 remain PGF@9.

**Suggested commit:**

```text
feat: persist agent memory candidates and immutable memories
```

---

## 11. Slice 7 — Atomic Promote and Reject with a Top-Level Commit Boundary

**Purpose:** Implement the first formal curation paths using locked snapshots,
the shared state machine, and a provider-owned COMMIT that completes before #56
can publish a committed fact.

This Slice proves Promote/Reject mechanics but keeps
`CurationOutcomeGuarantee.Unknown`. The formal-curation Host validator must
still fail closed because Supersede/Archive are not complete. Focused service
tests compose the real service/Store directly without treating Host startup as
approved production composition.

### 11.1 Files

Modify:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  PostgreSqlAgentMemoryStore.cs
  PostgreSqlAgentMemoryLockManager.cs
  PostgreSqlRuntimeTransactionCoordinator.cs
  PostgreSqlRuntimeTestHooks.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlAgentMemoryCurationContractTests.cs
  PostgreSqlAgentMemoryConcurrencyTests.cs
  PostgreSqlAgentMemoryFailureTaxonomyTests.cs
  PostgreSqlAgentMemoryCompositionTests.cs
  PostgreSqlAgentMemoryAccountabilityCompositionTests.cs
```

### 11.2 Red — Promote

Run/add:

```text
Promote_Should_Be_Atomic
Promote_With_StaleCandidateHash_Should_Conflict
ConcurrentPromote_Should_Have_ExactlyOneWinner
PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection
FormalCuration_WithPreexistingAmbientTransaction_Should_FailBeforeMutation
CommittedAccountability_Should_Never_Precede_DurableCommit
```

Also prove every deterministic failure leaves Candidate/Memory unchanged:

```text
missing Candidate              -> ResourceUnavailable
occupied new MemoryId          -> IdentityConflict
Tenant mismatch                -> TenantMismatch
Candidate not Candidate status -> InvalidLifecycleState
stale Candidate hash           -> StateConflict
content hash mismatch          -> StateConflict
expected new Memory hash drift -> StateConflict
```

### 11.3 Green — Promote algorithm

`PromoteAsync` calls `ExecuteTopLevelAsync` and performs, in order:

1. acquire the new Memory identity advisory lock;
2. lock Candidate row `FOR UPDATE`;
3. establish Candidate presence and new Memory absence in the Tenant;
4. map/validate the persisted Candidate row;
5. call `IAgentMemoryCurationStateMachine.PreparePromote` with locked snapshot
   and existing Plan;
6. compute/persist returned Memory and Candidate state hashes through the
   shared hash interface;
7. INSERT Memory;
8. UPDATE Candidate JSON/status/hash/revision with an expected revision/status
   predicate as a defensive invariant;
9. commit inside `ExecuteTopLevelAsync`;
10. return only after COMMIT acknowledgement.

If the defensive update affects zero rows after the row lock, throw
`PersistedInvariantViolation` unless a locked domain expectation already
established a typed conflict. Do not manufacture `StateConflict` from an
unexpected row-count anomaly.

### 11.4 Shared-projection parity proof

Instrument the test at semantic boundaries, not by duplicating projection
logic. Given one Candidate, operation identity, and new Memory ID:

- the Promotion Service prepares its expected Memory hash using
  `IAgentMemoryCurationProjector`;
- the state machine returns the committed Memory snapshot using the same
  interface;
- snapshots are value-identical before provider metadata;
- persisted JSON deserializes to that snapshot;
- state hash matches both preparation and committed row.

An architecture test rejects private methods named/behaving as
`CreatePromotedMemory` in Promotion Service, InMemory Store, and PostgreSQL
Store. Prefer semantic symbol/constructor checks over a brittle whole-repo text
ban that could match historical documentation.

### 11.5 Red/Green — Reject

Run `Reject_Should_Be_Conditional` in both shared runners.
`RejectAsync` uses `ExecuteTopLevelAsync`, locks Candidate, maps the snapshot,
calls `PrepareReject`, then updates Status/JSON/hash/revision in one statement.

Cases:

```text
valid Candidate -> Rejected exactly once
missing -> ResourceUnavailable
stale hash -> StateConflict
wrong lifecycle -> InvalidLifecycleState
cancellation before first write -> no mutation
concurrent Reject/Promote -> exactly one valid winner
```

Formal Reject must not call the compatibility
`TransitionCandidateStatusAsync` method.

### 11.6 Ambient boundary proof

Build an ambient transaction by invoking the public
`IRuntimeTransactionCoordinator.ExecuteAsync`, then call each implemented formal
curation method inside its delegate. Assert:

- `AmbientCommitBoundaryUnsupported` error code 5;
- delegate for top-level curation performs no advisory lock/SQL command;
- Candidate and Memory rows remain unchanged after outer commit and outer
  rollback variants;
- no known-success or typed-rejection #56 fact is written.

The test must call the real Store/service, not only the coordinator helper.

### 11.7 Commit-before-Accountability proof

Use the internal one-shot `before top-level COMMIT` gate:

1. start `DefaultAgentMemoryPromotionService.PromoteAsync` against the durable
   Store and real #56 bridge/durable `IAuditSink`;
2. wait until Store writes are complete but COMMIT is blocked;
3. from a fresh connection, assert no Memory mutation and no committed Memory
   Accountability row are visible;
4. assert the service Task has not completed and producer has not been called;
5. release COMMIT;
6. await success;
7. assert mutation is durable before/with the subsequent Accountability fact.

Add a rollback/failure variant: throw before COMMIT and prove neither mutation
nor committed fact exists. Commit-unknown remains unknown and produces no false
deterministic failure fact, preserving #56 behavior.

### 11.8 Slice gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryCurationContractTests&FullyQualifiedName~Promote|FullyQualifiedName~PostgreSqlAgentMemoryCurationContractTests&FullyQualifiedName~Reject|FullyQualifiedName~FormalCuration_WithPreexistingAmbientTransaction|FullyQualifiedName~CommittedAccountability_Should_Never_Precede_DurableCommit"

rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests \
  --filter "FullyQualifiedName~Promotion|FullyQualifiedName~Curation"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests
rtk dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~Slice7EvidenceActivationTests"
rtk git diff --check
```

Close PGS@7 for H04/F01/F02/F04/C16, PGF@7 for F15, and ACC@7 for C13.
F03/F09/F10/F14 and C07/C08 retain their later concurrency/failure/full-
composition evidence. C01/C02 remain incomplete, and no PostgreSQL
`ConfirmedAtomic` evidence is activated in this Slice.

**Suggested commit:**

```text
feat: commit agent memory promote and reject atomically
```

---

## 12. Slice 8 — Atomic Supersede and Archive Graph Curation

**Purpose:** Complete the formal lifecycle with one reciprocal three-node
Supersede transaction and link-preserving Archive.

### 12.1 Files

Modify:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  PostgreSqlAgentMemoryStore.cs
  PostgreSqlAgentMemoryLockManager.cs
  PostgreSqlAgentMemoryRowMapper.cs
  PostgreSqlRuntimeTestHooks.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlAgentMemoryCurationContractTests.cs
  PostgreSqlAgentMemoryConcurrencyTests.cs
  PostgreSqlAgentMemoryFailureTaxonomyTests.cs
  PostgreSqlAgentMemoryRestartTests.cs
```

### 12.2 Red — Supersede

Run/add:

```text
Supersede_Should_Commit_ThreePartGraph_Atomically
Supersede_Failure_Should_Expose_No_PartialGraph
ConcurrentSupersede_Should_Have_ExactlyOneWinner
```

Prove the committed result is exactly:

```text
old Memory:
    Status = Superseded
    SupersededByMemoryId = new ID
    prior SupersedesMemoryId retained

new Memory:
    Status = Active
    IsAuthoritative = false
    SupersedesMemoryId = old ID
    SupersededByMemoryId = null

replacement Candidate:
    Status = Active
```

All payload/provenance fields and `PromotedAt` come from the shared projector.

Failure matrix with zero mutation:

```text
missing target Memory
missing replacement Candidate
occupied new Memory ID
Tenant mismatch
target not Active
replacement not Candidate
stale target hash
stale Candidate hash
content hash mismatch
expected new-state hash mismatch
self-link/duplicate replacement claim
```

### 12.3 Green — Supersede algorithm

Inside `ExecuteTopLevelAsync`:

1. acquire new Memory identity advisory lock;
2. acquire target Memory row lock(s) in ordinal MemoryId order;
3. acquire replacement Candidate row lock(s) in ordinal CandidateId order;
4. validate absence/presence and map all persisted snapshots;
5. call `PrepareSupersede` once;
6. compute hashes for old/new Memory and Candidate snapshots;
7. update old Memory JSON/status/links/hash/revision;
8. insert new Memory JSON/structured projections/hash;
9. update Candidate JSON/status/hash/revision;
10. commit once and return detached new Memory.

The self-FKs are deferred only to permit reciprocal graph writes in this one
transaction. They do not relax application-level reciprocal validation.

Add a one-shot failure hook after each of the three SQL mutations. Every forced
failure must roll back all three.

### 12.4 Red/Green — Archive

Run/add:

```text
Archive_Should_Be_Conditional
ConcurrentArchive_Should_Have_ExactlyOneWinner
Archive_Should_RetainExistingGraphLinks_AfterRestart
```

Inside `ExecuteTopLevelAsync`, lock the Memory row, validate expectation through
the shared state machine, accept only Active/Superseded, and update
Status/JSON/hash/revision. Existing links must be copied unchanged. Missing,
stale, already Archived, Candidate, or Rejected inputs produce the frozen typed
outcomes with zero mutation.

### 12.5 Graph read closure

After Supersede/Archive, exercise both direct Get and filtered List on fresh
provider instances. The row mapper must validate reciprocal graph endpoints
even when one endpoint is Archived. Archive is not an edge deletion operation.

Tamper tests:

```text
old points to new, new does not point back
new points to wrong Tenant/ID
JSON link differs from structured link
two rows claim same supersedes target (where constraint is bypassed in isolated setup)
```

Each public read fails persisted invariant validation.

### 12.6 Concurrency races

Coordinate real parallel connections with barriers/hooks, not `Task.Delay` as
the correctness mechanism:

```text
Supersede vs Supersede same target -> one success, one locked-state conflict
Supersede vs Archive same target   -> one valid winner
Archive vs Archive                 -> one success, one InvalidLifecycleState/StateConflict
```

The loser result depends on the locked current snapshot and expectation; tests
assert an allowed exact typed outcome and always assert one valid final graph,
never only “an exception occurred.”

### 12.7 Truthful capability activation

Only after Promote, Reject, Supersede, and Archive plus their atomic rollback
tests are Green, change
`PostgreSqlAgentMemoryStore.CurationOutcomeGuarantee` from `Unknown` to
`ConfirmedAtomic`. In the same Red→Green step activate:

```text
CurationCapabilities_Should_Be_ConfirmedAtomic
SelectedMemoryStore_Should_ImplementConditionalAndCapabilitiesWithoutSeparateDescriptors
CurationCompositionValidator_Should_PassAndReportConfirmedAtomic
```

Start the real Generic Host and prove the validator that failed closed in Slice
3 now passes. Re-prove there is one selected `IAgentMemoryStore`, no separate
conditional/capability descriptors, and both registration orders select that
truthful Store. This capability flip and the fourth primitive belong in the
same reviewable Slice; never commit `ConfirmedAtomic` first.

### 12.8 Slice gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryCurationContractTests&FullyQualifiedName~Supersede|FullyQualifiedName~PostgreSqlAgentMemoryCurationContractTests&FullyQualifiedName~Archive|FullyQualifiedName~PostgreSqlAgentMemoryConcurrencyTests"

rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests \
  --filter "FullyQualifiedName~Supersede|FullyQualifiedName~Archive|FullyQualifiedName~Curation"
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~CurationCapabilities_Should_Be_ConfirmedAtomic|FullyQualifiedName~SelectedMemoryStore_Should_ImplementConditionalAndCapabilitiesWithoutSeparateDescriptors|FullyQualifiedName~CurationCompositionValidator_Should_PassAndReportConfirmedAtomic"
rtk dotnet build src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~Slice8EvidenceActivationTests"
rtk git diff --check
```

Close PGS@8 for H05/H06, PGR@8 for H06, and PGD@8 for C01/C02. The
PGF/PGC obligations for F05/F06/F09/F10/F14 remain open until Slice 9; F15,
C13, and C16 were already closed by their earlier evidence tuples.

**Suggested commit:**

```text
feat: persist reciprocal agent memory curation graph
```

---

## 13. Slice 9 — Concurrency, Failure Taxonomy, and Real Crash Evidence

**Purpose:** Prove the durability claims under real contention, injected SQL
failures, backend loss, process death, and ambiguous COMMIT without weakening
the frozen exception taxonomy.

### 13.1 Files

Modify/create:

```text
src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/
  PostgreSqlRuntimeTestHooks.cs
  PostgreSqlAgentMemoryStoreSupport.cs
  PostgreSqlAgentMemoryStore.cs
  PostgreSqlAgentTaskHistoryStore.cs
  PostgreSqlAgentCompressedContextStore.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlAgentMemoryConcurrencyTests.cs
  PostgreSqlAgentMemoryCrashTests.cs
  PostgreSqlAgentMemoryFailureTaxonomyTests.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/
  AgentMemoryCrashScenarios.cs
  Program.cs
  CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker.csproj
```

Add concrete Memory runtime/Accountability references to CrashWorker only as
needed to execute the real service path.

Every test class in this Slice that installs a static hook, blocks a write or
COMMIT, or coordinates CrashWorker/backend exit is annotated with:

```csharp
[Collection(PostgreSqlRuntimeCollection.Name)]
```

The same rule applies retroactively to hook-using classes introduced in Slices
4-8. Add an architecture/reflection guard that enumerates methods using the
hook API and requires their declaring xUnit class to carry this collection,
unless Slice 9 replaced the hook implementation with the keyed isolation model
from §4.7.

### 13.2 Complete concurrency matrix

Use separate service scopes/connections and deterministic barriers:

```text
two Task appends to one row
SaveTask replacement racing append
overlapping Candidate batches
Context replacements sharing one Block ID
two Promotes for one Candidate
Promote vs Reject
two Supersedes for one target
Supersede vs Archive
two Archives
```

For every race assert:

1. the exact allowed success/failure counts;
2. no deadlock/provider-unavailable misclassification;
3. the complete final durable aggregate/graph;
4. revision increments match committed mutations;
5. fresh-provider reads return the same state/order.

### 13.3 SQL failpoint matrix

Trigger one injected exception after every individual write point for:

```text
Context parent upsert
Context old Block delete
each Context new Block insertion boundary
Promote Memory insert
Promote Candidate update
Supersede old Memory update
Supersede new Memory insert
Supersede Candidate update
Archive Memory update
Task append update
```

Use bounded categories, not one test per loop iteration if a parameterized test
can name the write point. After each failure, rebuild/fresh-connect and assert
old complete state or complete absence. Never inspect only the throwing scope's
tracked/in-memory snapshots.

### 13.4 Provider failure taxonomy

Prove separately:

```text
open/command failure before known COMMIT
    -> RuntimePersistenceUnavailableException

COMMIT submitted, acknowledgement indeterminate
    -> RuntimeTransactionCommitUnknownException
    -> no automatic retry
    -> no deterministic failed Accountability fact

locked domain mismatch
    -> AgentMemoryOperationException with exact code

invalid persisted row/graph
    -> RuntimePersistenceContractException(PersistedInvariantViolation)

pre-existing ambient formal curation
    -> RuntimePersistenceContractException(AmbientCommitBoundaryUnsupported)
```

The tests must reject raw `NpgsqlException`, SQLSTATE leakage, and messages
containing content/connection strings/SQL.

### 13.5 Crash worker protocol

Extend CrashWorker argument dispatch with explicit Agent Memory scenarios:

```text
agent-memory-before-promote-commit
agent-memory-after-promote-commit
agent-memory-before-supersede-commit
agent-memory-after-supersede-commit
```

For before-COMMIT scenarios:

1. worker composes real runtime + durable Store;
2. prepares fixture rows;
3. installs one-shot before-COMMIT block;
4. starts the real formal service operation;
5. prints a sentinel only after SQL mutations and before COMMIT;
6. waits indefinitely;
7. parent kills the complete process tree;
8. test waits until the worker PostgreSQL backend exits;
9. a fresh provider proves zero mutation/partial graph and no committed fact.

For after-COMMIT scenarios:

1. worker awaits the real Store/service result;
2. prints a committed sentinel containing only bounded fixture identity;
3. waits indefinitely;
4. parent kills process and waits for backend exit;
5. fresh provider proves complete mutation/graph survives.

Do not simulate process crash by disposing a ServiceProvider or cancelling the
method. Do not rely on an arbitrary sleep; wait for sentinels and backend exit.

### 13.6 Cancellation

For each multi-write Store family, cancel before the first durable mutation and
assert zero mutation. For cancellation after writes but before COMMIT, assert
transaction rollback. Once COMMIT is submitted, preserve the coordinator's
uncancelled acknowledgement attempt and never report a false rollback.

### 13.7 Slice gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryConcurrencyTests|FullyQualifiedName~PostgreSqlAgentMemoryFailureTaxonomyTests"

rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryCrashTests"

rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests
rtk dotnet build tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~Slice9EvidenceActivationTests"
rtk git diff --check
```

If Docker/PostgreSQL is unavailable, report the Slice blocked; do not substitute
mocked crash evidence and do not advance to Slice 10 as if Green.

Close every remaining PGC@9, PGF@9, and CW@9 tuple in §17: B13,
F03/F05-F11/F14, F07/F08, and the PGF portion of C08. Earlier evidence such as
B07/B08, F12/F13/F15/F16, and C13 is rerun as regression but not double-counted.

**Suggested commit:**

```text
test: prove durable agent memory concurrency and crash boundaries
```

---

## 14. Slice 10 — Restart Recall, Source Expansion, and #56 Composition

**Purpose:** Prove that swapping the Store changes only durability. Existing
Retriever, Source Expander/ReadCore, formal curation, and Accountability retain
their exact observable semantics after a fresh provider/process.

### 14.1 Files

Create/modify tests only unless a defect is found in the durable Store mapping:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  PostgreSqlAgentMemoryRecallExpansionTests.cs
  PostgreSqlAgentMemoryRestartTests.cs
  PostgreSqlAgentMemoryCompositionTests.cs
  PostgreSqlAgentMemoryAccountabilityCompositionTests.cs

tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests/
  existing regression files only if a provider-neutral expectation needs an
  explicit durable runner adapter
```

Production changes to Retriever, Source Expander, ReadCore, Tool handlers, MCP,
or Accountability are presumptively out of scope. If a test exposes a real
provider-neutral defect, stop and document why the approved boundary cannot be
met before editing those projects.

### 14.2 Real composition setup

Compose one Host with:

```text
AddRuntimePersistence (existing provider-neutral Runtime state, if required)
AddAgentMemoryRuntime
AddCrestCreatesPostgreSqlRuntimePersistence(options)
AddCrestCreatesPostgreSqlAgentMemoryPersistence
AddAccountability
AddAgentMemoryAccountability
ReadCore/Tool/MCP registrations required by the selected scenario
```

Exercise both Agent Memory/Memory-provider registration orders in composition
tests. Start the Host so bootstrap validators execute; resolving services from
an unstarted container is insufficient evidence for C02/C15.

### 14.3 Recall parity

Build identical InMemory and PostgreSQL fixture sets containing:

- multiple kinds/confidence/promoted timestamps;
- active/superseded/archived statuses;
- Tags and DescriptorRefs with visible/hidden combinations;
- SourceRefs and sanitization diagnostics;
- IDs including non-BMP Unicode;
- enough content to trigger `MaxCount` and CharacterBudget truncation.

For the same `AgentMemoryQuery` and visibility closure, compare:

```text
returned Memory sequence and complete snapshots
diagnostics
returned count / truncation
ScopeFingerprint
VisibleMemorySetHash
CanonicalPackHash / EffectivePackHash as owned by existing layers
```

Then dispose/rebuild the PostgreSQL provider and repeat. `DefaultAgentMemoryRetriever`
must remain unchanged and own confidence/kind/PromotedAt/final tie-break order.
The provider returns only its Store subset in final MemoryId ordinal order.

### 14.4 Source Expansion parity

Persist Conversation Turns, Task Events, Context Blocks, Candidate, and Memory
with ordered source material. Through the existing Source Expander/ReadCore:

- expand Conversation and Task ranges before/after restart;
- expand direct Context Block, Candidate, and Memory references;
- prove identical pre-ReadCore material/sequence;
- prove final Grant/visibility and sanitization remain owned above the Store;
- prove a cross-Tenant lookup is null/unavailable without existence leakage;
- prove `IncludeSourceRefs=false` changes output only, not persisted provenance.

Do not add provider-specific SourceRef resolution.

### 14.5 Accountability composition

Run the real #56 service paths against the durable Store:

```text
known Promote/Reject/Supersede/Archive commit -> existing committed fact
typed known domain rejection                 -> existing rejection fact
provider unavailable                         -> no false typed rejection fact
commit acknowledgement unknown               -> no known commit/failure fact
ambient boundary unsupported                 -> no committed fact
```

Assert Store constructors/source do not mention Memory Accountability producer,
Recorder, or Sink. Existing `PostgreSqlAuditSink` in the same project is not a
violation by itself; the guard targets Agent Memory Store dependency/source
ownership.

### 14.6 Architecture and unchanged-mainline guards

Add/complete guards proving:

- PostgreSQL project references only Agent Memory Abstractions;
- Store source has no recall confidence/budget/visibility/pack-hash logic;
- no Stale/TTL heuristic was introduced;
- no JSON reflection fallback exists;
- both Stores consume shared state machine/projector/comparer interfaces;
- Promotion Service consumes the shared projector;
- formal service still casts selected `IAgentMemoryStore` to conditional and
  capabilities rather than resolving independent DI surfaces.

### 14.7 Slice gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests \
  --filter "FullyQualifiedName~PostgreSqlAgentMemoryRecallExpansionTests|FullyQualifiedName~PostgreSqlAgentMemoryRestartTests|FullyQualifiedName~PostgreSqlAgentMemoryAccountabilityCompositionTests|FullyQualifiedName~PostgreSqlAgentMemoryCompositionTests"

rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.ReadCore.Tests
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests
rtk dotnet test tests/Integrations/CrestCreates.Mcp.Memory.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests \
  --filter "FullyQualifiedName~Slice10EvidenceActivationTests"
rtk git diff --check
```

If an exact project name differs, locate it with `rtk rg --files tests` and
record the actual command; never silently omit the regression family.

Close REP@10/PGR@10 for H07/H08, REP@10 for B11/C05, BND@10 for C04,
REP@10/PGR@10 for C06, and ACC@10 for C07/C08.

**Suggested commit:**

```text
test: close durable agent memory runtime composition parity
```

---

## 15. Slice 11 — NativeAOT and Repository Closure

**Purpose:** Execute the complete durable Agent Memory mainline in the original
linked linux-x64 binary, then run the canonical repository gates and update
evidence without overclaiming excluded capabilities.

### 15.1 Files

Modify:

```text
tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/
  CrestCreates.Runtime.Persistence.PostgreSql.AotHost.csproj
  Program.cs

tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/
  CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests.csproj
  PostgreSqlRuntimeAotFixtureTests.cs

tests/Boundary/CrestCreates.DependencyBoundaries.Tests/
  DurableAgentMemoryPersistenceArchitectureTests.cs

memory.md
docs/superpowers/plans/2026-08-13-phase-9bplus-durable-agent-memory-store-provider.md
```

Add concrete Agent Memory/ReadCore/Accountability references only to the AOT
Host as required. Do not add them to the production provider.

### 15.2 AOT Host scenario

Extend the existing Host without removing prior Phase 9b or Agent Tool
sentinels. The original binary must:

1. apply and validate V010;
2. compose `AddAgentMemoryRuntime`, base PostgreSQL persistence, explicit Agent
   Memory persistence, and #56 validation/bridge;
3. start the Host so composition validators run;
4. save a Conversation containing accepted and rejected raw content and verify
   only safe content is returned;
5. save a Task, append an Event, and verify sequence;
6. save Context with Blocks and verify direct Block projection;
7. create a Candidate and Promote through the formal service;
8. create a replacement Candidate and Supersede, or Archive the promoted
   Memory after proving the approved graph transition;
9. dispose the first provider completely;
10. build/start a fresh provider using the same schema;
11. read Conversation/Task/Context/Block/Candidate/Memory;
12. execute real Recall and Source Expansion;
13. validate the resulting graph, deterministic Pack/hashes, and formal
    curation capabilities;
14. verify #56 durable Accountability fact presence through the existing sink;
15. print exactly once:

```text
CRESTCREATES_DURABLE_AGENT_MEMORY_OK
```

The scenario uses stable fixture timestamps/IDs where determinism is required.
Do not use runtime reflection, dynamic JSON, or a managed-only shortcut.

### 15.3 AOT fixture assertions

The existing fixture continues to:

- publish `-c Release -r linux-x64 --self-contained true
  -p:CrestCreatesPublishMode=aot`;
- fail on IL2026/IL3050 warnings;
- mark/run the produced ELF executable;
- start/use real PostgreSQL;
- assert every pre-existing sentinel;
- assert the new exact Agent Memory sentinel;
- fail if the binary exits nonzero or the sentinel is missing.

Add a static check that the AOT Host references generated
`PostgreSqlRuntimeJsonSerializerContext` roots indirectly through the real
Stores, not a fixture-only serialization path.

### 15.4 Full verification sequence

Run in this order and record exact pass/fail counts:

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests -c Release
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests -c Release
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests -c Release
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests -c Release
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests -c Release \
  --filter "FullyQualifiedName~Slice11EvidenceActivationTests"
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests -c Release \
  --filter "FullyQualifiedName~AllDurableAgentMemoryEvidenceTests"

rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests \
  -c Release \
  --filter "FullyQualifiedName~Publish_native_aot_postgresql_runtime_links_and_executes_real_database_operations"

rtk dotnet build CrestCreates.slnx -c Release
rtk dotnet build solutions/CrestCreates.Runtime.slnx -c Release
rtk dotnet build solutions/CrestCreates.All.slnx -c Release
rtk dotnet test CrestCreates.slnx -c Release --no-build
rtk git diff --check
```

If repository-wide tests fail for unrelated environment-dependent suites,
record the exact command, completed projects, failures, and why they are
unrelated. Do not convert that into a false Green claim. The focused #55,
boundary, PostgreSQL, and original-binary AOT gates are mandatory and cannot be
waived as environmental.

### 15.5 Evidence/document update

Only after the original binary succeeds:

- update `memory.md` with implemented status, exact test counts, canonical
  build result, native publish/link/run result, and sentinel;
- state explicitly that InMemory is semantic-only and PostgreSQL is durable;
- state explicitly that mutation/Accountability are post-result, not atomic;
- do not claim Outbox/reliable delivery, automatic replay, exactly-once business
  effects, vector retrieval, retention, or a second provider;
- change this Plan status from APPROVED/ready to IMPLEMENTED only with the
  actual reviewed implementation head and evidence.

### 15.6 Slice gate

All **44** exact Spec §18 skeleton names must resolve to discovered tests: 31
shared method names, six PostgreSQL group/class names, and seven PostgreSQL
method names. Independently, every RequiredEvidence tuple under H01-H09,
B01-B18, F01-F16, and C01-C16 must resolve to its exact discovered test. The
architecture guards fail if a skeleton name, Case, evidence kind, or evidence
test name is removed, but remain activation/discovery completeness guards; they
do not claim that another assembly's tests passed.

The owning test-project commands in §15.4 prove execution and Green. The Slice
11 handoff records each exact command with discovered, executed, passed,
failed, and skipped counts. Completion requires 44/44 skeleton discovery,
98/98 evidence activation, 98/98 evidence execution/Green, no skipped #55
evidence, and no zero-test filter invocation.

**Suggested commit:**

```text
test: verify durable agent memory provider under native aot
```

---

## 16. V010 Object and Validation Ledger

This section fixes implementation names so different Agents do not invent
incompatible migration/manifest objects. If PostgreSQL truncation limits make a
name invalid, shorten it once in both migration and manifest and record the
mapping in the Slice 3 handoff.

### 16.1 Tables and primary keys

| Table | Primary key name | Columns |
|---|---|---|
| `agent_memory_conversations` | `pk_agent_memory_conversations` | `tenant_id, conversation_id` |
| `agent_memory_tasks` | `pk_agent_memory_tasks` | `tenant_id, task_id` |
| `agent_memory_compressed_contexts` | `pk_agent_memory_compressed_contexts` | `tenant_id, context_id` |
| `agent_memory_compressed_blocks` | `pk_agent_memory_compressed_blocks` | `tenant_id, block_id` |
| `agent_memory_candidates` | `pk_agent_memory_candidates` | `tenant_id, candidate_id` |
| `agent_memories` | `pk_agent_memories` | `tenant_id, memory_id` |

Every table has named checks for `revision > 0` where revision exists and
`state_contract_version = 1`. Blocks have
`ck_agent_memory_compressed_blocks_ordinal_nonnegative`.

### 16.2 Enum/range/graph checks

Use named checks:

```text
ck_agent_memory_candidates_status        status between 0 and 4
ck_agent_memory_candidates_kind          kind between 0 and 5
ck_agent_memories_status                 status between 0 and 4
ck_agent_memories_kind                   kind between 0 and 5
ck_agent_memories_confidence             confidence between 0 and 3
ck_agent_memories_no_self_supersedes     supersedes_memory_id is null or <> memory_id
ck_agent_memories_no_self_superseded_by  superseded_by_memory_id is null or <> memory_id
```

These checks mirror current enum numeric values but do not authorize
`SaveMemoryAsync` to insert Candidate/Rejected/Superseded/Archived states. The
Store's stricter create contract remains mandatory.

### 16.3 Foreign keys and indexes

```text
fk_agent_memory_blocks_context
    (tenant_id, context_id)
    -> agent_memory_compressed_contexts (tenant_id, context_id)
    ON DELETE CASCADE
    NOT DEFERRABLE

fk_agent_memories_supersedes
    (tenant_id, supersedes_memory_id)
    -> agent_memories (tenant_id, memory_id)
    ON DELETE NO ACTION
    DEFERRABLE INITIALLY DEFERRED

fk_agent_memories_superseded_by
    (tenant_id, superseded_by_memory_id)
    -> agent_memories (tenant_id, memory_id)
    ON DELETE NO ACTION
    DEFERRABLE INITIALLY DEFERRED

uq_agent_memory_blocks_context_ordinal
    UNIQUE (tenant_id, context_id, ordinal)

uq_agent_memories_supersedes
    UNIQUE (tenant_id, supersedes_memory_id)
    WHERE supersedes_memory_id IS NOT NULL
```

Add non-unique query indexes only for approved Store predicates:

```text
ix_agent_memory_tasks_tenant_task
    (tenant_id, task_id)

ix_agent_memory_candidates_tenant_status
    (tenant_id, status, candidate_id)

ix_agent_memories_tenant_status_kind
    (tenant_id, status, kind, memory_id)
```

The PK may make the Task index redundant; if PostgreSQL proves it redundant,
omit it from both migration and manifest. Do not add an index merely to satisfy
this illustrative name. The Candidate/Memory indexes must be justified by
actual approved lookup/filter paths and verified as non-unique in the manifest.

### 16.4 Collation ledger

Every text column explicitly shown as `collate "C"` in Spec §9.2 must carry
that collation in migration and manifest. At minimum:

```text
all tenant_id columns
conversation_id / task_id / context_id / block_id / candidate_id / memory_id
canonical_content_hash / state_hash
supersedes_memory_id / superseded_by_memory_id
```

JSON/content fields do not acquire a collation contract. SQL uses `C` for
deterministic DB identity/index behavior; public list order is still finalized
with `StringComparer.Ordinal`.

---

## 17. Complete Acceptance-to-Test Mapping

The implementation manifest must use these IDs and normative names. A concrete
runner may place the method in a more specific class, but it must not rename the
method without updating this approved Plan and ledger guard.

The tables below show required evidence, not interchangeable owners:

```text
IMS  = InMemorySemantic
PGS  = PostgreSqlSemantic
PGC  = PostgreSqlConcurrency
PGR  = PostgreSqlRestart
PGF  = PostgreSqlFailureInjection
CW   = CrashWorker
PGD  = PostgreSqlComposition
ACC  = AccountabilityComposition
REP  = RecallExpansionParity
MIG  = Migration
JSON = JsonArchitecture
BND  = Boundary
AOT  = NativeAot
BLD  = CanonicalBuild
```

Each abbreviation becomes a separate manifest `RequiredEvidence` item with its
own owning Slice and exact fully-qualified concrete test name. Multiple items
may use the same method basename in different runner classes; the manifest uses
the full name and therefore distinguishes them.

The frozen matrix contains **59 Case IDs and 98 RequiredEvidence tuples**:

```text
IMS 28, PGS 24, PGC 5, PGR 8, PGF 10, CW 2, PGD 5,
ACC 3, REP 5, MIG 2, JSON 1, BND 3, AOT 1, BLD 1
```

The Slice 1 architecture test asserts both totals and each per-kind count. Any
change requires an explicit Plan review; an implementation Agent may not reduce
the matrix to make closure pass.

### 17.1 Happy-path cases

| ID | Normative test name | Required evidence |
|---|---|---|
| H01 | `Conversation_SaveAndRestart_Should_PreserveSanitizedSnapshotAndTurnSequence` | IMS@2 + PGS@4 + PGR@4 |
| H02 | `Task_SaveAppendAndRestart_Should_PreserveSanitizedSnapshotAndEventSequence` | IMS@2 + PGS@4 + PGR@4 |
| H03 | `ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey_AndRestartLookup` | IMS@2 + PGS@5 + PGR@5 |
| H04 | `Promote_Should_CommitCandidateAndMemoryAtomically` | IMS@2 + PGS@7 |
| H05 | `Supersede_Should_CommitReciprocalThreeNodeGraphAtomically` | IMS@2 + PGS@8 |
| H06 | `Archive_Should_RetainGraphLinks_AfterRestart` | IMS@2 + PGS@8 + PGR@8 |
| H07 | `Recall_Should_ReturnSameOrderPackAndHashes_AfterRestart` | REP@10 + PGR@10 |
| H08 | `SourceExpansion_Should_ReturnSameDomainMaterial_AfterRestart` | REP@10 + PGR@10 |
| H09 | `SaveMemory_ExactReplay_Should_NotMutateRevisionOrState` | IMS@2 + PGS@6 |

### 17.2 Boundary cases

| ID | Normative test name | Required evidence |
|---|---|---|
| B01 | `AllStores_Should_IsolateSameIdentityAcrossTenants` | IMS@2 + PGS@6 |
| B02 | `AllCrossTenantLookups_Should_ReturnNullOrEmptyWithoutLeakage` | IMS@2 + PGS@6 |
| B03 | `BlockIdentity_Should_BeIndependentAcrossTenants` | IMS@2 + PGS@5 |
| B04 | `BlockIdentity_Should_BeTenantWideUniqueAcrossContexts` | IMS@2 + PGS@5 |
| B05 | `ReplacingContext_Should_RemoveOldBlockProjectionAtomically` | IMS@2 + PGS@5 |
| B06 | `OrderedArtifacts_Should_PreserveSubmittedSequence_NotTimestampOrIdOrder` | IMS@2 + PGS@5 |
| B07 | `Concurrent_TaskAppend_Should_Not_Lose_Event` | IMS@2 + PGC@4 |
| B08 | `ConcurrentTaskAppend_CommittedOrder_Should_SurviveRestart` | PGC@4 + PGR@4 |
| B09 | `ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal` | IMS@2 + PGS@6 |
| B10 | `IncludeStale_Should_RemainNoOp_WithoutStaleSchema` | IMS@2 + PGS@6 + MIG@3 |
| B11 | `Memory_Query_Should_Match_InMemory_Contract` | REP@10 |
| B12 | `AllStores_Should_ReturnDetachedSnapshots` | IMS@2 + PGS@6 |
| B13 | `CandidateBatch_WithOneConflict_Should_WriteNone` | IMS@2 + PGC@9 |
| B14 | `SaveMemory_ExistingOneFieldDifference_Should_ReturnStateConflict` | IMS@2 + PGS@6 |
| B15 | `PostgreSqlAgentMemoryPersistence_Should_ReplaceStores_InEitherOrder` | PGD@3 |
| B16 | `SaveMemory_Should_Not_CreateOneSidedSupersedeGraph` | IMS@2 + PGS@6 |
| B17 | `TaskAppend_MissingTask_Should_Return_ResourceUnavailable` | IMS@2 + PGS@4 |
| B18 | `SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected` | IMS@2 + PGS@6 |

### 17.3 Failure/concurrency cases

| ID | Normative test name | Required evidence |
|---|---|---|
| F01 | `Promote_OccupiedMemoryIdentity_Should_LeaveCandidateUnchanged` | IMS@2 + PGS@7 |
| F02 | `Promote_StaleCandidateHash_Should_ConflictWithoutMutation` | IMS@2 + PGS@7 |
| F03 | `ConcurrentPromote_Should_HaveExactlyOneWinner` | PGC@9 |
| F04 | `Reject_StaleExpectation_Should_HaveZeroMutation` | IMS@2 + PGS@7 |
| F05 | `Supersede_FailureAfterEachWritePoint_Should_ExposeNoPartialGraph` | PGF@9 |
| F06 | `ConcurrentSupersedeOrArchive_Should_HaveOneValidWinner` | PGC@9 |
| F07 | `CrashBeforeCurationCommit_Should_ExposeNoMutationAfterBackendExit` | CW@9 |
| F08 | `CrashAfterCurationCommit_Should_RemainVisibleToFreshProcess` | CW@9 |
| F09 | `DatabaseUnavailable_Should_RemainRuntimePersistenceUnavailable` | PGF@9 |
| F10 | `CommitAcknowledgementLoss_Should_RemainCommitUnknown` | PGF@9 |
| F11 | `MalformedPersistedState_Should_FailPersistedInvariantValidation` | PGF@9 |
| F12 | `RejectedRawContent_Should_BeAbsentFromDatabaseParametersAndRows` | PGF@4 |
| F13 | `ContextBlockConflict_Should_RestoreOldAggregateAndProjection` | PGF@5 |
| F14 | `CancellationBeforeFirstWrite_Should_ProduceZeroMutation` | IMS@2 + PGF@9 |
| F15 | `FormalCuration_WithPreexistingAmbientTransaction_Should_FailBeforeMutation` | PGF@7 |
| F16 | `TamperedBlockContextOrOrdinal_Should_FailPersistedInvariantValidation` | PGF@5 |

### 17.4 Composition/evidence cases

| ID | Normative test name | Required evidence |
|---|---|---|
| C01 | `SelectedMemoryStore_Should_ImplementConditionalAndCapabilitiesWithoutSeparateDescriptors` | PGD@8 |
| C02 | `CurationCompositionValidator_Should_PassAndReportConfirmedAtomic` | IMS@2 + PGD@8 |
| C03 | `PostgreSqlProvider_Should_ReferenceOnlyAgentMemoryAbstractions` | BND@3 |
| C04 | `PostgreSqlAgentMemoryStores_Should_HaveNoAccountabilityDependency` | BND@10 |
| C05 | `Retriever_Should_HaveInMemoryPostgreSqlParity` | REP@10 |
| C06 | `SourceExpanderAndReadCore_Should_RemainUnchangedAfterRestart` | REP@10 + PGR@10 |
| C07 | `KnownCommitAndTypedConflictFacts_Should_RemainCorrectWithDurableStore` | ACC@10 |
| C08 | `UnavailableOrCommitUnknown_Should_CreateNoFalseDeterministicCurationFact` | ACC@10 + PGF@9 |
| C09 | `V010Manifest_Should_ValidateApplyChecksumShapeCollationAndForeignKeyDeleteAction` | MIG@3 |
| C10 | `PostgreSqlAgentMemoryJsonPaths_Should_UseExactGeneratedRootsOnly` | JSON@3 |
| C11 | `DurableAgentMemoryDependencyBoundariesAndCanonicalSolutions_Should_Build` | BND@11 + BLD@11 |
| C12 | `PublishNativeAotPostgreSqlRuntime_Should_ExecuteDurableAgentMemoryMainline` | AOT@11 |
| C13 | `CommittedAccountability_Should_Never_Precede_DurableCommit` | ACC@7 |
| C14 | `PostgreSqlProvider_WithoutAgentMemoryRuntime_Should_ValidateAndBuild` | PGD@3 |
| C15 | `ExplicitAgentMemoryProviderRegistration_Should_ReplaceFourStores_InEitherOrder` | PGD@3 |
| C16 | `PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection` | IMS@2 + PGS@7 |

### 17.5 Case ownership audit

The manifest is immutable evidence requirements, not a progress checklist. At
the end of every Slice, the Slice-aware discovery guard requires all tuples
whose `OwningSlice <= current Slice` to have an activated exact test, and the
Slice gate executes those tests. Do not edit Spec case IDs or add a “Green”
field. Slice 11 requires:

```text
Spec §18 required skeleton names (44) subset of discovered tests
Spec Case IDs == manifest Case IDs
manifest RequiredEvidence tuples == activated/discovered evidence tuples
owning test-project runs prove every activated evidence tuple executed and passed
```

Parameterized tests may cover multiple IDs, but every required `(CaseId,
EvidenceKind)` still has an exact fully-qualified manifest test name and a
passing execution. A broad class-level pass or one provider's Green result does
not replace another required evidence kind. Neither
`SliceNEvidenceActivationTests` nor `AllDurableAgentMemoryEvidenceTests` may be
used as execution evidence for a test owned by another assembly.

---

## 18. Mandatory Review Checklist

Every Slice review answers the applicable questions with file/test evidence.
The final review answers all of them.

### 18.1 Dependency and composition

1. Does the PostgreSQL production project reference only Agent Memory
   Abstractions, with no concrete Memory/ReadCore/Tools/Accountability runtime?
2. Does the base PostgreSQL extension remain valid without Agent Memory runtime?
3. Is Agent Memory persistence enabled only through the explicit feature
   extension?
4. Do both feature registration orders select all four PostgreSQL Stores?
5. Is the selected `IAgentMemoryStore` the only Memory Store DI truth, with
   conditional/capability interfaces discovered by cast?
6. Does `AddAgentMemoryReadRuntime` own the shared projector/state
   machine/comparer implementations without admitting formal curation?
7. Does Host startup execute and pass the #56 formal-curation validator?

### 18.2 Shared semantics

8. Do Promotion Service, InMemory Store, and PostgreSQL Store use one shared
   curation projector/state machine rather than private projection copies?
9. Does every Candidate/Memory state hash come from
   `IAgentMemoryStateHashProjector`?
10. Does exact Memory replay compare every persisted field and collection order?
11. Can a missing Memory identity be created with anything other than Active,
    non-authoritative, and both links null?
12. Can an existing Memory be rewritten when it is not an exact replay?
13. Does missing Task append return the intentional `ResourceUnavailable` in
    both providers?

### 18.3 Transactions and concurrency

14. Do ordinary writes use ambient-joining `ExecuteAsync` while every formal
    curation path uses ambient-rejecting `ExecuteTopLevelAsync`?
15. Is ambient presence checked before the curation delegate, advisory lock, or
    SQL command executes?
16. Can #56 observe a successful Store return before COMMIT acknowledgement?
17. Are multi-identity advisory locks de-duplicated and ordinally ordered?
18. Is curation row-lock order always new identity → Memory → Candidate?
19. Can two committed Task appends lose one Event?
20. Can overlapping Context/Candidate/graph mutations deadlock because a path
    reverses the frozen order?
21. Does each forced failure/crash leave either the complete old or complete new
    durable state, never a partial graph/projection?

### 18.4 Schema, SQL, and read validation

22. Are V001-V009 byte/checksum semantics unchanged and V010 appended once?
23. Does the manifest validate exact column collation and FK delete action in
    addition to existing shape/deferrability checks?
24. Does Context parent INSERT/UPSERT happen before Block INSERT?
25. Does direct Block lookup validate its parent Context and exact ordinal slot?
26. Does every primary key, unique/index predicate, FK, lookup, lock, update,
    delete, and graph edge include TenantId where required?
27. Are SQL values parameterized, with no raw content in logs/errors?
28. Does every read fail closed on version/identity/enum/hash/graph drift?
29. Does SQL use explicit `C` order while final detached results use
    `StringComparer.Ordinal`, including non-BMP tests?

### 18.5 Runtime boundaries and evidence

30. Is Conversation/Task sanitization complete before JSON parameter creation?
31. Are Context/Candidate/Memory persisted unchanged without re-sanitization?
32. Did SQL avoid recall confidence/visibility/budget/pack-hash semantics?
33. Does any Agent Memory Store reference or call Accountability?
34. Are provider unavailable, commit unknown, domain conflict, ambient contract
    violation, and persisted corruption still distinguishable?
35. Do crash tests kill a real process and wait for the PostgreSQL backend exit?
36. Does every persisted JSON path use exact generated metadata only?
37. Did the newly published original linux-x64 binary execute the durable
    Memory mainline and print the exact sentinel?
38. Are all 44 exact Spec §18 skeleton names discoverable, and are all 59 Case
    IDs plus every required evidence tuple independently discoverable and
    passing with no skipped/zero-test gate?
39. Does `memory.md` report only evidence actually executed?
40. Does the Store exclusively own resource existence/identity availability,
    with the state machine limited to Tenant/lifecycle/state/projection checks?
41. Did PostgreSQL remain `Unknown` and formal-curation startup fail closed
    until all four conditional primitives became Green in Slice 8?
42. Can any InMemory evidence incorrectly satisfy a required PostgreSQL,
    concurrency, restart, crash, boundary, or AOT tuple?
43. Are all process-global PostgreSQL hooks and CrashWorker gates isolated by
    `PostgreSqlRuntimeCollection` or an executable keyed/test-scoped design?

Any “no” blocks Slice advancement or final merge.

---

## 19. Verification Command Matrix

### 19.1 Fast local loop

Use the owning test method/class while Red/Green cycling:

```bash
rtk dotnet test <owning-test-project> \
  --filter "FullyQualifiedName~<ExactNormativeTestName>"
rtk dotnet build <changed-production-project>
rtk git diff --check
```

Always run `--list-tests` once after introducing a new filtered class/name and
verify the filter selects at least one test.

### 19.2 Shared semantic regression

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tests -c Release
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Accountability.Tests -c Release
```

### 19.3 PostgreSQL regression

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests -c Release
```

This project requires real PostgreSQL/Testcontainers. A Docker failure is an
environment blocker, not a passing semantic result.

### 19.4 Architecture and composition regression

```bash
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests -c Release
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Memory.Tools.Tests -c Release
rtk dotnet test tests/Integrations/CrestCreates.Mcp.Memory.Tests -c Release
```

Use `rtk rg --files tests | rg 'Memory.*Tests.*csproj'` to confirm exact
integration project names at implementation time.

### 19.5 NativeAOT gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests \
  -c Release \
  --filter "FullyQualifiedName~Publish_native_aot_postgresql_runtime_links_and_executes_real_database_operations"
```

The pass is valid only if test output proves a fresh publish/link occurred and
the original executable emitted `CRESTCREATES_DURABLE_AGENT_MEMORY_OK`.

### 19.6 Canonical closure

```bash
rtk dotnet build CrestCreates.slnx -c Release
rtk dotnet build solutions/CrestCreates.Runtime.slnx -c Release
rtk dotnet build solutions/CrestCreates.All.slnx -c Release
rtk dotnet test CrestCreates.slnx -c Release --no-build
rtk git diff --check
rtk git status --short --branch
```

Record elapsed time and exact counts in the final handoff so a later Agent can
distinguish actual execution from intended gates.

---

## 20. Definition of Done and Final Handoff

Issue #55 implementation is complete only when all of the following are true:

- the shared semantic surfaces are implemented once and consumed by Promotion
  Service, InMemory, and PostgreSQL as specified;
- both InMemory and PostgreSQL pass the runner-free Store/curation contracts;
- V010 applies, validates, re-applies idempotently, and detects shape,
  collation, FK delete-action, predicate, and checksum drift;
- the base PostgreSQL Host validates with no Agent Memory runtime;
- explicit Agent Memory provider registration selects all four durable Stores
  in both feature orders without extra conditional/capability descriptors;
- Conversation/Task sanitization happens before JSON/parameter materialization
  and raw rejected content is absent from parameters/rows;
- Context/Block parent-first replacement and parent-validated direct lookup are
  durable and atomic;
- Candidate batch, transition, Memory create/replay, filters, non-BMP order,
  snapshot safety, and persisted-invariant checks are Green;
- Promote/Reject/Supersede/Archive are locked, conditionally projected, and
  committed through the provider-owned top-level boundary;
- PostgreSQL remained `Unknown`/fail-closed until all four primitives were
  complete, then and only then reported `ConfirmedAtomic` and passed Host
  validation;
- #56 cannot publish committed Accountability before durable COMMIT and does
  not manufacture facts for unavailable/unknown/ambient-contract outcomes;
- real concurrency and process-crash tests prove no lost committed append,
  partial projection, or partial graph;
- all process-global test hooks and backend gates are collection-serialized or
  demonstrably keyed/test-scoped;
- existing Recall, Source Expansion/ReadCore, Tools/MCP, and Accountability
  behavior is unchanged under the Store swap and restart;
- all 44 exact Spec §18 test skeleton names are discovered independently of the
  evidence ledger;
- every required evidence tuple for the exact
  H01-H09/B01-B18/F01-F16/C01-C16 ledger is discovered, executed, and passing;
- dependency boundaries, the root solution, Runtime sub-solution, and canonical
  full solution build;
- the freshly published original linux-x64 executable runs the real PostgreSQL
  durable mainline and emits the exact sentinel;
- `memory.md` contains only the actual reviewed evidence and preserves all
  out-of-scope disclaimers.

The final implementing Agent hands off:

```text
implementation branch and reviewed head SHA
Slice commit list (1-11)
spec-test-skeleton result: 44/44 exact names (31 shared methods, 6 PostgreSQL groups, 7 PostgreSQL methods)
case-manifest result: 59/59 Case IDs
evidence-manifest result: 98/98 required tuples, grouped by the frozen kind counts
focused test projects and each exact command's discovered/executed/passed/failed/skipped counts
PostgreSQL/Testcontainers result
crash scenario result and backend-exit evidence
NativeAOT publish command, binary path/type, sentinel output
canonical build/test result
dependency-boundary result
git diff --check result
remaining unrelated repository/environment failures, if any
explicit confirmation that no excluded capability was added
```

Do not merge on a design assertion alone. Merge only the implementation for
which this evidence was actually executed and reviewed.
