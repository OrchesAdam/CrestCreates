# Phase 9b+ — Durable Agent Memory Store Provider Design Spec

**Issue:** [#55 — Phase 9b+ Durable Agent Memory Store Provider](https://github.com/OrchesAdam/CrestCreates/issues/55)

**Depends on:** [#43 — Agent Memory & Context Compression Runtime](https://github.com/OrchesAdam/CrestCreates/issues/43), [#24 — Phase 9b Durable Persistence Foundation](https://github.com/OrchesAdam/CrestCreates/issues/24), [#56 — Phase 9b+ Agent Memory Accountability Integration](https://github.com/OrchesAdam/CrestCreates/issues/56)

**Related but excluded:** #25 Transactional Outbox / reliable delivery, #70/#73 Agent Tool pre-dispatch reconciliation, retention/GC, vector retrieval

**Design input:** Issue comment `5277240292`, reconciled against `master` at `4028ea94`

**Status:** APPROVED — implemented by PR #77; post-implementation closure review pending

**Review revision:** Propagates both 2026-08-13 review rounds through all
normative sections and executable design cases

**Design mode:** Case-first TDD; Red → Green → Review

**Date:** 2026-08-13

---

## 1. Decision Summary

Phase 9b+ adds one PostgreSQL-backed durable implementation for each existing
Agent Memory Store contract:

```text
IAgentConversationStore
IAgentTaskHistoryStore
IAgentCompressedContextStore
IAgentMemoryStore
    + IAgentMemoryStoreCapabilities
    + IAgentMemoryConditionalCurationStore
```

The provider extends the existing `CrestCreates.Runtime.Persistence.PostgreSql`
kernel. It does not create another data source, transaction coordinator,
migration runner, schema-history protocol, or provider capability model.

The durable mainline is:

```text
Agent Memory domain/runtime
    owns sanitization, lifecycle preparation, canonical/state hashes,
    recall, visibility, source expansion, and Accountability facts
        ↓ existing Store contracts + minimal provider-neutral semantics
PostgreSQL Agent Memory Stores
    own tenant-scoped snapshots, durable indexes, locking, atomic writes,
    restart persistence, and provider failure taxonomy
        ↓
existing PostgreSQL Runtime transaction + migration kernel
```

The design freezes ten decisions:

1. PostgreSQL is the only durable database provider in #55.
2. Conversation Turns and Task Summary/Events are sanitized before any JSON
   serialization or Npgsql parameter is created. Compressed Context, Candidate,
   and Memory snapshots are persisted unchanged because their content/hash pair
   is already canonical upstream.
3. A complete source-generated JSON aggregate snapshot is stored beside only
   the structured columns required for durable identity, filtering, locking,
   lifecycle CAS, graph integrity, and deterministic order.
4. All identity, uniqueness, lookup, graph, and ordering semantics are scoped
   by exact `TenantId`. PostgreSQL `C` collation provides deterministic database
   identity/index behavior, but observable Store order is finalized after
   deserialization with `StringComparer.Ordinal`.
5. Formal Promote, Reject, Supersede, and Archive use one database transaction
   each. Their expectation checks and all graph writes are one atomic primitive,
   so the store truthfully reports `ConfirmedAtomic`.
6. Formal curation owns a top-level commit boundary. It never joins a
   pre-existing ambient Runtime transaction and fails before mutation when one
   exists, so #56 cannot publish `committed` before the durable commit.
7. The PostgreSQL provider does not duplicate Agent Memory canonical hashing or
   lifecycle projection. One shared curation projector is consumed by the
   Promotion Service and Store state machine; the latter is consumed by both
   InMemory and PostgreSQL Stores.
8. Recall, visibility, budget, pack hashes, Source Expansion, and semantic
   Accountability remain above the provider. Switching stores must not change
   their observable results.
9. Deterministic domain conflicts, infrastructure unavailability, and unknown
   commit acknowledgement remain three different outcomes. Unknown commit
   outcome is never rewritten into a deterministic Memory failure.
10. Agent Memory persistence is an explicit opt-in registration surface inside
    the existing PostgreSQL provider project. The base Runtime Persistence
    extension remains valid for Hosts that do not enable Agent Memory.

Durability does not make Memory authoritative. A durable Memory remains
non-authoritative context and cannot replace Descriptor, Registry, review,
activation, authorization, approval, or Accountability evidence.

---

## 2. Repository Facts That Constrain the Design

### 2.1 The Store contracts are already the provider boundary

`CrestCreates.Agent.Memory.Abstractions` already owns four Store interfaces.
The implementation must preserve those signatures. #55 does not introduce an
ORM-shaped repository API, an Npgsql-facing abstraction, or a second Memory
Store facade.

The final #56 composition validator also requires a formal curation store to be:

```text
IAgentMemoryStore
    is IAgentMemoryConditionalCurationStore
    is IAgentMemoryStoreCapabilities
    CurationOutcomeGuarantee == ConfirmedAtomic
```

The PostgreSQL provider therefore registers one concrete singleton as
`IAgentMemoryStore`; that selected object itself implements the conditional and
capability interfaces, which consumers and the validator discover by casting.
No separate conditional/capability DI registrations or wrapper instances are
required.

### 2.2 Phase 9b already owns the durable provider kernel

The existing PostgreSQL provider supplies:

- `NpgsqlSlimDataSourceBuilder` and one `NpgsqlDataSource`;
- `PostgreSqlRuntimeTransactionCoordinator`;
- the ambient `PostgreSqlRuntimeSession` accessor;
- command serialization inside an ambient transaction;
- provider-neutral persistence and commit-unknown exceptions;
- checksummed migrations, schema manifest validation, and validation-only
  startup by default;
- Testcontainers, CrashWorker, and NativeAOT publish-link-run infrastructure.

#55 extends those components. A new `AgentMemoryDbContext`, independent
connection pool, migration history table, or local transaction abstraction is
rejected.

### 2.3 Persistence may reference Runtime abstractions, not Runtime implementations

The PostgreSQL project currently references Workflow, HumanTask,
Accountability, and Agent Tool abstraction assemblies only. It must add only:

```text
CrestCreates.Agent.Memory.Abstractions
```

It must not reference:

```text
CrestCreates.Agent.Memory
CrestCreates.Agent.Memory.ReadCore
CrestCreates.Agent.Memory.Accountability
CrestCreates.Agent.Memory.Tools
```

This makes the current concrete `AgentMemoryCanonicalHashProjector` unusable
directly from the provider and exposes a real design gap: a conditional Store
must validate `ExpectedStateHash`, but canonical state projection cannot be
reimplemented inside SQL persistence.

Section 6 closes that gap through a narrow provider-neutral semantic surface.

### 2.4 Current InMemory behavior defines most Store semantics, not every bug

The existing implementations establish these observable contracts:

- Conversation and Task Store own their current sanitization boundary.
- Conversation Turns, Task Events, and Context Blocks preserve list order.
- Task list and Memory list use ordinal ID order.
- Context Block identity is tenant-wide, not context-local.
- Candidate creation rejects an existing identity.
- `CreateCandidatesAsync` is an all-or-none batch.
- Memory query filtering is a Store-level subset; final recall is owned by
  `DefaultAgentMemoryRetriever`.
- formal curation is conditional and atomic in the InMemory Store.

Three implementation details are not copied as durable semantics:

1. concurrent `AppendEventAsync` currently has a read-modify-replace lost-update
   window; PostgreSQL must guarantee that two committed appends are both visible;
2. `SaveMemoryAsync` currently compares only part of the payload and can replace
   lifecycle/link fields after the comparison. #55 hardens both InMemory and
   PostgreSQL to Active-only create-or-exact-replay so this setup API cannot
   become a lifecycle bypass;
3. missing Task append currently throws a plain `InvalidOperationException`.
   #55 intentionally cuts this over in both providers to
   `AgentMemoryOperationException(ResourceUnavailable)`.

No public Store signature changes are required for these corrections.

### 2.5 #56 is complete and owns semantic Accountability

The selected Memory service performs:

```text
Store known commit / typed known rejection
    ↓
IAgentMemoryAccountabilityProducer
    ↓
IAuditRecorder
```

The Store must not inject or call `IAgentMemoryAccountabilityProducer`,
`IAuditRecorder`, or an `IAuditSink`. It returns a persistence result or throws
the correct persistence/domain exception. #56 then decides whether a semantic
fact exists.

Memory mutation and Accountability persistence are not atomic in #55. Reliable
post-result delivery remains outside this Issue.

### 2.6 An ambient transaction is not a known commit boundary

`PostgreSqlRuntimeTransactionCoordinator.ExecuteAsync` joins an existing
ambient transaction by executing the work and returning without committing.
`DefaultAgentMemoryPromotionService` publishes a #56 committed fact immediately
after the conditional Store returns. Therefore a durable formal curation Store
must not use ordinary ambient-join semantics: returning before the outer commit
would allow a committed fact followed by an outer rollback.

#55 freezes formal curation as a top-level commit operation. A pre-existing
ambient Runtime transaction is rejected before any lifecycle read-for-update or
write. The provider does not open a hidden independent connection and commit
behind the caller's ambient transaction.

### 2.7 `IncludeStale` has no lifecycle state

`AgentMemoryQuery.IncludeStale` exists, but `AgentMemoryStatus` contains only:

```text
Candidate, Active, Rejected, Superseded, Archived
```

The current Store has no Stale state and `IncludeStale` is a compatibility
no-op. The PostgreSQL provider must not invent a status, timestamp heuristic,
TTL, provider-local stale flag, or hidden SQL predicate.

### 2.8 Source Expansion depends on exact persisted sequence

Conversation Turn and Task Event SourceRefs use `RangeStart`/`RangeEnd` as list
indexes. Reordering by `CreatedAt`, `TurnId`, `EventId`, or database insertion
time changes the meaning of an existing SourceRef. Context Block lookup also
depends on tenant-wide Block ID projection.

The provider must therefore preserve the submitted sequence exactly and keep
that sequence stable across restart.

---

## 3. Scope

### 3.1 In scope

- four PostgreSQL Store implementations;
- one additive, provider-neutral Agent Memory state-hash surface;
- one additive, provider-neutral pure curation projection surface shared by
  Promotion Service preparation and Store mutation;
- one additive, provider-neutral Agent Memory curation state-machine surface;
- one shared exact persisted-snapshot comparer where provider parity requires it;
- InMemory alignment to the shared semantic surface and hardened Memory replay;
- V010 PostgreSQL migration and exact schema manifest checks;
- source-generated JSON roots for every persisted Agent Memory aggregate;
- tenant-scoped identity, Block uniqueness, Memory graph constraints, and
  deterministic Store order;
- atomic context replacement and Task append serialization;
- atomic Promote, Reject, Supersede, and Archive;
- a formal-curation top-level commit boundary plus an additive
  `AmbientCommitBoundaryUnsupported` persistence contract error;
- an explicit PostgreSQL Agent Memory persistence registration surface that
  leaves the base provider Host feature-neutral;
- DI replacement independent of registration order;
- shared Store/curation contract cases executed by InMemory and PostgreSQL;
- PostgreSQL restart, concurrency, crash, migration, and failure taxonomy cases;
- existing Recall, Source Expansion, #56 Accountability, Agent Tool/MCP, and
  formal-curation composition tests against the durable Store;
- extension of the existing PostgreSQL NativeAOT fixture with an Agent Memory
  durable mainline and an exact success sentinel.

### 3.2 Out of scope

- a second database provider;
- EF Core or a general Agent Memory ORM layer;
- lifecycle redesign or a new Stale state;
- vector, embedding, full-text, semantic, or approximate search;
- query ranking inside SQL;
- changes to recall confidence/kind/time order, visibility closure, `MaxCount`,
  character budget, ScopeFingerprint, VisibleMemorySetHash, or CanonicalPackHash;
- changes to Source Expansion Grant/final-sanitization semantics;
- changes to canonical content/state hash shapes;
- Memory-specific Accountability sink/table/envelope;
- Store-emitted semantic Accountability;
- atomic Memory mutation + Accountability write;
- Outbox, reliable delivery, or business operation replay;
- curation operation receipts or automatic retry after commit-unknown;
- retention, TTL, archive cleanup, compaction, GC, vacuum policy, or background
  maintenance;
- MCP/Agent Tool projection changes;
- LLM provider changes;
- a compliance platform or authoritative Memory governance plane.

### 3.3 Compatibility position

The four existing Store interfaces remain source-compatible. Additive semantic
interfaces are provider bridges, not alternative Store contracts.

`TransitionCandidateStatusAsync` remains a compatibility primitive, but it is
not a formal Promote/Reject implementation and no new production caller should
use it for curation. Formal curation continues exclusively through
`IAgentMemoryConditionalCurationStore`.

`SaveMemoryAsync` is frozen as create-or-exact-replay:

```text
missing identity       -> create only when Status == Active,
                          IsAuthoritative == false, and both graph links null
same complete snapshot -> success, no mutation
same identity + any different field, including Status or graph links
                       -> StateConflict, no mutation
new snapshot violating the create shape
                       -> InvalidLifecycleState, no mutation
```

Formal lifecycle transitions are the only way to create Archived/Superseded
Memory or a Supersede graph. #55 deliberately defines no privileged import
bypass; a future import capability requires its own contract and governance.

`AppendEventAsync` intentionally changes its missing-Task outcome from a plain
`InvalidOperationException` to
`AgentMemoryOperationException(ResourceUnavailable)`. InMemory and PostgreSQL
must make this cutover together and pass the same contract case.

The InMemory implementation must adopt the same rule before the shared provider
contract is considered Green.

---

## 4. System Invariants

### INV-01 — Memory remains non-authoritative

Database durability never changes `IsAuthoritative` semantics. The provider
does not infer authority from survival, age, confidence, promotion, or linkage.

### INV-02 — Tenant participates in every durable identity

Every primary key, unique index, foreign key, lookup, lock identity, update,
delete, graph edge, and deterministic query includes exact TenantId.

### INV-03 — Snapshot state never escapes

The provider snapshots before serialization, deserializes into detached
objects, and snapshots again before returning. Mutating an input after a call or
a returned result after a read cannot change durable state.

### INV-04 — Sanitization occurs before persistence materialization

Conversation Turn Content and Task Summary/Event Content are sanitized before
the provider serializes an aggregate or creates a JSON/text database parameter.
Rejected content is absent from every durable parameter and row.

### INV-05 — Canonical artifacts are not re-sanitized

Compressed Context, Candidate, and Memory snapshots are persisted exactly as
supplied. Re-sanitizing them could disconnect Content from CanonicalContentHash
and is prohibited.

### INV-06 — Provider does not own canonical or lifecycle semantics

The provider invokes the shared Agent Memory state-hash projector and curation
state machine. It does not reproduce canonical JSON writers, hash metadata,
promotion timestamp rules, lifecycle validation, or graph projection.

### INV-07 — Curation expectation and mutation are atomic

The current rows are locked, deserialized, validated against the caller's exact
expectations, projected through the shared state machine, and written inside one
provider-owned top-level PostgreSQL transaction. Formal curation never joins a
pre-existing ambient Runtime transaction; ambient presence fails closed before
the first lifecycle lock/write.

### INV-08 — Sequence is persisted, not reconstructed

Conversation Turns, Task Events, and Context Blocks retain their submitted or
committed append order. Timestamp/ID sorting and deduplication are prohibited.

### INV-09 — Store order is explicit

`C` collation provides deterministic database retrieval/index behavior only.
After deserialization and Store-level filtering, `ListTasksAsync` and
`ListMemoriesAsync` finalize observable order with
`OrderBy(Id, StringComparer.Ordinal)`. No observable result depends on database
default collation, heap order, or UTF-8 byte order.

### INV-10 — Recall stays above persistence

The Store performs only its existing tenant/kind/tag/ID/descriptor/status
filters and stable base order. It does not apply confidence, visibility,
`MaxCount`, character budget, or any pack hash.

### INV-11 — Outcome categories never collapse

`AgentMemoryOperationException`, `RuntimePersistenceUnavailableException`, and
`RuntimeTransactionCommitUnknownException` remain distinguishable through every
Store path.

### INV-12 — Store emits no semantic Accountability

The Store neither creates nor records a Memory fact. It does not persist
Operation Reason, Explanation, actor context, or Accountability payload in the
Memory schema.

### INV-13 — Structured columns and JSON must agree

On read, identity/lifecycle/hash/graph columns must match the deserialized JSON
snapshot. A mismatch is persisted corruption and fails closed; the provider
does not silently repair or prefer one representation.

### INV-14 — `ConfirmedAtomic` does not mean outcome-known

All lifecycle writes commit or roll back together. A lost commit acknowledgement
may still leave the caller uncertain whether that atomic commit occurred.
The provider reports commit-unknown and does not auto-retry or invent a receipt.

### INV-15 — Base PostgreSQL composition remains feature-neutral

`AddCrestCreatesPostgreSqlRuntimePersistence` registers the existing kernel and
durable participants but no Agent Memory Store. Only explicit
`AddCrestCreatesPostgreSqlAgentMemoryPersistence` opts a Host into the four
durable Memory Stores. `AddAgentMemoryRuntime` remains the owner of their
provider-neutral semantic prerequisites.

---

## 5. Ownership and Dependency Direction

### 5.1 Project ownership

```text
CrestCreates.Agent.Memory.Abstractions
    Store contracts
    provider-neutral state hash interface
    provider-neutral pure curation projector interface + projection snapshots
    provider-neutral curation state-machine interface + mutation snapshots
    exact persisted Memory snapshot comparer

CrestCreates.Agent.Memory
    existing canonical projector implementation
    one default curation projector implementation
    default curation state-machine implementation
    InMemory Store implementation
    sanitization, recall, expansion, promotion orchestration

CrestCreates.Runtime.Persistence.PostgreSql
    PostgreSQL Store implementations
    V010 schema + manifest
    provider JSON context roots
    SQL locking, mapping, exception translation, DI replacement

CrestCreates.Agent.Memory.Persistence.Testing (runner-free shared test kit)
    provider-neutral Store and curation contract cases

Concrete test projects
    InMemory runner
    PostgreSQL/Testcontainers/restart/crash runner
```

The exact shared-test project name may follow repository naming conventions,
but it must remain `IsTestProject=false`, contain no xUnit runner dependency,
and reference only provider-neutral abstractions required by its cases.

### 5.2 Forbidden dependency edges

```text
Agent.Memory(.ReadCore/.Tools/.Accountability) -> PostgreSql provider
PostgreSql provider -> Agent.Memory concrete runtime
PostgreSql Memory Stores -> Accountability producer/recorder/sink
PostgreSql Memory Stores -> ReadCore, Tool, MCP, LLM
shared contract kit -> InMemory/PostgreSql concrete providers
```

The existing PostgreSQL project contains a durable `IAuditSink`; this does not
authorize its Agent Memory Store classes to reference Audit semantics. Boundary
tests inspect the Store sources and constructors, not merely project-level
references.

---

## 6. Provider-neutral Memory Semantics

### 6.1 Why an additive semantic surface is required

`AgentMemoryPromotionPlan` carries an expected state hash, not the full expected
Candidate snapshot. A durable Store must hash the locked current snapshot to
compare it. Reimplementing the hash projection in SQL or the provider would
create a second canonical truth.

The provider must also construct the exact committed Candidate/Memory graph.
Copying `CreatePromotedMemory`, lifecycle checks, and state-hash checks from the
InMemory Store would create two production state machines.

### 6.2 State hash projector

Add an Abstractions-owned interface equivalent to:

```csharp
public interface IAgentMemoryStateHashProjector
{
    CanonicalHash ComputeCandidateStateHash(AgentMemoryCandidate candidate);
    CanonicalHash ComputeMemoryStateHash(AgentMemoryItem memory);
}
```

The existing `AgentMemoryCanonicalHashProjector` implements it. The runtime
registers the interface and concrete type as the same singleton.

The provider may persist the returned digest in a structured column, but the
interface remains the only authority for computing it. SQL never constructs a
state hash.

### 6.3 Shared curation projector

Add one Abstractions-owned pure projection interface equivalent in ownership to:

```text
IAgentMemoryCurationProjector
    ProjectPromotedMemory(candidate, newMemoryId, operation)
    ProjectCandidateStatus(candidate, newStatus)
    ProjectSupersededMemory(current, newMemoryId)
    ProjectSupersedingMemory(candidate, targetMemoryId, newMemoryId, operation)
    ProjectArchivedMemory(current)
```

It performs no Store I/O, locking, resource lookup, expectation comparison, or
Accountability. It is the only authority for:

- Candidate → Memory payload transfer;
- `PromotedAt = Operation.Identity.OccurredAt`;
- `IsAuthoritative = false` on a promoted Memory;
- lifecycle snapshot construction;
- reciprocal Supersedes/SupersededBy projection;
- preserving graph links during Archive.

`DefaultAgentMemoryPromotionService` must use this same projector to prepare the
expected new Memory and `ExpectedMemoryStateHash`. Its private
`CreatePromotedMemory` projection is removed. The curation state machine uses
the same projector for the locked mutation result. The private Store projection
is also removed. Tests prove that preparation and committed projection are
value-identical before persistence metadata is added.

### 6.4 Curation state machine

Add an Abstractions-owned interface whose implementation accepts locked current
snapshots plus the existing Plan/Expectation and returns detached mutation
snapshots:

```text
PreparePromote
    -> Candidate Active + new Memory Active

PrepareReject
    -> Candidate Rejected

PrepareSupersede
    -> old Memory Superseded + linked new Memory Active
       + replacement Candidate Active

PrepareArchive
    -> Memory Archived, retaining existing graph links
```

The default implementation belongs to `CrestCreates.Agent.Memory` and uses
`IAgentMemoryStateHashProjector` and `IAgentMemoryCurationProjector`. It owns:

- exact Tenant comparison;
- expected Candidate/Memory state hash comparison;
- allowed source lifecycle states;
- canonical content hash comparison;
- expected new Memory state hash validation;
- typed `AgentMemoryOperationException` failures.

The Store owns only resource existence, identity availability, locking, and
persisting the returned mutation snapshots atomically.

Both `InMemoryAgentMemoryStore` and `PostgreSqlAgentMemoryStore` consume this
same state machine. A test-only or compatibility fallback that copies the old
logic is prohibited.

`AddAgentMemoryReadRuntime` registers the state-hash projector and state-machine
and curation-projector interfaces as provider prerequisites even when the Host
does not add the formal curation service/marker. This mirrors the existing Store
shape: the selected Store can implement conditional primitives while the
read-only Host does not expose `IAgentMemoryPromotionService`.
`AddAgentMemoryCuration` still exclusively owns admission of the formal
curation mainline.

### 6.5 Exact Memory persistence comparer

The create-or-replay comparison must include every persisted property:

```text
TenantId, MemoryId, Kind, Content, CanonicalContentHash, PromotedAt,
Confidence, Status, IsAuthoritative, Tags, DescriptorRefs, SourceRefs,
SupersedesMemoryId, SupersededByMemoryId, RedactionKinds,
SanitizationDiagnostics
```

Collections use sequence equality and nested snapshot value equality. The
comparer belongs on the provider-neutral boundary and is shared by both Stores.
Hash equality alone is not used as the replay equality test.

---

## 7. Frozen Store Semantics

| Operation | Missing identity | Existing identity | Concurrency / order |
| --- | --- | --- | --- |
| Save Conversation | insert sanitized snapshot | replace sanitized snapshot | last committed replacement wins; Turn order unchanged |
| Save Task | insert sanitized snapshot | replace sanitized snapshot | serialized per Task; replacement is explicit aggregate replacement |
| Append Task Event | `ResourceUnavailable` | append sanitized event or no-op when rejected | two committed appends are both visible; committed order survives restart |
| List Tasks | empty list | n/a | final `TaskId` order by `StringComparer.Ordinal` |
| Create Context | insert | `IdentityConflict` | all Context + Blocks or none |
| Save Context | insert | atomic replacement | old Block projection and new aggregate switch together |
| Save/Create Candidate | insert | `IdentityConflict` | batch Create is all-or-none |
| Transition Candidate | `ResourceUnavailable` | expected-status atomic update | mismatch is `InvalidLifecycleState` |
| Save Memory | insert only when Active, non-authoritative, and unlinked | exact replay succeeds; difference is `StateConflict` | cannot create/update lifecycle or authority state |
| List Memories | empty list | n/a | Store filters + final `MemoryId` order by `StringComparer.Ordinal` |
| Conditional curation | `ResourceUnavailable` | state-machine result | one atomic database primitive |

### 7.1 Cancellation

Every method observes cancellation before its first durable write. For
multi-write operations, cancellation or failure before commit rolls the whole
transaction back. Commit itself follows the existing coordinator rule and uses
an uncancelled acknowledgement attempt so caller cancellation cannot turn an
already-submitted COMMIT into a false rollback claim.

### 7.2 Input identity validation

#55 does not invent a new public identity exception taxonomy. Existing domain
validation remains authoritative. The provider must at minimum verify after
deserialization that top-level Tenant/ID and structured columns agree. Existing
tenant mismatch rules for Context Blocks are preserved.

Task Event IDs are not deduplicated and Block order is not inferred from ID.
The current contracts do not declare EventId a Store identity.

---

## 8. Sanitization and Snapshot Boundary

### 8.1 Conversation

The durable write sequence is:

```text
copy input collection
    ↓ each Turn in submitted order
IAgentMemoryContentSanitizer.Sanitize(conversation.TenantId, Content, SourceRefs)
    ↓
Rejected -> omit Turn + add safe rejection diagnostic
Accepted -> persist SanitizedContent + sanitizer Diagnostics
    ↓
deep Snapshot
    ↓
source-generated JSON
    ↓
PostgreSQL transaction
```

The original `Content` value must never be serialized, logged, placed in an
exception, or added as a database parameter.

### 8.2 Task History

`Title` remains existing structured Task metadata and is persisted unchanged.
The current sanitizer boundary covers:

- optional Summary;
- every Event Content.

A rejected Summary becomes null plus a safe diagnostic. A rejected Event is not
appended/persisted. `AppendEventAsync` performs the same sanitization as
`SaveTaskAsync`; it is not a raw fast path.

### 8.3 Compressed Context, Candidate, and Memory

These artifacts are already canonical outputs. The provider:

- snapshots them;
- verifies required identity/graph projection invariants;
- computes state hashes only through the shared projector where applicable;
- serializes them unchanged.

It does not invoke `IAgentMemoryContentSanitizer` for their Content.

### 8.4 Diagnostics and source metadata

SourceRefs, DescriptorRefs, RedactionKinds, SanitizationDiagnostics, and prompt
evidence summaries are part of the durable snapshot. The Store preserves their
sequence and values. It does not strip provenance merely because a recall
caller later requests `IncludeSourceRefs=false`; that is an output boundary,
not a persistence mutation.

---

## 9. PostgreSQL Schema

### 9.1 Storage shape

Use full aggregate JSON plus structured durable columns. The JSON is the
detached aggregate representation; structured columns are enforcement/query
projections and must agree with it.

All textual identity columns use deterministic `C` collation. All rows include
`state_contract_version = 1`; unknown versions fail closed rather than using
reflection or best-effort deserialization.

### 9.2 Logical tables

```text
agent_memory_conversations
    tenant_id                  text collate "C"
    conversation_id            text collate "C"
    revision                   bigint > 0
    state_contract_version     integer = 1
    state_json                 jsonb
    created_at                 timestamptz
    updated_at                 timestamptz
    PK (tenant_id, conversation_id)

agent_memory_tasks
    tenant_id                  text collate "C"
    task_id                    text collate "C"
    revision                   bigint > 0
    state_contract_version     integer = 1
    state_json                 jsonb
    created_at                 timestamptz
    updated_at                 timestamptz
    PK (tenant_id, task_id)

agent_memory_compressed_contexts
    tenant_id                  text collate "C"
    context_id                 text collate "C"
    revision                   bigint > 0
    state_contract_version     integer = 1
    state_json                 jsonb
    created_at                 timestamptz
    updated_at                 timestamptz
    PK (tenant_id, context_id)

agent_memory_compressed_blocks
    tenant_id                  text collate "C"
    block_id                   text collate "C"
    context_id                 text collate "C"
    ordinal                    integer >= 0
    state_contract_version     integer = 1
    block_json                 jsonb
    PK (tenant_id, block_id)
    UNIQUE (tenant_id, context_id, ordinal)
    FK (tenant_id, context_id)
        -> agent_memory_compressed_contexts (tenant_id, context_id)
        ON DELETE CASCADE

agent_memory_candidates
    tenant_id                  text collate "C"
    candidate_id               text collate "C"
    revision                   bigint > 0
    status                     integer
    kind                       integer
    canonical_content_hash     text collate "C"
    state_hash                 text collate "C"
    state_contract_version     integer = 1
    state_json                 jsonb
    created_at                 timestamptz
    updated_at                 timestamptz
    PK (tenant_id, candidate_id)

agent_memories
    tenant_id                  text collate "C"
    memory_id                  text collate "C"
    revision                   bigint > 0
    status                     integer
    kind                       integer
    confidence                 integer
    promoted_at                timestamptz
    canonical_content_hash     text collate "C"
    state_hash                 text collate "C"
    supersedes_memory_id       text collate "C" null
    superseded_by_memory_id    text collate "C" null
    state_contract_version     integer = 1
    state_json                 jsonb
    created_at                 timestamptz
    updated_at                 timestamptz
    PK (tenant_id, memory_id)
```

### 9.3 Memory graph constraints

`agent_memories` adds:

- tenant-scoped self foreign keys for `supersedes_memory_id` and
  `superseded_by_memory_id`, both deferrable and initially deferred;
- a check that neither link points to the row itself;
- a partial unique index on `(tenant_id, supersedes_memory_id)` where the value
  is non-null, preventing two durable replacements from claiming the same old
  Memory;
- status range checks matching the current enum, without a Stale value.

Foreign keys prove endpoint existence, not reciprocal semantics. The shared
state machine plus atomic Supersede write owns reciprocity. Reads verify that a
linked pair is reciprocal and fail closed on corruption.

`SaveMemoryAsync` cannot be used to import a privileged/lifecycle-advanced
snapshot or assemble links one row at a time. A missing identity is insertable
only when the supplied snapshot is `Status = Active`,
`IsAuthoritative = false`, `SupersedesMemoryId = null`, and
`SupersededByMemoryId = null`. Any other new-snapshot shape fails with
`InvalidLifecycleState` before INSERT. The only production graph writer is
atomic conditional Supersede; #55 defines no privileged import path.

### 9.4 Query columns

Store-level status/kind/ID filters run in SQL. Tags and DescriptorRefs may be
filtered from source-generated detached snapshots in v1 to preserve exact .NET
sequence/value semantics; the provider must not use locale-sensitive or
approximate JSON matching.

Database queries always use an explicit deterministic order suitable for
stable plans and index use:

```sql
order by memory_id collate "C"
order by task_id collate "C"
```

PostgreSQL `C` collation compares encoded bytes and is not the public ordering
contract for every Unicode scalar sequence. After materialization, the Store
must therefore apply a final detached sort with `StringComparer.Ordinal` before
returning `ListTasksAsync` or `ListMemoriesAsync`. This final .NET sort is the
observable contract, including non-BMP identifiers. No result relies on
primary-key scan order or database locale.

### 9.5 V010 migration

Add one checksummed migration:

```text
V010_agent_memory_durable_store
```

It creates all six tables, constraints, foreign keys, and indexes in one
migration. V001–V009 remain immutable.

The Runtime schema metadata model and startup validator must validate:

- exact tables and columns;
- type and nullability;
- primary keys;
- named checks;
- unique/non-unique indexes and predicates;
- foreign-key columns, referenced columns, exact delete action, and
  deferrability;
- required `C` collation on identity/order columns.

If the current manifest cannot represent either column collation or foreign-key
delete action, extend its metadata model and validator together. Do not merely
document either property while leaving startup unable to verify it.

Validation-only startup fails closed when V010 or any required shape is absent.
Apply mode creates it, and repeated Apply is non-mutating.

---

## 10. Transaction, Locking, and Concurrency Model

### 10.1 One coordinator, two explicit transaction modes

Ordinary mutating Store methods enter the existing
`PostgreSqlRuntimeTransactionCoordinator.ExecuteAsync`. They may join an
ambient Runtime transaction; otherwise the coordinator owns begin, commit,
rollback, unavailability translation, and commit-unknown translation.

Formal `PromoteAsync`, `RejectAsync`, `SupersedeAsync`, and `ArchiveAsync`
instead enter a new provider-internal `ExecuteTopLevelAsync` (or equivalently
named concrete coordinator primitive). It must:

1. inspect the Runtime transaction accessor before any advisory lock, SQL
   command, or mutation;
2. fail immediately when an ambient Runtime transaction already exists;
3. otherwise begin, execute, and commit its own transaction before returning;
4. preserve the existing rollback, unavailability, and commit-unknown taxonomy.

The failure is
`RuntimePersistenceContractException(AmbientCommitBoundaryUnsupported)`. Add
`AmbientCommitBoundaryUnsupported = 5` additively to
`RuntimePersistenceContractErrorCode`; it is a caller/composition contract
violation, not a deterministic Agent Memory domain outcome.

This boundary is required because #56 publishes committed Accountability only
after the formal Store call returns. Joining a caller-owned transaction would
allow a committed fact to be published before the durable mutation commits, or
even when it later rolls back. No formal curation method may silently join,
suspend, or commit an ambient transaction.

Store code uses `PostgreSqlRuntimeSession.EnterCommand()` for each command and
does not issue concurrent commands on one session.

### 10.2 Identity locks

Use deterministic transaction-scoped advisory locks where a not-yet-existing
row must participate in serialization:

```text
agent-memory | tenant | artifact-kind | artifact-id
```

Multiple lock identities are sorted ordinally before acquisition. This is
required for:

- Candidate batch creation;
- Context replacement across current/new Block IDs;
- new Memory identity in Promote/Supersede;
- overlapping graph mutations where a missing endpoint cannot be row-locked.

Advisory locks are provider implementation details and never appear in public
contracts.

### 10.3 Row locks

Use `SELECT ... FOR UPDATE` for current Task, Context, Candidate, and Memory
rows before read-modify-write. Curation locks are acquired in one documented
global order:

```text
new identity advisory locks
    -> target Memory rows ordered by MemoryId
    -> Candidate rows ordered by CandidateId
```

No path may reverse this order.

### 10.4 Domain conflicts avoid provider exception translation

Expected identity conflicts use prechecks or
`INSERT ... ON CONFLICT DO NOTHING RETURNING ...`. They are converted inside
the transaction into typed `AgentMemoryOperationException` values, causing a
rollback.

The implementation must not rely on a PostgreSQL unique violation escaping the
coordinator, because the generic coordinator correctly treats unclassified
Npgsql failures as infrastructure failures.

Unexpected FK/check violations indicate provider/persisted invariant failure,
not a user identity conflict.

### 10.5 Task append

`AppendEventAsync` locks the Task row, deserializes its current snapshot,
sanitizes the Event, and writes revision + 1. Two successful concurrent calls
serialize on that row and both Events appear exactly once in the resulting
sequence.

The contract does not promise which concurrent Event appears first. Once
committed, the stored order is stable and survives every restart.

### 10.6 Context replacement

One transaction:

```text
lock Context identity + union(old Block IDs, new Block IDs)
validate new snapshot and tenant-wide Block availability
upsert Context aggregate and increment revision
delete old Block projection
insert new Blocks with submitted ordinal
commit
```

The parent Context upsert always precedes child Block INSERTs, including first
creation, so the declared foreign key is satisfied without relying on deferred
behavior. Because all steps share one transaction, a replacement failure rolls
the parent JSON/revision and child projection back together.

Any conflict/failure restores the old aggregate and old Block projection. A
fresh reader can observe only the old complete version or the new complete
version, never a mixed projection.

### 10.7 Candidate and Memory base writes

Candidate creation snapshots the input, computes its state hash through
`IAgentMemoryStateHashProjector`, and inserts JSON + structured columns in one
statement. `CreateCandidatesAsync` acquires all identity locks in ordinal order,
prepares every snapshot/hash before the first INSERT, and rolls the whole batch
back if any identity exists.

`TransitionCandidateStatusAsync` locks the row, establishes the expected
current Status, creates a detached snapshot with the requested Status, recomputes
its state hash, and updates JSON/Status/hash/revision together. It remains a
compatibility primitive and is never called by formal Promote or Reject.

Memory creation accepts a missing identity only for an Active,
non-authoritative snapshot whose two graph links are null. Any new snapshot
that is Candidate, Rejected, Superseded, Archived, authoritative, or linked is
`InvalidLifecycleState` before SQL mutation. When the identity exists, the
provider locks and validates the persisted snapshot; exact replay is a no-op
and every difference is `StateConflict`. It never rewrites JSON, authority,
lifecycle columns, graph columns, revision, or timestamps for an exact replay.

---

## 11. Persisted Invariant Validation

Every read validates before returning a snapshot.

### 11.1 Common checks

- `state_contract_version == 1`;
- JSON deserializes through the exact generated `JsonTypeInfo`;
- JSON top-level TenantId and ID equal structured columns ordinally;
- revision is positive;
- enum columns are defined and match JSON;
- canonical-content hash column matches the JSON digest.

### 11.2 Candidate and Memory checks

- recomputed state hash from `IAgentMemoryStateHashProjector` equals the stored
  state-hash digest;
- Memory confidence, PromotedAt instant, Status, and graph links match columns;
- graph endpoints belong to the same Tenant;
- reciprocal Supersede links agree when present.

### 11.3 Context checks

`GetCompressedContextAsync` loads Block projections ordered by ordinal and
verifies their count, IDs, ordinals, tenants, and semantic JSON snapshots equal
the aggregate Blocks. `GetCompressedContextBlockAsync` does not trust the Block
row in isolation: it loads the referenced parent Context, validates both
snapshots, and requires that the Block row's `(context_id, ordinal)` identifies
exactly `parent.Blocks[ordinal]` with the same BlockId, TenantId, and semantic
snapshot. A missing parent, out-of-range ordinal, or mismatch fails as persisted
invariant corruption rather than returning the detached Block.

A mismatch throws provider-neutral
`RuntimePersistenceContractException(PersistedInvariantViolation)`. The Store
does not return a partial aggregate, silently rebuild a projection, or
re-sanitize historical content.

---

## 12. Conditional Curation Protocol

Every protocol below runs through the top-level coordinator mode from §10.1.
Ambient-boundary rejection occurs before locks, mutation, and state-machine
projection. A successful Store return means its database COMMIT completed; an
unknown acknowledgement remains `RuntimeTransactionCommitUnknownException` and
must not be reported as a known committed or known failed curation result.

### 12.1 Promote

One provider-owned top-level transaction performs:

```text
lock new Memory identity
lock Candidate row
Candidate exists in Tenant
new Memory identity is absent
shared state machine validates expected Candidate state/lifecycle/content
shared state machine returns Candidate Active + new Memory Active
insert Memory
update Candidate, state hash, revision
commit
```

No Memory is visible if the Candidate update fails. No Candidate transition is
visible if Memory insertion fails.

### 12.2 Reject

One provider-owned top-level transaction locks the Candidate, invokes the shared
state machine, and updates Status/JSON/state hash/revision together. A stale
expectation or invalid lifecycle produces zero durable mutation.

### 12.3 Supersede

One provider-owned top-level transaction performs the complete three-node graph
mutation:

```text
old Memory Active
    -> Superseded
    -> SupersededByMemoryId = new Memory ID

new Memory
    -> Active
    -> SupersedesMemoryId = old Memory ID

replacement Candidate Candidate
    -> Active
```

The provider locks the target Memory, replacement Candidate, and new Memory
identity before projection. It then writes all three shared state-machine
snapshots and their state hashes/revisions before one commit.

An injected failure after any individual SQL statement must leave the old
Memory Active, Candidate Candidate, and new Memory absent after rollback.

### 12.4 Archive

The shared state machine permits only Active or Superseded → Archived. Existing
`SupersedesMemoryId`/`SupersededByMemoryId` links are retained. The provider
updates JSON, Status, state hash, and revision atomically.

### 12.5 Conflict outcomes

For conditional curation, the following are deterministic domain failures only
when established from locked current state:

- `ResourceUnavailable` — required durable resource is definitively absent;
- `TenantMismatch` — supplied/loaded domain Tenant mismatch;
- `IdentityConflict` — requested new identity is definitively occupied;
- `InvalidLifecycleState` — current locked lifecycle disallows transition;
- `StateConflict` — expected state/content/new-state hash mismatches.

No catch-all Npgsql translation may produce these codes.

Base Store methods may establish the same typed outcomes only from their frozen
complete inputs and advisory/row-locked identity state. In particular, missing
Task append is `ResourceUnavailable`, an invalid new Memory creation shape is
`InvalidLifecycleState`, and an existing non-exact Memory replay is
`StateConflict`; none is inferred from a catch-all provider exception.

---

## 13. Recall and Source Expansion Parity

### 13.1 Store-level Memory filtering

`ListMemoriesAsync` preserves current filters:

```text
exact TenantId
Kinds any
Tags any
MemoryIds any
DescriptorRefs any exact match
Status:
    Active always
    Superseded only IncludeSuperseded
    Archived only IncludeArchived
    Candidate/Rejected never
IncludeStale: no-op
```

It returns `MemoryId` ordinal order only.

### 13.2 Retriever ownership

`DefaultAgentMemoryRetriever` remains unchanged and owns:

- confidence/kind/PromotedAt/final tie-break ordering;
- minimum confidence;
- Descriptor visibility closure;
- `MaxCount` and CharacterBudget;
- diagnostics and truncation;
- ScopeFingerprint, VisibleMemorySetHash, and CanonicalPackHash.

The durable provider must produce the same Pack and hashes as InMemory for the
same snapshots and query.

### 13.3 Source Expansion ownership

The Store preserves source material and sequence. Existing Source Expander and
#56 ReadCore still own range resolution, Grant/visibility, final sanitization,
effective visible result hashing, and Accountability projection.

After fresh ServiceProvider/process restart, the same Conversation range, Task
range, Context Block ID, Candidate ID, and Memory ID must expand to the same
domain content before the existing ReadCore boundary.

---

## 14. DI and Host Composition

### 14.1 Feature-neutral provider registration

`AddCrestCreatesPostgreSqlRuntimePersistence(options)` remains the base
persistence-kernel registration. It installs the existing connection,
transaction, migration, schema validation, failure translation, and pre-existing
participants only. It must neither reference Agent Memory runtime semantics nor
remove/register any of the four Agent Memory Store contracts.

A Host that uses the PostgreSQL persistence kernel without
`AddAgentMemoryRuntime` must build and pass `ValidateOnBuild`/scope validation.
Agent Memory is not a mandatory feature of the base provider.

### 14.2 Explicit Agent Memory provider registration

Add a separate opt-in extension:

```csharp
AddCrestCreatesPostgreSqlAgentMemoryPersistence()
```

It requires the base PostgreSQL persistence registration to be present by Host
build/validation time and replaces exactly these runtime Store contracts:

```text
RemoveAll<IAgentConversationStore>()
RemoveAll<IAgentTaskHistoryStore>()
RemoveAll<IAgentCompressedContextStore>()
RemoveAll<IAgentMemoryStore>()

PostgreSqlAgentConversationStore -> IAgentConversationStore
PostgreSqlAgentTaskHistoryStore -> IAgentTaskHistoryStore
PostgreSqlAgentCompressedContextStore -> IAgentCompressedContextStore
PostgreSqlAgentMemoryStore -> IAgentMemoryStore
```

`PostgreSqlAgentMemoryStore` itself implements
`IAgentMemoryConditionalCurationStore` and `IAgentMemoryStoreCapabilities`.
Those are capabilities discovered by casting the selected
`IAgentMemoryStore`, matching the existing #56 composition validator. The
provider extension does not register separate conditional/capability service
descriptors and therefore cannot create a second instance or divergent Store
truth.

### 14.3 Registration order and semantic ownership

After the base provider is registered, composition must work in both feature
orders:

```text
AddAgentMemoryRuntime()
AddCrestCreatesPostgreSqlAgentMemoryPersistence()
```

and:

```text
AddCrestCreatesPostgreSqlAgentMemoryPersistence()
AddAgentMemoryRuntime()
```

The explicit provider extension owns the final four Store selections; existing
runtime `TryAdd` behavior must not restore InMemory Stores in the second order.
Tests resolve each Store contract and prove the same behavior in both orders.

Neither PostgreSQL extension registers sanitization, canonical hashing,
curation projector/state machine, Retriever, Expander, promotion service, or
Accountability. Missing required Agent Memory runtime semantics fail at
composition/resolution; the provider does not install hidden fallbacks.

The final #56 `AgentMemoryCurationCompositionValidator` validates the selected
`IAgentMemoryStore` by casting that single instance to the conditional and
capability interfaces. It must pass with the durable Store and fail closed when
the selected Store does not expose both surfaces or does not report
`ConfirmedAtomic`.

---

## 15. Exception and Outcome Taxonomy

### 15.1 Deterministic Memory operation failures

Use `AgentMemoryOperationException` only for established domain results listed
in §12.5. Messages contain bounded identifiers/categories and never raw Memory
content, Task content, SQL, connection strings, or provider error text.

### 15.2 Infrastructure unavailable

Connection/open/command/provider failures before a known commit outcome surface
as:

```text
RuntimePersistenceUnavailableException
```

They are not converted to `ResourceUnavailable`, `StateConflict`, or
`IdentityConflict`.

### 15.3 Commit acknowledgement unknown

An exception after COMMIT submission where acknowledgement is indeterminate
surfaces as:

```text
RuntimeTransactionCommitUnknownException
```

The Store does not retry automatically. The caller may read the relevant
Candidate/Memory graph to reconcile, but #55 defines no operation receipt or
exact business replay protocol.

Because #56 records curation failures only for typed deterministic
`AgentMemoryOperationException` values, commit-unknown produces no false
“definitely failed” Accountability fact.

### 15.4 Persisted corruption

Invalid JSON, unknown state contract version, structured/JSON mismatch,
impossible graph shape, or state-hash mismatch surfaces as
`RuntimePersistenceContractException(PersistedInvariantViolation)`. It is not a
domain conflict and is not silently repaired.

### 15.5 Unsupported ambient commit boundary

A formal curation call made while a Runtime transaction is already ambient
fails before mutation as:

```text
RuntimePersistenceContractException(
    AmbientCommitBoundaryUnsupported)
```

This is distinct from `ConcurrentAmbientUse`, infrastructure unavailability,
commit unknown, persisted corruption, and every
`AgentMemoryOperationException`. The provider does not publish Accountability,
and #56 receives no successful Store result from which it could publish a
committed fact.

---

## 16. AOT and Serialization

### 16.1 Exact generated roots

Extend `PostgreSqlRuntimeJsonSerializerContext` with exact roots for:

```text
AgentConversationRecord
AgentTaskRecord
AgentCompressedContext
AgentCompressedContextBlock
AgentMemoryCandidate
AgentMemoryItem
```

Nested Turns, Events, SourceRefs, Diagnostics, DescriptorRefs, CanonicalHash,
and prompt evidence summaries are transitive metadata.

No Store path uses:

- `JsonSerializer.Serialize(value)` without `JsonTypeInfo`;
- runtime type lookup;
- reflection resolver fallback;
- Npgsql dynamic JSON mapping;
- open `object` payloads;
- trimming/AOT suppression as a substitute for generated metadata.

Operation Plans/Requests are not persisted and therefore are not provider JSON
roots.

### 16.2 NativeAOT evidence

Extend the existing PostgreSQL Runtime AOT host/fixture. The published original
linux-x64 native executable must:

1. apply/validate V010;
2. save sanitized Conversation and Task state;
3. persist Context and Block projection;
4. create Candidate and perform at least Promote and Supersede or Archive;
5. rebuild/fresh-process the provider;
6. read/expand/recall the durable state;
7. validate the formal curation composition;
8. print exactly:

```text
CRESTCREATES_DURABLE_AGENT_MEMORY_OK
```

`NativeAOT-verified` requires publish, native link, and execution of that
original binary. Analyzer, trim, managed-host, or source-generated JSON unit
tests alone are insufficient.

---

## 17. Acceptance Case Matrix

The implementation Plan must preserve these IDs and map each to one or more
named executable tests.

### 17.1 Happy path

| ID | Case |
| --- | --- |
| H01 | Conversation save → fresh provider read preserves sanitized content and Turn sequence |
| H02 | Task save + append → fresh provider read preserves Summary/Event content and sequence |
| H03 | first Context save with non-empty Blocks satisfies the immediate FK; fresh provider direct Block lookup returns the matching snapshot |
| H04 | Candidate Promote commits Memory Active + Candidate Active |
| H05 | Supersede commits reciprocal three-node graph |
| H06 | Archive retains prior graph links and survives restart |
| H07 | Recall before/after restart returns same Memory order, Pack, and canonical hashes |
| H08 | Source Expansion before/after restart returns same pre-ReadCore source material |
| H09 | exact `SaveMemoryAsync` replay succeeds without revision/state mutation |

### 17.2 Boundary

| ID | Case |
| --- | --- |
| B01 | same Conversation/Task/Context/Candidate/Memory ID in different Tenants is independent |
| B02 | every cross-Tenant lookup returns null/empty without existence leakage |
| B03 | same BlockId in different Tenants succeeds |
| B04 | same BlockId in one Tenant across Contexts conflicts |
| B05 | replacing Context removes old Block projection atomically |
| B06 | Turn/Event/Block order is not timestamp/ID sorted |
| B07 | two committed concurrent Task appends are both visible |
| B08 | concurrent append order, once committed, is stable across restart |
| B09 | `ListTasksAsync` and `ListMemoriesAsync` match `StringComparer.Ordinal` for non-BMP IDs under non-default database locale |
| B10 | `IncludeStale` remains a no-op and no Stale schema/status exists |
| B11 | Store Memory filter results match InMemory for kind/tag/ID/descriptor/status combinations |
| B12 | caller mutation after write and after read cannot mutate durable state |
| B13 | Candidate batch with one conflict writes none of the batch |
| B14 | existing `SaveMemoryAsync` identity with lifecycle/authority/link-only difference is `StateConflict` |
| B15 | registration order never restores any InMemory Store |
| B16 | direct `SaveMemoryAsync` cannot create a one-sided Supersede graph |
| B17 | append to a missing Task intentionally returns `ResourceUnavailable`, not the legacy `InvalidOperationException` |
| B18 | direct creation of non-Active, authoritative, or linked Memory is `InvalidLifecycleState` with zero mutation |

### 17.3 Failure and concurrency

| ID | Case |
| --- | --- |
| F01 | Promote with occupied MemoryId leaves Candidate unchanged |
| F02 | stale Candidate state hash conflicts with zero mutation |
| F03 | two concurrent Promotes have exactly one winner |
| F04 | Reject with stale expectation has zero mutation |
| F05 | Supersede failure after each SQL write point exposes no partial graph |
| F06 | two concurrent Supersedes/Archives on one target have one valid winner |
| F07 | crash before curation COMMIT exposes no mutation after backend exit |
| F08 | crash after committed curation remains visible to a fresh process |
| F09 | database unavailable remains `RuntimePersistenceUnavailableException` |
| F10 | commit acknowledgement loss remains `RuntimeTransactionCommitUnknownException`/unknown outcome |
| F11 | malformed JSON/version/column mismatch fails as persisted invariant violation |
| F12 | rejected/raw Conversation or Task content is absent from captured DB parameters and rows |
| F13 | context Block conflict restores the old aggregate/projection |
| F14 | caller cancellation before first write produces zero mutation |
| F15 | formal curation inside an existing ambient Runtime transaction fails with `AmbientCommitBoundaryUnsupported` before mutation |
| F16 | tampered Block `context_id`/ordinal or parent aggregate mapping fails as persisted invariant corruption |

### 17.4 Composition

| ID | Case |
| --- | --- |
| C01 | the selected durable `IAgentMemoryStore` instance implements conditional + capabilities and no separate service descriptors are required |
| C02 | formal curation validator passes and reports ConfirmedAtomic |
| C03 | provider references Agent.Memory.Abstractions but no concrete Memory runtime |
| C04 | Memory Store constructors/source contain no Accountability producer/recorder/sink |
| C05 | existing Retriever works unchanged and has InMemory/PostgreSQL parity |
| C06 | existing Source Expander/ReadCore works unchanged after restart |
| C07 | #56 known commit and typed conflict facts remain correct with durable Store |
| C08 | provider unavailable/commit-unknown creates no false deterministic curation failure fact |
| C09 | V010 apply, validate-only, reapply, checksum, shape, column-collation, and FK-delete-action gates pass |
| C10 | source-generated JSON roots cover every persisted path with no reflection fallback |
| C11 | dependency boundary and canonical solution build are green |
| C12 | original native executable prints `CRESTCREATES_DURABLE_AGENT_MEMORY_OK` |
| C13 | #56 committed Accountability is never published before the provider-owned curation COMMIT succeeds |
| C14 | base PostgreSQL persistence without Agent Memory runtime builds and validates successfully |
| C15 | explicit Agent Memory provider registration replaces all four Stores in either feature registration order |
| C16 | promotion preparation and locked Store mutation use one shared projector and produce value-identical snapshots |

---

## 18. Required Test Structure

### 18.1 Shared provider-neutral contract kit

Create runner-free cases for:

```text
AgentMemoryStoreContractCases
AgentMemoryCurationStoreContractCases
```

At minimum, concrete runners expose these named tests:

```text
Conversation_Should_Preserve_TenantIsolation
Conversation_Should_Return_Snapshot
Conversation_Should_Persist_Only_Sanitized_Turns
Conversation_Should_Preserve_TurnSequence

Task_Should_Preserve_TenantIsolation
Task_Should_Return_Snapshot
Task_Should_Persist_Only_Sanitized_Content
Task_Should_Preserve_Deterministic_Order
Concurrent_TaskAppend_Should_Not_Lose_Event
TaskAppend_MissingTask_Should_Return_ResourceUnavailable

CompressedContext_Should_Return_Snapshot
CompressedContext_Should_Reject_CrossTenant_Block
BlockIdentity_Should_Be_TenantWide_Unique
ReplacingContext_Should_Remove_Old_BlockProjection

Candidate_Should_Return_Snapshot
Memory_Should_Return_Snapshot
SaveMemory_Should_Be_CreateOrExactReplay
SaveMemory_InvalidInitialLifecycleOrAuthority_Should_BeRejected
ListMemories_Should_Be_Ordinally_Deterministic
ListStores_NonBmpIdentifiers_Should_Match_StringComparerOrdinal
Memory_Query_Should_Match_InMemory_Contract

Promote_Should_Be_Atomic
Promote_With_StaleCandidateHash_Should_Conflict
ConcurrentPromote_Should_Have_ExactlyOneWinner
Reject_Should_Be_Conditional
Supersede_Should_Commit_ThreePartGraph_Atomically
Supersede_Failure_Should_Expose_No_PartialGraph
Archive_Should_Be_Conditional
ConcurrentArchive_Should_Have_ExactlyOneWinner
CurationCapabilities_Should_Be_ConfirmedAtomic
PromotionPreparation_AndStoreMutation_Should_UseSameCurationProjection
```

InMemory and PostgreSQL must execute the same semantic cases. PostgreSQL-only
durability evidence does not excuse provider semantic drift, and InMemory does
not claim process durability.

### 18.2 PostgreSQL restart/crash/failure suites

Use the existing schema lease, Testcontainers collection, CrashWorker, and
backend-exit waiting infrastructure. Do not replace process-crash evidence with
disposing a ServiceProvider.

Required named groups:

```text
PostgreSqlAgentMemoryRestartTests
PostgreSqlAgentMemoryConcurrencyTests
PostgreSqlAgentMemoryCrashTests
PostgreSqlAgentMemoryFailureTaxonomyTests
PostgreSqlAgentMemoryMigrationTests
PostgreSqlAgentMemoryCompositionTests
```

The PostgreSQL composition/failure suites additionally expose:

```text
CommittedAccountability_Should_Never_Precede_DurableCommit
ContextCreate_WithBlocks_Should_SatisfyImmediateForeignKey
FormalCuration_WithPreexistingAmbientTransaction_Should_FailBeforeMutation
PostgreSqlProvider_WithoutAgentMemoryRuntime_Should_ValidateAndBuild
PostgreSqlAgentMemoryPersistence_Should_ReplaceStores_InEitherOrder
TamperedBlockContextOrOrdinal_Should_FailPersistedInvariantValidation
V010Manifest_Should_ValidateCollationAndForeignKeyDeleteAction
```

Crash-before-commit and crash-after-commit cases must kill a worker process and
wait for its PostgreSQL backend to exit before a fresh provider reads state.

### 18.3 Architecture guards

Add executable guards for:

- provider dependency edges;
- shared semantic surface use by both Stores;
- no copied canonical writer/state-machine implementation in PostgreSQL;
- no Accountability dependency in Memory Store classes;
- no recall/budget/visibility/pack-hash implementation in PostgreSQL;
- exact generated JSON roots and no reflection resolver;
- V010 manifest/case ledger presence, including column collation and exact
  foreign-key delete action;
- formal curation's top-level commit-boundary guard;
- direct Block reads validating the parent aggregate mapping;
- only the selected `IAgentMemoryStore` being used for conditional/capability
  discovery;
- no Stale status/schema invention;
- NativeAOT sentinel and original-binary execution.

---

## 19. Delivery Slices for the Later Implementation Plan

The implementation Plan should use this order without reopening the design:

```text
1. Acceptance ledger + runner-free Store contracts
2. Provider-neutral state hash/curation projector/state-machine/comparer +
   Promotion Service and InMemory alignment
3. V010 schema/manifest/JSON roots + feature-neutral base and explicit Agent
   Memory provider registration
4. Conversation + Task durable Stores
5. Context + tenant-wide Block projection
6. Candidate/Memory base Store + exact replay/query parity
7. Promote + Reject atomic curation
8. Supersede + Archive atomic graph curation
9. concurrency + top-level curation commit boundary + failure taxonomy + real
   crash evidence
10. restart Recall/Source Expansion + #56 Accountability composition
11. NativeAOT publish-link-run + canonical closure evidence
```

Do not implement four independent CRUD Stores first and retrofit atomicity later.
The shared semantic boundary and contract cases precede provider code.

---

## 20. Rejected Approaches

### 20.1 A second persistence kernel

Rejected because #24 already owns connection, transaction, migration,
capability, exception, crash, and AOT infrastructure.

### 20.2 EF Core provider

Rejected for #55 because the implemented production provider mainline is direct
Npgsql with existing NativeAOT evidence. A future EF integration would require
its own separately declared capability and evidence.

### 20.3 JSON-only `(tenant, id, payload)` tables

Rejected because conditional curation, tenant-wide Block identity, Memory graph
integrity, query filters, deterministic order, and persisted invariant checks
need structured database columns/constraints.

### 20.4 Fully relationalizing every nested artifact

Rejected because it moves Agent Memory object mapping and domain evolution into
the provider, increases sequence/provenance drift risk, and provides no #55
benefit for Diagnostics, SourceRefs, prompt evidence, or canonical metadata.

### 20.5 Provider-owned canonical hash implementation

Rejected because it creates a second hash truth and a NativeAOT/provider drift
surface.

### 20.6 Provider-owned curation lifecycle logic

Rejected because InMemory and PostgreSQL would become separate formal
state machines. Both must consume one provider-neutral runtime implementation.

### 20.7 Re-sanitizing every artifact before database write

Rejected because Compressed Context/Candidate/Memory Content and
CanonicalContentHash could no longer describe the same canonical artifact.

### 20.8 SQL recall/ranking/budget logic

Rejected because Store persistence and Recall semantics have separate owners.
It would duplicate visibility and canonical pack rules in a provider.

### 20.9 Automatic retry on commit-unknown

Rejected because no durable curation operation receipt exists in #55. Retrying
could create a false conflict or duplicate a different requested identity while
the original transaction actually committed.

### 20.10 Store-written Accountability

Rejected because #56 freezes service/ReadCore ownership and the unified
`IAuditRecorder` path. A Store fact would have the wrong semantic boundary and
could contradict an unknown commit outcome.

---

## 21. Review Guardrails

Every implementation review answers these with code/test evidence:

1. Does the PostgreSQL project reference only Agent Memory Abstractions?
2. Do Promotion Service, InMemory, and PostgreSQL curation use the same pure
   curation projector and state machine, or was projection logic copied?
3. Can a missing Memory identity be inserted with anything other than Active,
   non-authoritative, and both graph links null?
4. Can `SaveMemoryAsync` change Status, authority, or a graph link on an
   existing row instead of requiring exact replay?
5. Does append to a missing Task produce the intentional
   `ResourceUnavailable` contract result?
6. Can two committed Task appends lose one Event?
7. Can Turn/Event/Block order change after restart?
8. Is BlockId tenant-wide unique rather than context-local?
9. Does Context create/replacement upsert the parent before child INSERTs, and
   can it expose new aggregate + old Blocks or vice versa?
10. Does direct Block read validate `(context_id, ordinal)` against the loaded
    parent aggregate rather than trusting the projection row?
11. Does every key/index/FK/lock/query include TenantId?
12. Are DB identity/order columns explicitly `C`, are final detached list
    results sorted with `StringComparer.Ordinal`, and do tests include non-BMP
    identifiers?
13. Can raw/rejected Conversation or Task content reach JSON parameters, logs,
    diagnostics, or exceptions?
14. Does the provider re-sanitize Context/Candidate/Memory content?
15. Are structured columns checked against JSON and recomputed state hashes?
16. Are Promote/Reject/Supersede/Archive expectation reads and writes inside a
    provider-owned top-level transaction that rejects any pre-existing ambient
    boundary before mutation?
17. Can #56 publish committed Accountability before that transaction commits?
18. Can Supersede expose a partial or non-reciprocal graph?
19. Can Npgsql unavailability or commit-unknown become an
    `AgentMemoryOperationException`?
20. Does any Store call a Memory Accountability producer, Recorder, or Sink?
21. Did SQL absorb confidence ranking, visibility, budgets, or pack hashing?
22. Did anyone invent a Stale state or TTL heuristic?
23. Does the schema metadata and validator enforce both exact FK delete action
    and column collation?
24. Does the base PostgreSQL extension build with no Agent Memory runtime or
    Store dependency?
25. Does only the explicit Agent Memory provider extension replace the four
    Stores in either registration order?
26. Are conditional/capability interfaces discovered by casting the selected
    `IAgentMemoryStore`, with no divergent DI registrations?
27. Does restart preserve Recall/Source Expansion/#56 behavior?
28. Does every persisted JSON path use an exact generated `JsonTypeInfo`?
29. Do crash tests kill a process and wait for backend exit?
30. Did the original linked native executable run the durable Memory mainline?

---

## 22. Exit Criteria

Phase 9b+ Durable Agent Memory Store Provider is complete only when:

- the feature-neutral base PostgreSQL registration validates without Agent
  Memory, while the explicit Agent Memory provider registration resolves all
  four Store contracts to PostgreSQL implementations in either feature order;
- InMemory and PostgreSQL pass the same semantic Store/curation contract cases;
- Conversation/Task sanitizer boundaries and canonical-artifact non-resanitize
  boundaries are executable and raw rejected content is absent from persistence;
- tenant isolation, snapshot safety, exact sequence, tenant-wide Block identity,
  parent-first context replacement, parent-validated Block lookup, and .NET
  ordinal order including non-BMP IDs pass after fresh-process restart;
- Candidate/Memory state hashes are computed by the shared hash projector, and
  Promotion Service plus both Stores obtain lifecycle/graph snapshots from one
  shared curation projector and state machine;
- `SaveMemoryAsync` admits only a new Active, non-authoritative, unlinked
  snapshot or an exact replay and cannot bypass lifecycle, authority, or graph
  immutability;
- direct Memory save cannot create one-sided graph links; Supersede remains the
  only graph-creation mainline;
- Promote, Reject, Supersede, and Archive are atomically conditional and the
  selected Store truthfully reports `ConfirmedAtomic`;
- every formal curation call owns its top-level commit boundary, rejects an
  existing ambient Runtime transaction before mutation, and cannot let #56
  publish committed Accountability before durable commit;
- concurrent Task appends do not lose committed Events and concurrent curation
  produces deterministic winners with no partial graph;
- crash-before-commit exposes no mutation and crash-after-commit is durable;
- deterministic domain conflict, persistence unavailable, commit unknown, and
  persisted corruption remain distinguishable;
- V010 apply/validation/reapply/checksum/shape evidence, including exact column
  collation and foreign-key delete action, is green;
- existing Retriever, Source Expander/ReadCore, and #56 Accountability behavior
  is unchanged when the Store is swapped;
- no Store-owned Accountability, recall algorithm, Stale lifecycle, retention,
  vector index, second provider, or outbox exists;
- dependency boundaries and the canonical solution build are green;
- the original linux-x64 NativeAOT executable prints
  `CRESTCREATES_DURABLE_AGENT_MEMORY_OK` after executing the durable mainline;
- `memory.md` is updated only with evidence actually executed, without claiming
  reliable delivery, mutation/audit atomicity, automatic replay, or a second
  database provider.

The closure proof is two end-to-end mainlines:

```text
Conversation / Task
    -> Store-owned sanitization
    -> PostgreSQL durable snapshot
    -> process restart
    -> Context / Source Expansion
    -> same safe content and sequence
```

and:

```text
Candidate
    -> shared domain curation projection
    -> PostgreSQL atomic graph commit
    -> #56 post-result Accountability
    -> process restart
    -> Recall
    -> same visible set, deterministic Pack, and canonical hashes
```

If either mainline cannot run as automated evidence, #55 is not closed.
