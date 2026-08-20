# Phase 9b Closure Review — Durable Persistence Foundation

**Review date:** 2026-08-20  
**Reviewed revision:** `codex/phase9b-closure-fixes` (local PR head)  
**Review type:** closure implementation review followed by a Phase 9b freeze review  
**Primary scope:** #24/#70/#73/#56/#55/#69, with #39/#43 as foundations and #25/#26/#68 as deferred boundaries

---

## A. Executive Judgment

```text
Phase 9b Closure Status: APPROVED_WITH_NON_BLOCKING_GAPS

P0: 0
P1: 0
P2: 1
P3: 0
```

Phase 9b has a strong executable durability foundation. The production Workflow/HumanTask transaction path, restart and subprocess-crash recovery, durable Agent Tool reconciliation, Agent Memory durability/accountability, Control Plane/reference-data durability, migration drift detection, and real PostgreSQL NativeAOT publish-link-run path all have verified evidence. The later Phase 9b+ work also reuses one PostgreSQL provider kernel instead of creating domain-local transaction or migration frameworks.

The two P1 closure blockers from the previous review are closed by this patch:

1. **P1-01 — DescriptorSnapshot identity:** a provider-neutral canonical writer now hashes every persisted top-level, entry and relationship field, normalizes collection order, and is consumed by both providers. Acceptance cases cover field mutation and permutation.
2. **P1-02 — Runtime parity kit/order:** the shared Runtime kit now runs Snapshot, Workflow, HumanTask, transaction and ordering cases through both providers; PostgreSQL also wraps the shared AuditSink cases. HumanTask providers now both order pending results by `CreatedAt`, then ordinal `InstanceId`.

The remaining P2 is the explicitly deferred #25 same-transaction enlistment probe. It is non-blocking because the coordinator already enlists multiple Runtime stores and #25 remains the owner of reliable delivery semantics; this patch does not implement Outbox behavior.

### Evidence confidence

| Evidence class | Result |
|---|---|
| Claimed evidence | PR descriptions for [#71](https://github.com/OrchesAdam/CrestCreates/pull/71), [#72](https://github.com/OrchesAdam/CrestCreates/pull/72), [#74](https://github.com/OrchesAdam/CrestCreates/pull/74), [#75](https://github.com/OrchesAdam/CrestCreates/pull/75), [#77](https://github.com/OrchesAdam/CrestCreates/pull/77), and [#78](https://github.com/OrchesAdam/CrestCreates/pull/78) were used only as navigation. |
| Verified locally | Canonical solution build; focused Runtime/Workflow/HumanTask/Agent Tool/Agent Memory/Accountability/Control Plane suites; full real PostgreSQL suite; dependency boundaries; exact evidence ledger; native publish-link-run fixture. |
| Verified from CI | Latest reviewed head passed the direct Npgsql PostgreSQL and NativeAOT gates in [run 32269447220](https://github.com/OrchesAdam/CrestCreates/actions/runs/32269447220). Earlier delivery-head CI runs were also successful. |
| Missing evidence | Only the Spec-named same-transaction #25 enlistment probe remains; it is recorded as a non-blocking deferred seam. |

### Local verification record

The first Testcontainers attempt could not connect to `/var/run/docker.sock`; this was a local runner configuration failure, not counted as product evidence. Re-running against the host's rootless Podman socket with `DOCKER_HOST=unix:///run/user/1000/podman/podman.sock` produced the results below.

| Verification | Result |
|---|---|
| `dotnet build CrestCreates.slnx -c Release --no-restore` | PASS, 0 errors (291 existing warnings) |
| Runtime InMemory provider / shared kit | PASS: 19 tests, including shared Snapshot/Workflow/HumanTask/transaction cases |
| Agent Tools | PASS: 397 |
| Agent Memory / Memory Accountability | PASS: 221 / 114 |
| Descriptor Draft and Organization/DataPermission-related focused suites | PASS: 124 / 181 |
| Real PostgreSQL integration suite | PASS: 393/393 |
| PostgreSQL shared AuditSink contract wrapper | PASS: 1/1 |
| Dependency/architecture boundaries | PASS: 161/161 |
| Source-discovered reference-data evidence ledger gate | PASS: 14/14 |
| PostgreSQL NativeAOT fixture | PASS: 1/1; native link and original native executable ran against real PostgreSQL |

The CI workflow makes both PostgreSQL tests and the native fixture mandatory, rather than treating them as optional analyzer checks ([`ci.yml`](../../.github/workflows/ci.yml#L119-L125)).

### Delivery lineage checked

| Issue / delivery | Repository state and external delivery evidence | Review use |
|---|---|---|
| [#39](https://github.com/OrchesAdam/CrestCreates/issues/39) / [PR #67](https://github.com/OrchesAdam/CrestCreates/pull/67) | Closed/merged; delivery-head [CI passed](https://github.com/OrchesAdam/CrestCreates/actions/runs/30596301351) | Accountability foundation |
| [#43](https://github.com/OrchesAdam/CrestCreates/issues/43) | Closed; current code/tests and `memory.md` were inspected rather than treating the issue text as proof | Agent Memory first-closure foundation |
| [#24](https://github.com/OrchesAdam/CrestCreates/issues/24) / [PR #71](https://github.com/OrchesAdam/CrestCreates/pull/71) | Closed/merged; delivery-head [CI passed](https://github.com/OrchesAdam/CrestCreates/actions/runs/30702612772) | Base provider kernel and the two P1 closure findings |
| [#70](https://github.com/OrchesAdam/CrestCreates/issues/70) / [PR #72](https://github.com/OrchesAdam/CrestCreates/pull/72) | Closed/merged; delivery-head [CI passed](https://github.com/OrchesAdam/CrestCreates/actions/runs/31013556639) | Agent Tool durable reconciliation |
| [#73](https://github.com/OrchesAdam/CrestCreates/issues/73) / [PR #74](https://github.com/OrchesAdam/CrestCreates/pull/74) | Closed/merged; delivery-head [CI passed](https://github.com/OrchesAdam/CrestCreates/actions/runs/31351665642) | Protocol consolidation/remediation |
| [#56](https://github.com/OrchesAdam/CrestCreates/issues/56) / [PR #75](https://github.com/OrchesAdam/CrestCreates/pull/75) | Closed/merged; delivery-head [CI passed](https://github.com/OrchesAdam/CrestCreates/actions/runs/31664745786) | Memory accountability composition |
| [#55](https://github.com/OrchesAdam/CrestCreates/issues/55) / [PR #77](https://github.com/OrchesAdam/CrestCreates/pull/77) | Closed/merged; delivery-head [CI passed](https://github.com/OrchesAdam/CrestCreates/actions/runs/31943226680) | Durable Memory provider |
| [#69](https://github.com/OrchesAdam/CrestCreates/issues/69) / [PR #78](https://github.com/OrchesAdam/CrestCreates/pull/78) | Closed/merged on reviewed `master`; [CI passed](https://github.com/OrchesAdam/CrestCreates/actions/runs/32269447220) | V011 Control Plane/reference data closure |
| [#25](https://github.com/OrchesAdam/CrestCreates/issues/25), [#26](https://github.com/OrchesAdam/CrestCreates/issues/26), [#68](https://github.com/OrchesAdam/CrestCreates/issues/68) | Open at review time | Confirmed deferred boundary; not counted as Phase 9b missing features |

---

## B. Final Delivered Boundary

This is the surface actually delivered on the reviewed `master`, not the initial roadmap.

| Domain | Durable/semantic surface | InMemory | PostgreSQL / migration | Strongest verified evidence |
|---|---|---|---|---|
| Runtime state | `IRuntimeTransactionCoordinator`, registered `RuntimeStateValue`, Workflow instance Add/CAS/read, HumanTask Add/CAS/query, immutable suspension receipt, DescriptorSnapshot evidence | Full semantic provider | V001–V006 | Atomic suspension, rollback, response-loss reconciliation, restart, subprocess crash, AOT |
| Accountability | `IAuditRecorder` pipeline and durable `IAuditSink` acceptance/duplicate/conflict | InMemory sink | V003 sink | validation/sanitization/integrity tests, PostgreSQL restart/duplicate/conflict, AOT retry |
| Agent Tool pre-dispatch | governance audit, budget reservation/finalization, invocation gate/lease, observation/receipt, reconciliation and cleanup | semantic participants | V007–V009 | five crash windows, CAS/ownership fencing, acknowledgment-loss, restart, AOT markers |
| Agent Memory accountability | stable semantic operation identity and typed facts recorded after effective caller-visible result | same producer | durable audit sink composition | accepted/duplicate/conflict, no false deterministic fact on unavailable/commit-unknown |
| Agent Memory | Conversation, Task History, Compressed Context/Block, Candidate/Memory, conditional curation | four store contracts with shared projectors/state machine | V010 | shared cases, tenant isolation, sanitization, graph integrity, concurrency, restart, crash, AOT |
| Control Plane/reference data | DescriptorDraft, Organization units/positions/memberships/roles, DataPermission scopes/rules | shared semantic cases | V011 | restart, six-surface crash matrix, concurrent blind replacement, corruption, schema, AOT |
| Provider infrastructure | one `NpgsqlDataSource`, ambient session/accessor, transaction coordinator, migration catalog/runner, schema validator, generated JSON contexts | one in-process coordinator/state | same PostgreSQL kernel for all rows above | boundary tests, migration drift tests, real database suite, AOT |

Not delivered by Phase 9b: an Outbox, reliable event delivery, cache freshness/version propagation, second database provider, EF Core provider, distributed exactly-once semantics, retention/compliance product, or general repository/UoW replacement.

---

## C. Canonical Ownership Map

| Concern | Canonical owner | Persistence authority limit |
|---|---|---|
| Workflow state transitions and recovery meaning | Workflow Runtime | Provider stores/CASes state; it does not invent a transition. |
| HumanTask lifecycle and assignee/correlation semantics | HumanTask Runtime | Provider enforces durable identity/constraints without becoming workflow authority. |
| Runtime tenant key, state envelope, transaction/failure contracts | Runtime Persistence Abstractions | No Npgsql types escape these contracts. |
| SQL, sessions, migrations, schema validation, provider exception mapping | PostgreSQL provider kernel | Mechanics only; domain semantics stay in contracts/shared projectors. |
| Audit fact, validation, sanitization, integrity, acceptance semantics | Accountability | Durable sink is evidence storage, never Runtime authority. |
| Agent Tool governance/budget/gate/recovery policy | Agent Tool Runtime | PostgreSQL owns durable compare-and-set mechanics, not recovery policy. |
| Agent Memory lifecycle, authority, projectors, curation state machine | Agent Memory | Persistence never promotes Memory to authoritative truth. |
| DescriptorDraft/Organization/DataPermission meaning | Their domain contracts | V011 stores snapshots and rejects corruption; it does not join Workflow recovery. |
| Reliable delivery | **#25, not Phase 9b** | Audit acceptance/state commit does not imply external delivery. |
| Cache freshness/versioning | **#26, not Phase 9b** | No provider-side hidden cache-consistency protocol. |
| Reusable evidence automation | **#68 candidate only** | This review records candidates and does not create a Harness. |

The base ownership rule is also explicit in the Phase 9b Spec ([§5.1](../superpowers/specs/2026-07-31-phase-9b-durable-persistence-foundation-design.md#51-contract-ownership)).

---

## D. Permanent Invariant Review

### Persistence ownership

| Invariant | Status | Verified evidence |
|---|---|---|
| INV-P01 Provider details do not leak into core abstractions | PASS | Runtime contracts expose provider-neutral keys, errors, state and transaction interfaces. Boundary suite passes; provider registration contains Npgsql mechanics ([registration](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlRuntimePersistenceServiceCollectionExtensions.cs#L17-L37)). |
| INV-P02 One reusable durable-provider kernel | PASS | One data source, accessor, coordinator and migration runner are registered by the base provider. Memory only replaces four stores; V011 explicitly requires the complete base marker/kernel ([Memory DI](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlAgentMemoryPersistenceServiceCollectionExtensions.cs#L15-L29), [V011 DI](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlControlPlaneReferenceDataPersistenceServiceCollectionExtensions.cs#L16-L38)). |
| INV-P03 Core contracts define semantics | PASS | `DescriptorSnapshotPersistenceHasher` owns the canonical full-content projection; both providers consume its digest. |

### Authority

| Invariant | Status | Verified evidence |
|---|---|---|
| INV-A01 Persistence does not make Agent Memory authoritative | PASS | Lifecycle/authority decisions remain in shared Agent Memory projector/state-machine code; PostgreSQL implements store contracts. Shared cases and restart/curation tests pass. |
| INV-A02 Accountability facts do not become Runtime authority | PASS | Recorder/sink produces durable evidence; Workflow/Tool/Memory decisions do not read audit facts to authorize state transitions. |
| INV-A03 V011 durability does not expand Workflow/HumanTask recovery authority | PASS | Reference-data writes use the provider-owned top-level boundary and reject ambient Runtime transactions; they are not participants in suspension recovery ([coordinator](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlRuntimeTransactionCoordinator.cs#L37-L56)). |
| INV-A04 No hidden Outbox/cache protocol | PASS | No delivery dispatcher/cache version protocol is registered; #25/#26 remain open and explicit. |

### Identity and isolation

| Invariant | Status | Verified evidence |
|---|---|---|
| INV-I01 Tenant participates in durable identity where required | PASS | Provider keys and composite schema constraints include tenant scope; same-ID/different-tenant cases pass across Runtime, Tool, Memory and reference data. |
| INV-I02 Same logical IDs do not collide/leak across tenants | PASS | Shared Memory cases, PostgreSQL integration cases, and V011 exact-tenant cases pass. |
| INV-I03 Stable semantic identities survive restart/retry | PASS | Suspension OperationId/receipt, Tool attempt identity, Memory operation identity and reference-data keys are verified across fresh providers. |

### Mutation and concurrency

| Invariant | Status | Verified evidence |
|---|---|---|
| INV-C01 Authoritative mutable state has explicit concurrency semantics | PASS WITH NOTE | Workflow/HumanTask revision CAS, Tool gate/fence, and Memory conditional curation are explicit. #69 intentionally specifies blind whole-snapshot replacement rather than stale-write rejection; concurrent tests prove a complete last committed snapshot, not CAS. |
| INV-C02 Stale writers cannot partially mutate committed state | PASS | Runtime rollback, Memory atomic curation/batch and Agent Tool settlement tests pass. |
| INV-C03 CAS/fencing has one observable winner | PASS | Agent Tool ownership-fence and Memory concurrent curation tests assert exactly one valid winner. |
| INV-C04 Replay distinguishes identical from conflicting mutation where required | PASS | Receipts, Audit sink, Agent Tool terminal receipt and Memory exact replay/conflict cases pass. The DescriptorSnapshot exception is separately recorded under INV-P03/D02. |

### Snapshot and serialization

| Invariant | Status | Verified evidence |
|---|---|---|
| INV-S01 Snapshot-on-read/write equivalence | PASS | Acceptance matrix covers every persisted Snapshot field plus descriptor/relationship permutation across both providers. |
| INV-S02 Explicit/source-generated JSON contracts | PASS | Provider-owned generated serializer contexts cover Runtime, Tool, Memory and reference data; serializer coverage gates pass. |
| INV-S03 No reflection fallback on owned AOT paths | PASS | Real publish-link-run fixture executes the provider and generated JSON paths. |
| INV-S04 Corruption fails explicitly | PASS | Invalid JSON/enums/references/structured-column disagreement and migration/schema corruption produce typed fail-closed outcomes. |

### Transactions and crash

| Invariant | Status | Verified evidence |
|---|---|---|
| INV-T01 Provider-owned boundaries are explicit | PASS | Nested store calls reuse the ambient provider session; top-level formal curation refuses ambient scope ([coordinator](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlRuntimeTransactionCoordinator.cs#L25-L56)). |
| INV-T02 Crash before commit exposes no partial state | PASS | Independent worker tests cover suspension, Memory curation, Agent Tool windows and all six V011 save surfaces. |
| INV-T03 Crash after commit recovers durable authority | PASS | Fresh-process/provider reads and receipt-based reconciliation pass. |
| INV-T04 Ambient behavior is explicit | PASS | Concurrent ambient use and unsupported top-level nesting fail with provider-neutral contract codes. |
| INV-T05 No reliable-delivery claim | PASS | Audit/state persistence is intentionally bounded; #25 remains the owner of reliable state-to-event delivery. |

### Migration

| Invariant | Status | Verified evidence |
|---|---|---|
| INV-M01 Forward/repeat safe | PASS | One catalog from V001 through V011, advisory lock, history/checksum, apply/reapply tests. |
| INV-M02 Incompatible drift detected | PASS | Missing/extra/wrong constraints, indexes, columns, FKs and checksum drift are executable failure cases. |
| INV-M03 Extensions do not duplicate migration infrastructure | PASS | V007–V011 reside in the same runner/catalog; Memory and reference-data DI register no runner. |

### Determinism

| Invariant | Status | Verified evidence |
|---|---|---|
| INV-D01 Observable ordering is deterministic | PASS | Shared HumanTask case freezes `CreatedAt`, then ordinal `InstanceId`; both providers pass it. |
| INV-D02 Canonical/state hashes use stable semantic projections | PASS | `DescriptorSnapshotPersistenceHasher` is an explicit fixed-order canonical writer with normalized collections; providers no longer hash JSON text. |
| INV-D03 InMemory/PostgreSQL runners agree | PASS | Shared Runtime kit and shared AuditSink wrapper execute through both provider runners; full PostgreSQL suite passes. |

### NativeAOT

| Invariant | Status | Verified evidence |
|---|---|---|
| INV-N01 Production durable mainlines remain NativeAOT-compatible | PASS | Native host includes Runtime, audit, Tool, Memory and V011 representative paths. |
| INV-N02 Evidence is publish + link + run | PASS | Fixture publishes `linux-x64`, executes the original native binary and asserts output markers; local 1/1 and CI pass. |
| INV-N03 Representative real-provider paths execute natively | PASS | Markers include suspension/state/pin recovery/audit retry, five Tool crash windows, durable Memory, Workflow/HumanTask/subworkflow reference data and Organization/rule paths ([native host markers](../../tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/Program.cs#L134-L163)). |

---

## E. Mainline Evidence Matrix

Correctness levels are cumulative: L1 contract, L2 component, L3 composition, L4 production mainline, L5 failure/recovery.

| Mainline | Production path | Contract/composition evidence | L5 evidence | PostgreSQL | NativeAOT | Judgment |
|---|---|---|---|---|---|---|
| Runtime durable | `WorkflowExecutionRunner` → `WorkflowSuspensionCommitter` → coordinator → receipt + HumanTask Add + Workflow CAS → registry/pin recovery | Runtime shared Snapshot/Workflow/HumanTask/transaction cases plus PostgreSQL integration | rollback, worker crash between writes, commit-response loss, restart, pin mismatch | Real DB | Yes | L4/L5 PASS |
| Accountability | producer → `DefaultAuditRecorder` → validate → sanitize → integrity → `PostgreSqlAuditSink` | Accountability shared cases for InMemory and PostgreSQL wrapper | duplicate/conflict across restart and native retry | Real DB | Yes | L4/L5 PASS |
| Agent Tool pre-dispatch | invoker coordinator → governance/budget/gate/checkpoint → recovery policy → claim/fenced settlement → terminal receipt | runtime matrix plus PostgreSQL composition | five crash windows, response loss, CAS loser, live-invoker/reconciler ownership fence, restart | Real DB | Yes | L4/L5 PASS |
| Memory accountability | effective Memory result → stable OperationId/typed fact → `IAuditRecorder` → durable sink | producer and PostgreSQL composition tests | recorder failure preserves Memory result; unavailable/commit-unknown creates no false deterministic fact | Real DB | Indirect sink/native Memory | L4/L5 PASS within non-atomic delivery boundary |
| Durable Agent Memory | stores → shared sanitizer/projectors/comparer/state machine → PostgreSQL stores → recall/expansion/curation | same shared case methods run by InMemory and PostgreSQL | restart, concurrent winner, crash before/after curation, corruption, unavailable/commit-unknown | Real DB | Yes | L4/L5 PASS |
| Control Plane/reference data | domain contracts → base-first feature DI → top-level provider transaction → V011 stores → fresh provider | shared cases run against InMemory and PostgreSQL; 77-case evidence ledger | six-surface crash before/after/unknown, concurrent writers, restart, corruption, schema drift | Real DB | Yes | L4/L5 PASS; transaction boundary deliberately separate from Runtime recovery |

The Runtime atomic write order is visible in production code: receipt acceptance, HumanTask Add and Workflow revision update all occur inside the same coordinator call ([`WorkflowSuspensionCommitter`](../../src/Runtime/Workflow/CrestCreates.Workflow/WorkflowSuspensionCommitter.cs#L58-L84)). The PostgreSQL coordinator maps ambiguous commit acknowledgement to `RuntimeTransactionCommitUnknownException` rather than falsely claiming rollback ([coordinator](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlRuntimeTransactionCoordinator.cs#L58-L99)).

---

## F. Failure / Recovery Matrix

| Case | Verified executable evidence | Result / boundary |
|---|---|---|
| Crash before commit | Runtime crash worker; `CrashBeforeCurationCommit`; Agent Tool pre-checkpoint windows; V011 six-surface theory | PASS: no partial committed effect |
| Crash after commit | fresh process/provider for Runtime, Memory and V011; Agent Tool post-checkpoint windows | PASS: complete durable effect remains visible |
| Concurrent writers | Workflow/HumanTask CAS, Tool races, Memory append/curation, V011 blind replacement | PASS: one CAS/fence winner or one complete last-committed snapshot according to contract |
| CAS loser | Runtime stale revision; Agent Tool observation/result CAS; Memory conditional curation | PASS: loser cannot partially mutate authority |
| Fencing loser | Agent Tool live invoker versus reconciler and two-reconciler ownership cases | PASS: gate claim/fence is the authority |
| Acknowledgement/response loss | suspension receipt reconciliation; Agent Tool settlement; Memory/V011 commit-unknown taxonomy | PASS: unknown is preserved, no false rollback/deterministic fact |
| Restart | fresh provider/process cases for all durable domains | PASS |
| Corruption | malformed JSON, invalid enum/reference, structured-column mismatch, graph corruption | PASS: explicit typed/fail-closed behavior |
| Migration drift | checksum, history and exact schema manifest/constraint/index/FK cases | PASS |
| Provider unavailable | invalid/unreachable PostgreSQL cases | PASS: provider-neutral unavailable classification |
| Ambient transaction misuse | concurrent ambient use and V011/curation top-level rejection | PASS |
| Transaction interruption | cancellation-before-write, rollback and subprocess termination | PASS |
| Same ID/different tenant | shared Memory/reference-data and PostgreSQL Runtime cases | PASS: no collision/leak |
| Exact retry/replay | receipt, Audit, Tool, Memory | PASS, except DescriptorSnapshot canonical identity (P1-01) |

---

## G. Provider Kernel Reuse Judgment

**Judgment: PASS — #55, #69 and Agent Tool durability reuse the #24 PostgreSQL kernel. The repository is not forming independent persistence subsystems per domain.**

Concrete basis:

1. Base DI creates exactly one `NpgsqlDataSource`, `PostgreSqlRuntimeTransactionAccessor`, `PostgreSqlRuntimeTransactionCoordinator`, `IRuntimeTransactionCoordinator`, migration runner and hosted compatibility validator.
2. Agent Tool durable participants are registered by that base extension and use the same coordinator/session conventions; V007–V009 live in the same migration catalog.
3. #55's feature extension only replaces the four Agent Memory store contracts. It does not register another data source, transaction coordinator, accessor, migration runner or failure hierarchy. V010 is in the base catalog.
4. #69's feature extension refuses partial or standalone composition unless the complete base provider marker/kernel is already registered. Its stores use `ExecuteTopLevelAsync` to preserve their declared commit boundary, not a second transaction model. V011 is in the same catalog.
5. The provider project has no EF Core dependency, and dependency boundaries prevent Runtime/Memory/Control Plane core projects from referencing Npgsql/provider implementation types.

`PostgreSqlAuditSink` and the migration runner open provider-owned connections for their separately declared acceptance/apply boundaries. They are not competing general transaction frameworks. Audit-to-state reliable atomic delivery is deliberately left to #25.

---

## H. InMemory vs PostgreSQL Parity

### Shared semantic kits actually present

| Surface | Shared provider-neutral cases | InMemory wrapper | PostgreSQL wrapper | Judgment |
|---|---|---|---|---|
| Agent Memory | `CrestCreates.Agent.Memory.Persistence.Testing` cases/projectors/assertions | Yes | Yes | PASS |
| DescriptorDraft/Organization/DataPermission | shared static case/evidence ledger | Yes | Yes | PASS |
| Agent Tool governance/reconciliation | provider-neutral Runtime matrix plus provider integration/ownership cases | Semantic participants | Yes | PASS for reviewed cases |
| Accountability sink | `CrestCreates.Accountability.Testing/Sinks/AuditSinkContractCases` | Yes | Yes — `SharedAuditSinkContractKit_ShouldPass` | PASS |
| Base Workflow/HumanTask/Snapshot/Transaction | `CrestCreates.Runtime.Persistence.Testing/Cases/RuntimePersistenceContractCases` | Yes — `SharedRuntimeContractKit_ShouldPass` | Yes — same shared cases | PASS |

The base shared project now contains provider-neutral driver contracts, assertions and executable Snapshot/Workflow/HumanTask/transaction cases ([cases](../../tests/Shared/CrestCreates.Runtime.Persistence.Testing/Cases/RuntimePersistenceContractCases.cs)). Provider-local projects only supply lifecycle adapters; they do not redefine the semantic assertions.

### Confirmed provider-specific semantics

#### Closed P1-01: DescriptorSnapshot immutable identity

The normative contract is:

```text
Duplicate = exact same full persisted snapshot content
Conflict  = same SnapshotId, different persisted content
Hash      = dedicated canonical writer over all fields/entries/relationships,
            with normalized collection order; not ordinary JSON text equality
```

Source: [Spec §7.1](../superpowers/specs/2026-07-31-phase-9b-durable-persistence-foundation-design.md#71-contract).

The shared `DescriptorSnapshotPersistenceHasher` now writes the frozen projection ([hasher](../../src/Metadata/CrestCreates.Metadata.Abstractions/Persistence/DescriptorSnapshotPersistenceHasher.cs)), and both stores compare its digest ([InMemory](../../src/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory/Stores/InMemoryDescriptorSnapshotStore.cs#L8-L39), [PostgreSQL](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlDescriptorSnapshotStore.cs#L8-L78)). Acceptance cases cover all persisted field classes and collection permutation through both runners.

#### Closed P1-02: HumanTask observable order and shared parity

- InMemory pending queries now order by `CreatedAt`, then ordinal `InstanceId` ([implementation](../../src/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory/Stores/InMemoryHumanTaskInstanceStore.cs#L15-L21)).
- PostgreSQL uses the same ordering ([implementation](../../src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/PostgreSqlHumanTaskInstanceStore.cs#L100-L124)).
- The shared Runtime kit and PostgreSQL AuditSink wrapper execute in the provider suites ([InMemory test](../../tests/Runtime/Persistence/CrestCreates.Runtime.Persistence.InMemory.Tests/InMemoryRuntimeProviderTests.cs#L24-L32), [PostgreSQL tests](../../tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/PostgreSqlRuntimeIntegrationTests.cs#L347-L374)).

The contract is now frozen as `CreatedAt`, then ordinal `InstanceId`; both providers pass the same observable case.

### Parity conclusion

Provider parity is now executable for the reviewed base Runtime surfaces. The only remaining gap is the explicitly deferred #25 enlistment probe, not a provider divergence or a Phase 9b freeze blocker.

---

## I. Deferred vs Missing

| Correctly deferred | Why it is not a Phase 9b defect |
|---|---|
| #25 Transactional Outbox/reliable state-to-event delivery | Phase 9b persists state and audit acceptance but does not claim dispatcher, retry/DLQ, broker delivery, inbox or exactly-once. This boundary is explicit and preserved. |
| #26 Versioned cache consistency | No multi-instance cache-freshness claim exists in Phase 9b, and no hidden provider cache protocol was found. |
| Second database provider / EF Core | Explicitly out of scope; direct Npgsql is the verified production provider. |
| Distributed exactly-once, saga, remote synchronization | Not implied by local PostgreSQL atomicity/recovery. |
| Retention/WORM/compliance UI/encryption-envelope platform | Product/governance scope beyond the persistence foundation. |
| #68 Engineering Harness productization | Reusable checks are candidates only; no harness is required to establish the present result. |

| Actual missing evidence/defect | Severity | Freeze effect |
|---|---|---|
| Canonical full-content DescriptorSnapshot persistence identity was absent and provider behavior differed | Closed P1 | Closed by canonical hasher plus both-provider acceptance matrix |
| Base Runtime shared runners/Audit wrapper were absent; HumanTask ordering differed | Closed P1 | Closed by shared Runtime/Audit cases and `CreatedAt`, then `InstanceId` ordering |
| Spec-named neutral same-transaction enlistment probe (`OutboxStore_ShouldBeAbleToEnlistWithoutProviderLeak`, commit and rollback cases) was not found | P2 | Non-blocking by itself: the coordinator demonstrably enlists multiple Runtime stores, but the promised explicit future-#25 seam evidence should be added without implementing Outbox |

---

## J. Escaped / Late Findings

These are learning inputs for future Design Cases, not attribution of blame.

| Delivery | Finding exposed late | What should become an earlier case |
|---|---|---|
| #70 / PR #72 | full budget semantics, authoritative-missing versus unavailable, completion/release replay, commit-response loss | Recovery truth table before persistence implementation; no false rollback/absence claims |
| #70 remediation | live invoker versus reconciler ownership and fencing; `MarkIndeterminate` CAS | Explicit two-owner race matrix with one authoritative fence winner |
| #73 / PR #74 | null persisted `ReasonCode`, CAS-loser/TOCTOU consolidation | Persisted null/legacy semantic bridge and loser re-read cases |
| #55 / PR #77 | null persisted hashes, graph integrity, commit unknown, sanitization ordering | Full persisted-field closure, graph constraints, unknown-outcome taxonomy before provider coding |
| #69 / PR #78 | schema exactness, recursive payload closure, corruption taxonomy, evidence completeness | Exact schema manifest plus source-discovered case ledger at the beginning of the slice |
| This closure review | DescriptorSnapshot full-content canonical identity, HumanTask ordering and base runner skeleton | Mutation matrix for every persisted field, permutation invariance, and identical shared runners for every advertised provider |

The repeated pattern is that happy-path durability was usually present early; ambiguity appeared at semantic nulls, response loss, ownership races, schema exactness and “same identity, slightly different content.” Those belong in initial case matrices.

---

## K. Reusable Check Candidates for #68

No Harness is implemented here. Candidate ratings use Yes/Partial/No.

| Candidate | Repeated? | Deterministic? | Low noise? | Clearly owned? | Actionable? | Affordable? |
|---|---:|---:|---:|---:|---:|---:|
| Shared-provider contract completeness: every advertised provider wraps every required shared case | Yes | Yes | Yes | Yes — contract kit | Yes | Yes |
| Canonical immutable-identity mutation/permutation matrix | Yes | Yes | Yes | Yes — semantic owner | Yes | Yes |
| Exact migration/schema set and checksum drift guard | Yes | Yes | Yes | Yes — provider | Yes | Partial |
| Architecture guard: no provider dependencies in core; one kernel registration | Yes | Yes | Yes | Yes — boundaries | Yes | Yes |
| Source-generated JSON roots and reflection-fallback guard | Yes | Yes | Yes | Yes — provider/AOT | Yes | Yes |
| Crash-worker sentinel matrix: before/after commit plus commit-unknown | Yes | Yes | Partial | Yes — durable feature | Yes | Partial |
| Native marker manifest mapped to production scenarios | Yes | Yes | Yes | Yes — AOT fixture | Yes | Yes |
| Source-discovered evidence tuple ledger, as used by #55/#69 | Yes | Yes | Partial | Yes — feature/spec | Yes | Partial |
| Provider-unavailable/corruption failure taxonomy matrix | Yes | Yes | Yes | Yes — provider contracts | Yes | Yes |

The most immediate #68 seed candidate remains contract-kit completeness: the new shared ledger makes provider parity mechanically visible without encoding PostgreSQL behavior as the contract.

---

## L. Human Judgment Still Required

Tests and automation cannot finally decide:

1. Whether a persisted surface should be authority, evidence, cache, or delivery intent. That boundary requires domain judgment.
2. Whether a transaction boundary is appropriately broad. Atomicity can be tested after the boundary is chosen, but “should these facts commit together?” is a design decision.
3. Whether a semantic accountability fact should exist for a given result, especially an unknown persistence outcome.
4. Whether #69's blind whole-snapshot replacement remains the intended business contract; tests can only enforce the chosen rule.
5. Whether future reliable delivery/cache/multi-provider scope is proportionate. Touching persistence does not make it a Phase 9b backfill.
6. Whether a newly discovered issue is a confirmed escaped correctness/compatibility/security defect or simply a later feature request.

---

## M. Freeze Recommendation

**Phase 9b is approved for freeze with non-blocking gaps.** The two narrowly bounded P1 closure blockers are closed; the explicitly deferred #25 enlistment probe remains P2 and does not expand this patch into Outbox work.

### Required blocker closure

1. Keep the canonical Snapshot identity and shared provider cases as permanent gates.
2. Add the neutral same-transaction #25 enlistment probe in the later Outbox slice; do not implement Outbox as part of Phase 9b closure.
3. Preserve the verified build, full PostgreSQL, boundary and NativeAOT gates in the PR CI.

### Freeze statement to use only after the blockers pass

```text
Phase 9b is frozen.

Further changes require one of:
- confirmed Phase 9b escaped correctness defect
- compatibility defect
- security defect
- explicitly approved later-phase dependency change

New features must not be backfilled into Phase 9b merely because they touch persistence.
```

The applied follow-up was closure-only: contract cases first, minimum semantic correction second, no new durable surfaces, no provider-kernel rewrite, and no #25/#26 implementation.
