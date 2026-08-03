# Phase 9b+ Durable Agent Tool Pre-dispatch Reconciliation Implementation Plan

> Implement Issue #70 through ordered Case-first TDD slices. The approved Spec
> is normative; this Plan fixes file placement, dependency edges, migration
> ownership, test runners, crash/AOT evidence, and review gates without reopening
> the approved protocol.

**Goal:** Replace the volatile/response-only Agent Tool pre-dispatch path with
one recoverable mainline keyed by `LogicalInvocationKey + AttemptId`, backed by
Pending/Ready/Accepted fences, Attempt-idempotent budget reservation, complete
checkpoint comparison, durable PostgreSQL recovery, no-auto-dispatch
reconciliation, bounded retention, and original-binary NativeAOT evidence.

**Spec:** `docs/superpowers/specs/2026-08-03-phase-9bplus-durable-agent-tool-pre-dispatch-reconciliation-design.md`

**Issue:** #70

**Branch:** `70-phase-9b-durable-agent-tool-pre-dispatch-reconciliation`

**Spec status:** APPROVED

**Plan status:** APPROVED / Ready for Implementation

**Approval:** Public Contract PASS · Shared Semantics PASS · Persistence/Retention PASS · Normative Tests PASS · Slice Ownership PASS · Crash/JSON Evidence PASS · DI Ownership PASS

```text
Recovery identity:       LogicalInvocationKey + AttemptId
Dispatch mainline:       Pending -> Ready -> Accepted -> DispatchStarted
Restart policy:          reconcile only; never dispatch
Budget Missing:          authoritative once; interpreted with Gate state
Valid Budget Denied:     Attempt-scoped Abandoned receipt; later Acquire re-evaluates
StillPending:            mutable observation; never an immutable terminal receipt
PostgreSQL migration:    V007 (next catalog entry at Plan creation)
Provider registration:  complete Gate + Budget + Auditor participant set
NativeAOT evidence:      publish + native link + original binary + PostgreSQL
```

---

## 1. Execution Rules

- Run every shell command through `rtk`; use `apply_patch` for source/document
  edits.
- Before the first build/test command in an implementation session, run:

  ```bash
  rtk --version
  rtk dotnet --info
  rtk git status --short --branch
  ```

- Preserve unrelated worktree changes. Never edit an applied migration; append
  `V007` only after verifying `V006` is still the catalog tail.
- Never delete directly. Move retired files to
  `99_RecycleBin/Phase9bPlusAgentToolPreDispatch/` and update references.
- Begin each Slice with the named Red cases. A Red case must fail for the missing
  contract/behavior, not because the fixture cannot start.
- Make the smallest mainline change needed for Green, then run the focused test,
  the owning project, dependency boundaries, and `rtk git diff --check`.
- Do not retain an operational caller-generated `AuditId`, receipt-free dispatch
  overload, Record-only retry recovery identity, or volatile fallback after the
  Slice that cuts over its replacement.
- Do not add automatic Tool replay, Dispatcher access from the reconciler,
  exactly-once side-effect claims, Outbox delivery, Agent Memory durability, or
  Accountability-based authorization.
- Provider failure, timeout, cancellation, replica/cache miss, malformed result,
  and unknown schema never translate to authoritative `Missing`.
- No Runtime project references PostgreSQL/Npgsql. The PostgreSQL provider may
  reference Agent Tool Abstractions, never the concrete Agent Tools runtime.
- Durable JSON uses source-generated metadata only. Do not add runtime `Type`,
  `object?`, `Dictionary<string, object>`, dynamic Npgsql JSON mapping,
  `DefaultJsonTypeInfoResolver`, or warning suppression as a substitute.
- Do not update `memory.md` to Implemented/NativeAOT-verified until the final
  linked native binary executes successfully against PostgreSQL.

---

## 2. Ordered Delivery Map

| Slice | Deliverable | Required Red evidence | Must not include |
|---|---|---|---|
| 1 | Acceptance scaffold and public contract cutover | compile/architecture skeleton plus all Case IDs | provider implementation, SQL |
| 2 | Complete comparator and InMemory Auditor semantics | H01/H02/B01–B05/B11/F01–F06/F23/C07 | Gate/Invoker reordering, persisted-format cases |
| 3 | Pending/Ready/Accepted Gate, budget identity, Invoker cutover | H03–H05/H09/H10, B06/B07/B13–B15/B17, F07–F13/F25/F26/F29, C01/C02/C11 | cross-process dispatch |
| 4 | Default reconciler and terminal/observation semantics | H06–H08, B08/B12/B18, F14–F17/F27/F28/F30, C03 | PostgreSQL-specific orchestration |
| 5 | Complete PostgreSQL participants and crash recovery | F20/F21/F24, B03/C04/C05/C12 plus shared contract suite | cleanup policy, NativeAOT claim |
| 6 | Retention/cleanup and post-fact Accountability | B09/B10/B16/F18/F19/F22/C06 | reliable event delivery |
| 7 | Generated JSON, NativeAOT, full ledger and docs | C08/C09/C10 plus all regressions | scope expansion |

Each Slice is independently buildable and reviewable. Do not begin the next
Slice while a prior Slice has unresolved Red cases or Review guardrail failures.

---

## 3. Final Dependency and Project Graph

### 3.1 Production graph

```text
CrestCreates.Agent.Tools.Abstractions
    -> CrestCreates.Agent.Abstractions
    -> CrestCreates.Metadata.AgentTool.Abstractions
    -> CrestCreates.Schema.Abstractions

CrestCreates.Agent.Tools
    -> CrestCreates.Agent.Tools.Abstractions
    -> CrestCreates.Accountability.Abstractions
    -> existing Authorization/MultiTenancy/Metadata/Schema dependencies
    X  CrestCreates.Runtime.Persistence.PostgreSql

CrestCreates.Runtime.Persistence.PostgreSql
    -> CrestCreates.Agent.Tools.Abstractions
    -> existing Runtime.Persistence/Workflow/HumanTask/Accountability abstractions
    -> Npgsql
    X  CrestCreates.Agent.Tools

CrestCreates.Agent.Tools.Persistence.Testing
    -> CrestCreates.Agent.Tools.Abstractions
    X  test runner packages
    X  CrestCreates.Agent.Tools
    X  every provider
```

Agent Tool Abstractions owns the pure semantic projection, snapshot, and
comparer used by every implementation. The concrete Agent Tools runtime owns
orchestration, same-Worker continuation, reconciliation, and Accountability
projection. The provider owns durable rows, SQL/CAS, migrations, authoritative
reads, cleanup, and serialization DTOs. Neither concrete side reaches through
the other's boundary.

### 3.2 Project changes

Modify:

- `src/Runtime/Agent/CrestCreates.Agent.Tools/CrestCreates.Agent.Tools.csproj`
  to reference `CrestCreates.Accountability.Abstractions`.
- `src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql/CrestCreates.Runtime.Persistence.PostgreSql.csproj`
  to reference `CrestCreates.Agent.Tools.Abstractions`.
- Existing Agent Tool/PostgreSQL test, CrashWorker, AotHost, and AotFixture
  projects only with the references required by their concrete runners.

Create:

```text
tests/Shared/CrestCreates.Agent.Tools.Persistence.Testing/
  CrestCreates.Agent.Tools.Persistence.Testing.csproj
  Fixtures/AgentToolPreDispatchContractFixture.cs
  Drivers/IAgentToolPreDispatchContractDriver.cs
  Drivers/IDurableAgentToolPreDispatchContractDriver.cs
  Cases/AgentToolPreDispatchContractCases.cs
  Cases/AgentToolPreDispatchFenceContractCases.cs
  Cases/AgentToolPreDispatchReconciliationContractCases.cs
  Assertions/AgentToolPreDispatchContractAssertions.cs
  AgentToolPreDispatchContractTestBase.cs
  TestingBoundaryMarker.cs
```

Add this runner-free project to both `CrestCreates.slnx` and
`solutions/CrestCreates.All.slnx`. It has `IsTestProject=false`, references only
Agent Tool Abstractions, and contains no xUnit/Test SDK/provider/runtime
reference.

### 3.3 Composition rule

`AddCrestAgentTools()` may register development participants with `TryAdd*`.
`AddCrestCreatesPostgreSqlRuntimePersistence()` must use
`RemoveAll<TContract>()` followed by one explicit durable registration for each
contract below; it must not rely on call order or “last registration wins”:

```text
IAgentToolInvocationGate
IAgentToolInvocationLeaseAbandoner
IAgentToolBudgetGate
IAgentToolGovernanceAuditor
IAgentToolPreDispatchReconciliationStore
IAgentToolPreDispatchPersistenceCapabilities
```

A PostgreSQL composition must never leave any method of those three Phase 8f
participants delegated to a development InMemory type. A startup composition
test resolves both each single interface and `IEnumerable<TContract>` and
rejects mixed durable/volatile ownership. The test composes Agent Tools then
PostgreSQL and PostgreSQL then Agent Tools; both orders must resolve exactly one
durable owner for every participant. Any provider-owned hosted service resolves
the same durable instances through those contracts.
The reconciliation Store and capability interfaces live in Agent Tool
Abstractions. Cleanup remains provider-internal and is never exposed as a
Runtime business contract.

---

## 4. Public Contract Cutover

### 4.1 New contract files

Create under `CrestCreates.Agent.Tools.Abstractions`:

```text
Governance/AgentToolPreDispatchContracts.cs
Governance/AgentToolPreDispatchReconciliationContracts.cs
Invocation/AgentToolInvocationPreDispatchContracts.cs
```

Extend, rather than duplicate, the existing participant interfaces in:

```text
Governance/AgentToolGovernanceAuditContracts.cs
Governance/AgentToolBudgetContracts.cs
Invocation/AgentToolInvocationGateContracts.cs
```

The contract surface contains:

- `AgentToolPreDispatchIdentity(LogicalInvocationKey, AttemptId)`;
- immutable `AgentToolGovernancePreDispatchReceipt` with identity, provider
  `AuditId`, and first `AcceptedAt`;
- `AgentToolGovernancePreDispatchWriteResult`: Accepted/Duplicate carries only
  the exact Receipt; Conflict carries no Receipt and never carries a checkpoint;
- `AgentToolGovernancePreDispatchReadResult`: Accepted carries the exact Receipt
  plus a detached complete Checkpoint; Missing carries neither;
- `AgentToolInvocationPreDispatchState` with Pending, Ready, Accepted,
  DispatchStarted, Abandoned, ReleasePending, Released, CompletionPending,
  Completed, and Indeterminate;
- typed `AgentToolInvocationPreDispatchIntentSnapshot` and
  `AgentToolInvocationAbandonedReceipt` records;
- prepare-intent, reservation-bind, receipt-bind, state-read, denial-publication,
  and receipt-bound dispatch request/result records;
- budget read statuses `Missing`, `Reserved`, `Released`, `Committed`, and
  `Indeterminate`, with no provider-specific stronger absence state;
- `AgentToolPreDispatchReconciliationObservation`,
  `AgentToolPreDispatchReconciliationReceipt`, and
  `AgentToolPreDispatchReconciliationResult` contracts;
- `IAgentToolPreDispatchReconciliationStore` with read, mutable-observation CAS,
  and first-terminal-receipt CAS operations;
- `IAgentToolPreDispatchPersistenceCapabilities`, reporting FullSemantic
  InMemory versus FullDurable PostgreSQL support without coupling the generic
  Runtime Persistence capability contract to Agent Tool types;
- `IAgentToolPreDispatchReconciler` with only identity and cancellation input.

### 4.2 Signature rules

- Replace `AgentToolGovernanceAuditHandle` in the operational mainline with
  `AgentToolGovernancePreDispatchReceipt`; remove the old declaration only
  after all callers compile against the receipt. If this retires a standalone
  file, move that file to the recycle bin. Do not keep adapter overloads.
- `RecordPreDispatchAsync` returns
  `AgentToolGovernancePreDispatchWriteResult`; Accepted/Duplicate carries only
  `AgentToolGovernancePreDispatchReceipt`, and Conflict carries no Receipt.
- `GetPreDispatchStateAsync(identity)` returns
  `AgentToolGovernancePreDispatchReadResult`; only Accepted carries Receipt plus
  the detached complete Checkpoint. Record does not become a snapshot-read API.
- `BindAcceptedPreDispatchAsync` accepts only an exact Ready Attempt.
- `TryMarkDispatchStartedAsync` requires lease + exact receipt + exact
  ReservationId; remove the receipt-free overload.
- `ReserveAsync` is idempotent by identity plus complete request.
  `GetReservationStateAsync(identity)` is authoritative only when it completes
  successfully against the provider authority.
- A valid Denied result is published through one named Gate transition to an
  immutable Attempt-scoped Abandoned receipt. Repeating that transition returns
  the same receipt; changed denial content conflicts.
- `StillPending` updates mutable observation metadata only. Released,
  Conflict/terminal Indeterminate, and PostDispatchUnknown create at most one
  immutable terminal receipt. `AlreadyReleased` projects the existing Released
  receipt; Missing has no receipt.

### 4.3 Equality and snapshot ownership

Create in Agent Tool Abstractions:

```text
Governance/Semantics/AgentToolGovernancePreDispatchSemanticProjection.cs
Governance/Semantics/AgentToolGovernancePreDispatchSnapshot.cs
Governance/Semantics/AgentToolGovernancePreDispatchComparer.cs
```

These are contract-only, deterministic components: no DI, database, concrete
Runtime service, reflection, current clock, or provider dependency. The
projection enumerates every INV-04 field once; the snapshot deep-copies every
required and optional nested value; the comparer consumes those snapshots and
returns a stable mismatch classification. InMemory, Invoker/reconciler, and
PostgreSQL Auditor call this same comparer. A provider-private second comparer
is forbidden. Hashes may short-circuit obvious differences but are never the
conflict authority.

---

## 5. PostgreSQL V007 Design

Before implementation, assert that `PostgreSqlRuntimeMigrationRunner.Catalog`
still ends at V006. Append one immutable checksummed migration:

```text
V007 durable_agent_tool_pre_dispatch_reconciliation
```

### 5.1 Tables and keys

All logical/Attempt tables begin their primary/unique/FK identity with:

```text
tenant_scope_kind, tenant_id,
user_id, agent_id, execution_id, invocation_id,
attempt_id
```

Host scope uses the existing non-null Phase 9b representation. The migration
adds:

| Table | Purpose | Required constraints/indexes |
|---|---|---|
| `agent_tool_invocations` | fingerprint binding, logical state, fencing counter | logical-key PK; non-empty fingerprint; tenant-scope check |
| `agent_tool_attempts` | lease, frozen intent, mutable expiry, Pending/Ready/Accepted/terminal state | Attempt PK/FK; unique scoped LeaseId; positive fencing token/revision; state-shape checks |
| `agent_tool_budget_reservations` | one Attempt-idempotent reservation and terminal budget state | Attempt PK/FK; scoped unique ReservationId; fingerprint/state checks |
| `agent_tool_governance_checkpoints` | provider AuditId, first acceptance and complete generated-JSON checkpoint | Attempt PK/FK; scoped unique AuditId; non-null format/integrity |
| `agent_tool_governance_decisions` | immutable safe denial/indeterminate decision evidence | scoped logical-key + decision AttemptId PK; decision-shape check; no mandatory Gate Attempt FK |
| `agent_tool_pre_dispatch_reconciliation_observations` | mutable StillPending/last-observed metadata | Attempt PK/FK with `ON DELETE CASCADE`; positive revision; no terminal receipt columns |
| `agent_tool_pre_dispatch_reconciliation_receipts` | immutable terminal tombstone queryable after aggregate cleanup | exact scoped logical-key + AttemptId PK; no destructive Attempt FK; terminal status/time/integrity all required |

`agent_tool_attempts` stores LeaseId, FencingToken, AcquiredAt, frozen
PreparedExpiresAt, mutable CurrentExpiresAt, invocation fingerprint, intent JSON,
ReservationId, AuditId, DispatchStarted, denial/release/completion receipts,
state, revision, and timestamps. State checks prevent Ready without a bound
reservation, Accepted without reservation+AuditId, or Abandoned denial without
its stable receipt.

`RecordDecisionAsync` can run before a Gate Attempt exists (for example an
early governance denial using the existing decision AttemptId). Therefore the
decision table is identity-scoped and idempotent but must not require a foreign
key to `agent_tool_attempts`. Budget-denial decisions that do correspond to a
Pending Gate Attempt are validated compositionally by the provider/runtime
contracts, not by an invalid universal FK assumption.

Observation and terminal receipt lifecycles are intentionally different. A
live observation belongs to the Attempt aggregate and is removed with that
aggregate only after the aggregate is cleanup-eligible. The terminal receipt is
an independent identity-complete tombstone: it has no FK to the deletable
Attempt, survives aggregate cleanup through
`ReconciliationReceiptRetention`, and is deleted only by its own retention
policy. Reconciliation reads the terminal receipt before classifying a missing
Attempt so cleanup cannot manufacture a false first-time Missing result.

### 5.2 Provider files

Create:

```text
PostgreSqlAgentToolInvocationGate.cs
PostgreSqlAgentToolBudgetGate.cs
PostgreSqlAgentToolGovernanceAuditor.cs
PostgreSqlAgentToolPreDispatchReconciliationStore.cs
PostgreSqlAgentToolPreDispatchCleanup.cs
PostgreSqlAgentToolPersistenceSupport.cs
```

Modify:

```text
PostgreSqlRuntimeMigrationRunner.cs
PostgreSqlRuntimeJsonSerializerContext.cs
PostgreSqlRuntimePersistenceOptions.cs
PostgreSqlRuntimePersistenceOptionsValidator.cs
PostgreSqlRuntimeProviderCapabilities.cs
PostgreSqlRuntimePersistenceServiceCollectionExtensions.cs
PostgreSqlRuntimeSchemaCompatibilityHostedService.cs
```

The schema manifest validates all seven tables, exact columns, primary keys,
checks, indexes, and foreign keys. Validation-only mode fails on missing V007,
checksum drift, newer unknown migration, or incompatible shape.

### 5.3 SQL/CAS rules

- First write uses conditional insert. Duplicate/conflict classification reads
  and compares the stored complete snapshot under the same transaction/locking
  discipline.
- Gate transitions use `state + revision + scoped identity + fencing token` CAS.
  CAS loss triggers one authoritative reread/classification, never blind
  delegate replay.
- Budget reservation has one Attempt-scoped row. Same complete request returns
  the original ReservationId; a changed request conflicts.
- Cleanup uses one transaction and locks/CAS the aggregate after proving every
  linked state terminal and outside all retention windows.
- COMMIT acknowledgement loss is recovered by read. No mutation delegate is
  automatically replayed.
- All values are parameters. Only the already validated/quoted schema identifier
  is interpolated.

### 5.4 Generated JSON roots

`PostgreSqlRuntimeJsonSerializerContext` is the owning generated context for the
provider path. This is the exact direct-root ledger; implementation may add a
root when a new direct serializer call is introduced, but may not remove or
replace one with reflection metadata:

| Exact CLR root type | Owning `JsonSerializerContext` | Persistence column/use | Nested types covered by this root | Coverage test | Slice |
|---|---|---|---|---|---|
| `AgentToolInvocationPreDispatchIntentSnapshot` | `PostgreSqlRuntimeJsonSerializerContext` | `agent_tool_attempts.intent_json` | `AgentToolGovernanceAuditContext`, frozen `AgentToolInvocationLease`, `AgentToolApprovalResult`, contract/schema/governance identities | `GeneratedJsonRootLedger_Should_Cover_All_DurableAgentToolTypes` | 3/5 |
| `AgentToolBudgetReservation` | `PostgreSqlRuntimeJsonSerializerContext` | `agent_tool_budget_reservations.reservation_json` | budget category/cost/limit/state and Attempt/fingerprint identity | same | 3/5 |
| `AgentToolGovernancePreDispatchRecord` | `PostgreSqlRuntimeJsonSerializerContext` | `agent_tool_governance_checkpoints.checkpoint_json` | Context, frozen Lease, Approval, BudgetReservation | same | 2/5 |
| `AgentToolGovernancePreDispatchReceipt` | `PostgreSqlRuntimeJsonSerializerContext` | checkpoint/Attempt accepted-receipt projection | `AgentToolPreDispatchIdentity` | same | 1/5 |
| `AgentToolGovernancePreDispatchWriteResult` | `PostgreSqlRuntimeJsonSerializerContext` | generated provider write-result contract | Receipt | same | 1/5 |
| `AgentToolGovernancePreDispatchReadResult` | `PostgreSqlRuntimeJsonSerializerContext` | generated provider lookup contract | Receipt and complete Checkpoint | same | 1/5 |
| `AgentToolInvocationPreDispatchResult` | `PostgreSqlRuntimeJsonSerializerContext` | Gate read/transition persistence projection | intent snapshot, reservation and accepted receipt | same | 1/5 |
| `AgentToolInvocationAbandonedReceipt` | `PostgreSqlRuntimeJsonSerializerContext` | `agent_tool_attempts.abandoned_receipt_json` | stable denial outcome/reason and Attempt identity | same | 3/5 |
| `AgentToolBudgetReservationReadResult` | `PostgreSqlRuntimeJsonSerializerContext` | budget authoritative-read contract | Reservation | same | 1/5 |
| `AgentToolGovernanceDecisionRecord` | `PostgreSqlRuntimeJsonSerializerContext` | `agent_tool_governance_decisions.decision_json` | safe outcome/context/observed reservation | same | 5 |
| `AgentToolGovernanceFinalizationRecord` | `PostgreSqlRuntimeJsonSerializerContext` | checkpoint finalization projection | outcome, audit facts, lease and reservation | same | 5 |
| `AgentToolPreDispatchReconciliationObservation` | `PostgreSqlRuntimeJsonSerializerContext` | `agent_tool_pre_dispatch_reconciliation_observations.observation_json` | safe status/reason/revision metadata | same | 4/5 |
| `AgentToolPreDispatchReconciliationReceipt` | `PostgreSqlRuntimeJsonSerializerContext` | `agent_tool_pre_dispatch_reconciliation_receipts.receipt_json` | terminal status, identity, integrity and terminal time | same | 4/5 |
| `AgentToolPreDispatchReconciliationResult` | `PostgreSqlRuntimeJsonSerializerContext` | generated reconciler result contract | observation or terminal receipt projection | same | 4/5 |
| `PostgreSqlAgentToolInvocationRow` | `PostgreSqlRuntimeJsonSerializerContext` | provider row materialization/AOT coverage | no open object state | same | 5 |
| `PostgreSqlAgentToolAttemptRow` | `PostgreSqlRuntimeJsonSerializerContext` | provider row materialization/AOT coverage | typed intent/receipt fields | same | 5 |
| `PostgreSqlAgentToolBudgetReservationRow` | `PostgreSqlRuntimeJsonSerializerContext` | provider row materialization/AOT coverage | typed reservation | same | 5 |
| `PostgreSqlAgentToolGovernanceCheckpointRow` | `PostgreSqlRuntimeJsonSerializerContext` | provider row materialization/AOT coverage | typed checkpoint/receipt/finalization | same | 5 |
| `PostgreSqlAgentToolGovernanceDecisionRow` | `PostgreSqlRuntimeJsonSerializerContext` | provider row materialization/AOT coverage | typed decision | same | 5 |
| `PostgreSqlAgentToolPreDispatchReconciliationObservationRow` | `PostgreSqlRuntimeJsonSerializerContext` | provider row materialization/AOT coverage | typed observation | same | 5 |
| `PostgreSqlAgentToolPreDispatchReconciliationReceiptRow` | `PostgreSqlRuntimeJsonSerializerContext` | provider row materialization/AOT coverage | typed terminal tombstone | same | 5 |

`AgentToolInvocationPreDispatchIntentSnapshot`,
`AgentToolInvocationAbandonedReceipt`,
`AgentToolPreDispatchReconciliationObservation`, and
`AgentToolPreDispatchReconciliationReceipt` are fixed contract names introduced
in the §4 Abstractions files. Provider row DTOs are internal sealed records in
`PostgreSqlAgentToolPersistenceSupport.cs`.

`GeneratedJsonRootLedger_Should_Cover_All_DurableAgentToolTypes` lives in PGC
and enumerates this table as compile-time `JsonTypeInfo<T>` references.
`AgentToolPreDispatch_Should_Use_GeneratedJsonOnly` additionally rejects
reflection serializer overloads, `DefaultJsonTypeInfoResolver`, runtime `Type`,
and open object state.

---

## 6. Test File Map

The ledger below uses these aliases; every target is an exact repository path.

| Alias | Exact test file |
|---|---|
| `IM` | `tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/Governance/InMemoryAgentToolPreDispatchContractTests.cs` |
| `CMP` | `tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/Governance/AgentToolPreDispatchComparerTests.cs` |
| `INV` | `tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/Invocation/AgentToolInvokerPreDispatchRecoveryTests.cs` |
| `REC` | `tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/Invocation/AgentToolPreDispatchReconcilerTests.cs` |
| `ARCH` | `tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/Architecture/AgentToolPreDispatchContractArchitectureTests.cs` |
| `PGC` | `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/AgentTools/PostgreSqlAgentToolPreDispatchContractTests.cs` |
| `PGR` | `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/AgentTools/PostgreSqlAgentToolPreDispatchRestartTests.cs` |
| `PGL` | `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/AgentTools/PostgreSqlAgentToolPreDispatchResponseLossTests.cs` |
| `PGX` | `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/AgentTools/PostgreSqlAgentToolPreDispatchConcurrencyTests.cs` |
| `PGT` | `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/AgentTools/PostgreSqlAgentToolPreDispatchRetentionTests.cs` |
| `PGM` | `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/AgentTools/PostgreSqlAgentToolPreDispatchMigrationTests.cs` |
| `PGDI` | `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/AgentTools/PostgreSqlAgentToolRegistrationTests.cs` |
| `CRASH` | `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/PostgreSqlRuntimeCrashTests.cs` |
| `AOT` | `tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/PostgreSqlRuntimeAotFixtureTests.cs` |
| `BOUND` | `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/AgentToolPreDispatchPersistenceArchitectureTests.cs` |

Concrete InMemory and PostgreSQL runners call the same runner-free static Case
methods. Restart/crash tests additionally require
`IDurableAgentToolPreDispatchContractDriver`; InMemory does not skip or pretend
to pass durable cases.

---

## 7. One-row-per-Case Execution Ledger

### 7.1 Happy cases

| ID | Primary test | File | Slice |
|---|---|---|---|
| H01 | `Identical_PreDispatchRetry_Should_Return_SameAuditId` | IM + PGC | 2/5 |
| H02 | `Identical_PreDispatchRetry_Should_Return_SameAuditId` | IM + PGC | 2/5 |
| H03 | `Accepted_ResponseLoss_Should_Be_Reconciled_By_AttemptIdentity` | INV + PGL | 3/5 |
| H04 | `Accepted_BindResponseLoss_Should_Be_Reconciled_From_Gate` | INV + PGL | 3/5 |
| H05 | `Dispatch_Should_Require_ExactAcceptedReceipt`; `Immediate_ExactRecovery_Should_Dispatch_AtMostOnce` | INV + PGX | 3/5 |
| H06 | `Restarted_Reconciler_Should_Not_AutoDispatch` | REC + PGR | 4/5 |
| H07 | `Authoritative_Missing_Should_Close_Only_UnrecordedAttempt` | REC + PGR | 4/5 |
| H08 | `Reconciled_Checkpoint_Should_Not_Consume_Or_Release_BudgetTwice`; `Repeated_Reconciliation_Should_Return_SameTerminalReceipt` | REC + PGC | 4/5 |
| H09 | `Budget_ResponseLoss_Should_Recover_ReservationByAttemptIdentity` | INV + PGL | 3/5 |
| H10 | `ReservationBind_ResponseLoss_Should_Recover_ReadyState` | INV + PGL | 3/5 |

### 7.2 Boundary cases

| ID | Primary test | File | Slice |
|---|---|---|---|
| B01 | `Concurrent_IdenticalCheckpoint_Should_Have_OneAcceptance` | IM + PGX | 2/5 |
| B02 | `Different_Attempt_Should_Not_Be_Treated_As_Duplicate` | IM + PGC | 2/5 |
| B03 | `TenantScoped_Identity_Should_Not_Cross_Host_Or_Tenant` | IM + PGC | 2/5 |
| B04 | `Optional_SchemaContracts_Should_RoundTrip_Without_Defaults` | IM + PGC | 2/5 |
| B05 | `NotRequired_Approval_Should_Produce_ValidCheckpoint` | IM + PGC | 2/5 |
| B06 | `Pending_Checkpoint_Should_Block_LeaseExpiryReplacement` | INV + PGC | 3/5 |
| B07 | `Accepted_Checkpoint_Should_Block_LeaseExpiryReplacement` | INV + PGC | 3/5 |
| B08 | `Released_Reservation_Should_Not_Be_ReleasedTwice` | REC + PGC | 4/5 |
| B09 | `Retention_AtMinimumWindow_Should_RemainQueryable` | PGT | 6 |
| B10 | `Cleanup_AfterTerminalWindows_Should_RetainTerminalReceipt` | PGT | 6 |
| B11 | `Lookup_Should_Return_Detached_CompleteCheckpoint` | CMP + IM + PGC | 2/5 |
| B12 | `Repeated_Reconciliation_Should_Return_SameTerminalReceipt` | REC + PGC | 4/5 |
| B13 | `Ready_Checkpoint_Should_Block_LeaseExpiryReplacement` | INV + PGC | 3/5 |
| B14 | `LeaseRenewal_Should_Not_Change_FrozenCheckpointIdentity` | INV + PGC | 3/5 |
| B15 | `Known_BudgetDenial_Should_Abandon_Attempt_WithStableReceipt`; `Repeated_BudgetDenial_Should_Return_SameAbandonedReceipt` | INV + PGC | 3/5 |
| B16 | `Cleanup_Should_Not_Remove_PreDispatchReadyState` | PGT | 6 |
| B17 | `Acquire_AfterBudgetDenial_Should_Create_NewAttempt` | INV + PGC | 3/5 |
| B18 | `StillPending_Observation_Should_Progress_To_Released` | REC + PGC | 4/5 |

### 7.3 Failure cases

| ID | Primary test | File | Slice |
|---|---|---|---|
| F01 | `Conflicting_FullCheckpoint_Should_Be_Rejected_And_Preserve_First` | CMP + IM + PGC | 2/5 |
| F02 | `Changed_ToolCapabilityOrSchemaContract_Should_Conflict` | CMP + IM + PGC | 2/5 |
| F03 | `Changed_EffectiveGovernance_Should_Conflict` | CMP + IM + PGC | 2/5 |
| F04 | `Changed_LeaseIdentityOrTime_Should_Conflict` | CMP + IM + PGC | 2/5 |
| F05 | `Changed_ApprovalClaim_Should_Conflict` | CMP + IM + PGC | 2/5 |
| F06 | `Changed_BudgetFacts_Should_Conflict` | CMP + IM + PGC | 2/5 |
| F07 | `Malformed_WriteResult_Should_FailClosed` | INV | 3 |
| F08 | `Unconfirmed_Checkpoint_Should_Not_Dispatch_Or_Abandon` | INV + REC | 3/4 |
| F09 | `Unavailable_Lookup_Should_Not_Be_Treated_As_Missing` | INV + REC | 3/4 |
| F10 | `PendingFence_Should_Survive_ProcessRestart` | PGR | 5 |
| F11 | `DurableCheckpoint_Should_Survive_ProcessRestart` | PGR + CRASH | 5 |
| F12 | `AcceptedFence_Should_Survive_ProcessRestart` | PGR + CRASH | 5 |
| F13 | `Dispatch_Should_Require_ExactAcceptedReceipt`; `Stale_FencingToken_Should_Not_Dispatch` | INV + PGX | 3/5 |
| F14 | `Recovery_Should_Validate_FullLeaseFencingReservationApprovalAndGovernance`; `CommittedBudget_WithDispatchFalse_Should_Conflict` | REC + PGC | 4/5 |
| F15 | `Recovery_Should_Validate_FullLeaseFencingReservationApprovalAndGovernance`; `IndeterminateBudget_Should_RemainStillPending` | REC + PGC | 4/5 |
| F16 | `Recovery_Should_Validate_FullLeaseFencingReservationApprovalAndGovernance`; `PostDispatchUnknown_Should_Remain_Indeterminate` | REC + PGC | 4/5 |
| F17 | `Reconciled_Checkpoint_Should_Not_Consume_Or_Release_BudgetTwice`; `Concurrent_Reconcilers_Should_Have_One_CasWinner` | REC + PGX + PGC | 4/5 |
| F18 | `Cleanup_Should_Not_Remove_LiveReconciliationState` | PGT | 6 |
| F19 | `TooShort_Retention_Should_Fail_Startup` | PGT | 6 |
| F20 | `Migration_Should_Reject_IncompatibleCheckpointSchema` | PGM | 5 |
| F21 | `Cancellation_AtCrashWindow_Should_Not_ClaimMissingOrRollback` | PGL + CRASH | 5 |
| F22 | `AccountabilityFailure_Should_Not_Change_ReconciliationResult` | REC | 6 |
| F23 | `Lookup_Should_Return_Detached_CompleteCheckpoint` | CMP + IM + PGC | 2/5 |
| F24 | `Missing_RequiredPersistedField_Should_FailClosed` | PGM | 5 |
| F25 | `Unavailable_BudgetLookup_Should_RemainFenced` | INV + REC | 3/4 |
| F26 | `Malformed_BudgetDenial_Should_RemainFenced` | INV | 3 |
| F27 | `Ready_WithAuthoritativeBudgetMissing_Should_Conflict` | REC + PGC | 4/5 |
| F28 | `Accepted_WithAuthoritativeBudgetMissing_Should_Conflict` | REC + PGC | 4/5 |
| F29 | `Conflicting_BudgetDenialReceipt_Should_Be_Rejected` | INV + PGC | 3/5 |
| F30 | `StillPending_Should_Not_Create_TerminalReceipt` | REC + PGC | 4/5 |

### 7.4 Composition cases

| ID | Primary test | File | Slice |
|---|---|---|---|
| C01 | `Invoker_GovernanceComposition_Should_Produce_OneCoherentDecision` | INV | 3 |
| C02 | `Dispatch_Should_Require_ExactAcceptedReceipt`; `Immediate_ExactRecovery_Should_Dispatch_AtMostOnce` | INV + PGX | 3/5 |
| C03 | `Recovery_Should_Validate_FullLeaseFencingReservationApprovalAndGovernance`; `Restarted_Reconciler_Should_Not_AutoDispatch` | REC + PGC + PGR | 4/5 |
| C04 | `DurableCheckpoint_Should_Survive_ProcessRestart` | PGR | 5 |
| C05 | `CrashWindow_Should_Match_NormativeRecoveryState` | CRASH | 5 |
| C06 | `AccountabilityProjection_Should_Not_Replace_GovernanceControl` | REC | 6 |
| C07 | `InMemory_PreDispatch_Should_Pass_SharedSemanticContractCases` | IM | 2–4 |
| C08 | `AgentToolDurableContracts_Should_Not_Expose_NpgsqlTypes`; `RuntimeProjects_Should_Not_Reference_AgentToolPostgreSqlProvider` | ARCH + BOUND | 1/7 |
| C09 | `AgentToolPreDispatch_Should_Use_GeneratedJsonOnly` | ARCH + PGC | 5/7 |
| C10 | `PostgreSql_PreDispatch_Should_Run_In_NativeAot_Binary` | AOT | 7 |
| C11 | `Reserve_Gate_Checkpoint_ResponseLoss_Should_RecoverOriginalReceipts` | INV + PGL | 3/5 |
| C12 | `PostgreSql_AgentToolParticipants_Should_Pass_ExistingPhase8fContracts`; `PostgreSql_Registration_Order_Should_Not_Produce_MixedParticipants` | PGC + PGDI | 5 |

### 7.5 Normative-name and wrapper closure

These Spec-frozen names are mandatory independent wrappers, not aliases for a
similar test:

| Exact normative test | Cases | Exact files | Owning Slice | Concrete wrapper |
|---|---|---|---|---|
| `Repeated_BudgetDenial_Should_Return_SameAbandonedReceipt` | B15, B17, F29 | INV + PGC | 3/5 | `AgentToolInvokerPreDispatchRecoveryTests`; `PostgreSqlAgentToolPreDispatchContractTests` |
| `Dispatch_Should_Require_ExactAcceptedReceipt` | H05, C02, F13 | INV + PGX | 3/5 | `AgentToolInvokerPreDispatchRecoveryTests`; `PostgreSqlAgentToolPreDispatchConcurrencyTests` |
| `Recovery_Should_Validate_FullLeaseFencingReservationApprovalAndGovernance` | F02–F06, F13–F16, C03 | REC + PGC | 4/5 | `AgentToolPreDispatchReconcilerTests`; `PostgreSqlAgentToolPreDispatchContractTests` |
| `Reconciled_Checkpoint_Should_Not_Consume_Or_Release_BudgetTwice` | H08, B08, B12, F17 | REC + PGC | 4/5 | `AgentToolPreDispatchReconcilerTests`; `PostgreSqlAgentToolPreDispatchContractTests` |
| `AgentToolDurableContracts_Should_Not_Expose_NpgsqlTypes` | C08 | ARCH + BOUND | 1/7 | `AgentToolPreDispatchContractArchitectureTests`; `AgentToolPreDispatchPersistenceArchitectureTests` |

The Plan-required DI guard is also fixed:

| Exact test | Cases | Exact file | Owning Slice | Concrete wrapper |
|---|---|---|---|---|
| `PostgreSql_Registration_Order_Should_Not_Produce_MixedParticipants` | C12 | PGDI | 5 | `PostgreSqlAgentToolRegistrationTests` |

The Slice 1 manifest carries every ID and normative test name as typed test
metadata. Concrete `[Fact]` wrappers are added when their owning Slice starts
Red; there are no permanently skipped tests. The final Boundary test parses
this Plan ledger, manifest, and concrete wrappers so removing or renaming a Case
ID or normative test fails CI.

---

## 8. Slice 1 — Acceptance Scaffold and Contract Shape

### Red

Create the runner-free Contract Kit, the concrete xUnit partial-class files from
§6, and the 70-ID/name manifest before production behavior. Add:

```text
tests/Boundary/CrestCreates.DependencyBoundaries.Tests/
  AgentToolPreDispatchPersistenceArchitectureTests.cs
```

The first Red assertions prove:

- identity cannot be constructed without logical key + AttemptId;
- Auditor has authoritative lookup and typed write result;
- Gate has Prepare/BindReservation/BindAccepted/Get operations;
- dispatch cannot compile without receipt and ReservationId;
- Budget has Attempt-identity read;
- reconciler has no Dispatcher dependency;
- shared testing project is runner/provider/runtime-free;
- every H01–H10, B01–B18, F01–F30, C01–C12 appears in the typed manifest and
  has one planned concrete wrapper owner.

Run:

```bash
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj --filter "FullyQualifiedName~AgentToolPreDispatchPersistenceArchitectureTests"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj --filter "FullyQualifiedName~AgentToolPreDispatchContractArchitectureTests"
```

Expected Red: missing contracts/project graph and the legacy receipt-free
dispatch signature.

### Green

- Add the minimum records/enums/interfaces in §4; do not implement provider
  behavior.
- Update existing Agent Tool test doubles to compile against the new signatures.
- Add the runner-free project to both solutions.
- Do not add `[Fact]` to a behavior until its owning Slice starts Red. The
  skeleton fixes class/file/name metadata without committed skipped or failing
  placeholders.

Run:

```bash
rtk dotnet build src/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions/CrestCreates.Agent.Tools.Abstractions.csproj
rtk dotnet build tests/Shared/CrestCreates.Agent.Tools.Persistence.Testing/CrestCreates.Agent.Tools.Persistence.Testing.csproj
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj --filter "FullyQualifiedName~AgentToolPreDispatch"
```

### Review gate

- No caller-supplied AuditId path exists.
- No old receipt-free dispatch overload remains.
- BindAccepted source state is Ready in contracts, docs, and test names.
- Public types expose no Npgsql/ADO.NET/provider exception.
- Contract Kit has no runner or concrete implementation dependency.
- `rtk git diff --check` passes.

---

## 9. Slice 2 — Complete Equality and InMemory Semantics

### Red

Activate H01/H02, B01–B05/B11, F01–F06/F23, and C07 in `CMP`/`IM`.
Generate one `[Theory]` mutation row for every required/optional nested field in
Spec INV-04, including nullable schema identities and every lease/approval/
budget/governance field.

Run:

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj --filter "FullyQualifiedName~AgentToolPreDispatchComparerTests|FullyQualifiedName~InMemoryAgentToolPreDispatchContractTests"
```

Expected Red: Handle-only response, incomplete comparer, mutable snapshots, and
missing typed lookup/duplicate/conflict behavior.

### Green

- Implement the shared Abstractions semantic projection, snapshot, and
  `AgentToolGovernancePreDispatchComparer` from §4.3.
- Cut `DevelopmentInMemoryAgentToolGovernanceAuditor` to typed write/read
  semantics using one `(LogicalInvocationKey, AttemptId)` index.
- Return the first provider-issued AuditId/AcceptedAt for identical sequential
  and concurrent retries.
- Preserve the first checkpoint on conflict and deep-copy every returned
  snapshot.
- Preserve existing decision/finalization Phase 8f semantics against the new
  receipt identity.

### Review gate

Run the focused suite, all existing Governance Auditor tests, and mutation
coverage:

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj --filter "FullyQualifiedName~GovernanceAuditor|FullyQualifiedName~PreDispatchComparer|FullyQualifiedName~InMemoryAgentToolPreDispatchContract"
rtk rg -n "AgentToolGovernanceAuditHandle|ReferenceEquals|JsonSerializer.*object|DefaultJsonTypeInfoResolver" src/Runtime/Agent/CrestCreates.Agent.Tools src/Runtime/Agent/CrestCreates.Agent.Tools.Abstractions
rtk git diff --check
```

The Handle search must be empty in the active mainline. Review compares the
snapshot field ledger against INV-04 manually; hash-only equality blocks Green.

---

## 10. Slice 3 — Pending/Ready/Accepted Gate and Invoker Cutover

### Red

Activate H03–H05/H09/H10, B06/B07/B13–B15/B17,
F07–F13/F25/F26/F29, and C01/C02/C11 in `INV`. Extend existing
`AgentToolInvocationGateTests.cs` and `AgentToolBudgetGateTests.cs` for the new
state transitions and Attempt-idempotent reserve/read contract.

Every ambiguity fake exposes whether the mutation committed before throwing.
Each case asserts Dispatcher call count, reservation count, AuditId, AttemptId,
and final Gate state.

### Green

Modify:

```text
Invocation/DevelopmentInMemoryAgentToolInvocationGate.cs
Governance/DevelopmentInMemoryAgentToolBudgetGate.cs
Invocation/AgentToolInvoker.cs
Invocation/AgentToolInvocationFingerprintBuilder.cs (only if projection input changes)
```

Implement the exact same-Worker order:

```text
Acquire
-> approval claim
-> PreparePreDispatchIntent
-> Reserve by identity
-> BindReservation/confirm Ready
-> Record checkpoint
-> BindAccepted/confirm Accepted
-> receipt-bound DispatchStarted CAS
-> Dispatcher
```

Add one bounded read/retry/read helper per acknowledgement-loss boundary. The
helpers accept the frozen intent snapshot and exact lease ownership; they never
run after restart. A valid Denied result records Decision evidence, publishes
the immutable Abandoned receipt, returns a stable denial, and calls Dispatcher
zero times. A malformed/ambiguous denial queries Budget and remains fenced when
absence is not authoritative.

Remove the old `TryRecoverPreDispatchAsync` Record-only behavior and the old
receipt-free dispatch overload once all callers are converted.

### Review gate

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj --filter "FullyQualifiedName~AgentToolInvocationGateTests|FullyQualifiedName~AgentToolBudgetGateTests|FullyQualifiedName~AgentToolInvokerPreDispatchRecoveryTests|FullyQualifiedName~AgentToolInvokerTests"
rtk rg -n "TryMarkDispatchStartedAsync\([^,]+,[[:space:]]*CancellationToken|TryRecoverPreDispatchAsync" src tests/Runtime/Agent
rtk git diff --check
```

Review manually proves:

- budget Reserve cannot occur before durable Pending;
- Auditor cannot run before Ready;
- Accepted binding cannot run from Pending;
- renewal changes current expiry only, never frozen intent;
- expired Pending/Ready/Accepted cannot create a replacement Attempt;
- repeated Denied returns the same old Attempt receipt while a later Acquire
  creates a new Attempt and re-evaluates budget;
- every ambiguity/denial/conflict path calls Dispatcher zero times.

---

## 11. Slice 4 — Reconciler and Observation/Receipt Semantics

### Red

Activate H06–H08, B08/B12/B18, F14–F17/F27/F28/F30, and C03 in
`REC`. Build a table-driven matrix over:

```text
Gate: Pending | Ready | Accepted | DispatchStarted | terminal | unavailable
Budget: Missing | Reserved | Released | Committed | Indeterminate | unavailable
Checkpoint: Missing | Accepted | Finalized | unavailable | conflicting
```

Tests assert both the returned observation and persisted receipt count.

### Green

Create:

```text
Governance/DefaultAgentToolPreDispatchReconciler.cs
Governance/AgentToolPreDispatchReconciliationAccountabilityProducer.cs
```

Keep the Accountability producer dormant until Slice 6; Slice 4 wires a no-op
optional collaborator. The reconciler reads Gate, Budget, and checkpoint in the
Spec order and owns no `ICapabilityDispatcher`, approval evaluator, or Tool
handler dependency.

Implement:

- Pending + authoritative Budget Missing + checkpoint Missing + dispatch false
  -> Abandoned;
- Ready/Accepted + Budget Missing -> Conflict;
- Accepted + Reserved -> release/finalize/publish without dispatch;
- already Released -> converge without second budget effect;
- dispatch true/unknown -> PostDispatchUnknown;
- authority unavailable/Indeterminate -> mutable StillPending observation;
- one terminal receipt through CAS; repeated Released projects
  AlreadyReleased from that receipt.

### Review gate

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj --filter "FullyQualifiedName~AgentToolPreDispatchReconcilerTests|FullyQualifiedName~InMemoryAgentToolPreDispatchContractTests"
rtk rg -n "ICapabilityDispatcher|IAgentToolApprovalGate|DispatchAsync" src/Runtime/Agent/CrestCreates.Agent.Tools/Governance/DefaultAgentToolPreDispatchReconciler.cs
rtk git diff --check
```

The dependency search must be empty. Review proves `StillPending` can advance to
Released and has not populated immutable terminal fields.

---

## 12. Slice 5 — PostgreSQL Participants and Crash Windows

### Red

Add the PostgreSQL driver and activate all shared semantic cases in `PGC`, then
add F20/F21/F24 through `PGR`, `PGL`, `PGX`, `PGM`, and `CRASH`. F24 becomes
Red only here, when generated provider JSON and a persisted row can actually be
mutated. Add `PostgreSql_Registration_Order_Should_Not_Produce_MixedParticipants`
in PGDI before provider registration changes. Extend the existing CrashWorker
with Agent Tool scenarios; do not create a second worker unless the existing
process protocol cannot express a crash boundary.

Required worker scenarios:

```text
agent-tool-cw02-pending-committed
agent-tool-cw04-budget-committed
agent-tool-cw06-ready-committed
agent-tool-cw08-checkpoint-committed
agent-tool-cw12-dispatch-cas-entered
agent-tool-cw13-dispatch-started-committed
agent-tool-cw15-release-pending-committed
agent-tool-cw16-released-receipt-committed
```

The parent test starts the worker, waits for a deterministic sentinel emitted
after the target database commit, kills the process, creates a fresh provider,
and checks the Crash Window Ledger. No test infers commit from elapsed time.

### Green

- Append V007 and its complete schema manifest.
- Implement the three complete PostgreSQL participant classes and
  reconciliation store from §5.
- Make `PostgreSqlAgentToolGovernanceAuditor` call the exact shared
  `AgentToolGovernancePreDispatchComparer` from Agent Tool Abstractions after a
  uniqueness collision; do not add a provider-private field comparer.
- Register every Agent Tool participant with the §3.3 `RemoveAll<TContract>()`
  plus one durable registration rule; retain no volatile split and no
  order-dependent resolution.
- Add provider-internal generated persistence DTOs and JSON roots.
- Translate constraint/CAS outcomes to stable provider-neutral results.
- Make authoritative reads primary/consistent and tenant-complete.
- Extend the provider options/capabilities to declare Agent Tool durable
  reconciliation through `IAgentToolPreDispatchPersistenceCapabilities` only
  when V007 schema validation succeeds; do not add Agent Tool members to the
  generic `IRuntimePersistenceProviderCapabilities` contract.

### Review gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~AgentTool"
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~PostgreSqlRuntimeCrashTests"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj --filter "FullyQualifiedName~AgentToolPreDispatch|FullyQualifiedName~RuntimePersistence"
rtk git diff --check
```

Review inspects every SQL statement for full tenant/logical/Attempt identity,
parameters, state/revision/fencing CAS, and acknowledgement-loss reread. Compare
V001–V006 bytes/checksums against branch base; only V007 may be new.

---

## 13. Slice 6 — Retention, Cleanup, and Accountability

### Red

Activate B09/B10/B16, F18/F19/F22, and C06. Add deterministic clock support to
the provider test driver and race cleanup against a held reconciliation CAS.

Retention options added to `PostgreSqlRuntimePersistenceOptions`:

```text
MaximumInvocationReconciliationWindow
InvocationAttemptReceiptRetention
BudgetReservationRetention
GovernanceCheckpointRetention
GovernanceFinalizationRetention
ReconciliationObservationRetention
ReconciliationReceiptRetention
AccountabilityProjectionRetryWindow
```

Freeze these defaults:

| Option | Default |
|---|---|
| `MaximumInvocationReconciliationWindow` | 7 days |
| `InvocationAttemptReceiptRetention` | 30 days |
| `BudgetReservationRetention` | 30 days |
| `GovernanceCheckpointRetention` | 90 days |
| `GovernanceFinalizationRetention` | 90 days |
| `ReconciliationObservationRetention` | 14 days |
| `ReconciliationReceiptRetention` | 30 days |
| `AccountabilityProjectionRetryWindow` | 7 days |

Every dependent retention must be greater than or equal to
`MaximumInvocationReconciliationWindow`; invalid composition fails at startup.
Hosts may lengthen these values. Shortening below the dependency floor is not a
warning or development convenience; it is a startup error.

### Green

- Implement provider cleanup with one transaction/CAS and the Spec's exact
  protected set: Pending, Ready, Accepted, ReleasePending, CompletionPending,
  Indeterminate.
- Keep live StillPending observation/reconciliation ownership non-cleanable.
- Delete the FK-bound mutable observation with an eligible Attempt aggregate,
  but retain the independent terminal receipt tombstone for its declared lookup
  window after aggregate cleanup.
- Wire `AgentToolPreDispatchReconciliationAccountabilityProducer` to
  `IAuditRecorder`, never `IAuditSink`.
- Emit only safe IDs/descriptors/reason families after the durable terminal
  control transition. Exclude arguments, prompt/content, opaque approval data,
  raw provider errors, SQL, and Tool output.
- Accountability failure is observed/logged according to existing producer
  conventions and cannot alter the reconciliation result.

### Review gate

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~Retention|FullyQualifiedName~Cleanup"
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj --filter "FullyQualifiedName~Accountability|FullyQualifiedName~Reconciler"
rtk rg -n "IAuditSink|Arguments|Prompt|Output|Exception" src/Runtime/Agent/CrestCreates.Agent.Tools/Governance/AgentToolPreDispatchReconciliationAccountabilityProducer.cs
rtk git diff --check
```

Review proves cleanup cannot manufacture Missing, Ready is protected everywhere,
StillPending is not terminalized, and Accountability is one-way post-fact.

---

## 14. Slice 7 — NativeAOT and Final Repository Evidence

### Red

Extend the existing PostgreSQL AotHost/AotFixture. The fixture initially fails
because the native host does not execute Agent Tool V007 participants or emit:

```text
CRESTCREATES_DURABLE_AGENT_TOOL_PREDISPATCH_OK
```

Activate C08/C09/C10 and run all ledger/boundary tests.

### Green

The original linked linux-x64 binary must:

1. apply/validate V007 against real PostgreSQL;
2. compose complete PostgreSQL Gate/Budget/Auditor participants;
3. acquire an Attempt and persist Pending before budget reserve;
4. claim deterministic test approval, reserve, bind Ready;
5. accept a checkpoint while simulating lost acknowledgement;
6. dispose the first provider and create a fresh provider view;
7. recover the same AuditId and complete checkpoint by identity;
8. reconcile with a Dispatcher spy proving zero calls;
9. repeat reconciliation and prove one budget release/terminal receipt;
10. print the sentinel and exit zero.

Add Agent Tools/Accountability references and generated JSON BuildTasks roots to
the existing AotHost only as required by this scenario. Do not suppress IL2026,
IL3050, or missing-generated-root warnings.

Run:

```bash
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests.csproj --filter "FullyQualifiedName~PostgreSql_PreDispatch_Should_Run_In_NativeAot_Binary"
```

The fixture itself publishes with:

```bash
rtk dotnet publish tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotHost/CrestCreates.Runtime.Persistence.PostgreSql.AotHost.csproj -c Release -r linux-x64 --self-contained true -p:CrestCreatesPublishMode=aot --disable-build-servers
```

and launches the original produced executable against its Testcontainers
PostgreSQL instance.

### Final review gate

```bash
rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj
rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests.csproj
rtk dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests/CrestCreates.DependencyBoundaries.Tests.csproj
rtk dotnet build CrestCreates.slnx
rtk git diff --check
```

After executable evidence is captured, update `memory.md` with the exact support
tier and evidence command. Do not claim Tool replay or exactly-once external
side effects.

---

## 15. Crash Window Execution Ledger

### 15.1 CrashWorker process contract

CrashWorker keeps the existing five-argument protocol. Every process-kill row
is reproducible with:

```bash
rtk dotnet run --project tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker.csproj -- "<connection-string>" "<schema>" "<operation-id>" "<application-name>" "<scenario>"
```

The parent xUnit test starts the equivalent built worker command, waits for the
exact sentinel, kills the process tree, and only then creates a fresh provider.

| Worker scenario | Application name | Required sentinel |
|---|---|---|
| `agent-tool-cw02-pending-committed` | `crest-agent-tool-cw02` | `AGENT_TOOL_CW02_PENDING_COMMITTED` |
| `agent-tool-cw04-budget-committed` | `crest-agent-tool-cw04` | `AGENT_TOOL_CW04_BUDGET_COMMITTED` |
| `agent-tool-cw06-ready-committed` | `crest-agent-tool-cw06` | `AGENT_TOOL_CW06_READY_COMMITTED` |
| `agent-tool-cw08-checkpoint-committed` | `crest-agent-tool-cw08` | `AGENT_TOOL_CW08_CHECKPOINT_COMMITTED` |
| `agent-tool-cw12-dispatch-cas-entered` | `crest-agent-tool-cw12` | `AGENT_TOOL_CW12_DISPATCH_CAS_ENTERED` |
| `agent-tool-cw13-dispatch-started-committed` | `crest-agent-tool-cw13` | `AGENT_TOOL_CW13_DISPATCH_STARTED_COMMITTED` |
| `agent-tool-cw15-release-pending-committed` | `crest-agent-tool-cw15` | `AGENT_TOOL_CW15_RELEASE_PENDING_COMMITTED` |
| `agent-tool-cw16-released-receipt-committed` | `crest-agent-tool-cw16` | `AGENT_TOOL_CW16_RELEASED_RECEIPT_COMMITTED` |

### 15.2 Exact CW01–CW18 evidence

| Window | Execution mode | Exact test method | Exact file | Worker scenario / sentinel | Exact command or filter | Required assertion |
|---|---|---|---|---|---|---|
| CW01 | Unit fake | `CrashWindow_CW01_BeforePendingCommit_Should_NotStartBudget` | INV | — | `rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW01_BeforePendingCommit_Should_NotStartBudget"` | no budget call or checkpoint; ordinary lease rules only |
| CW02 | CrashWorker | `CrashWindow_CW02_PendingCommitted_Should_AbandonOnlyAfterAuthoritativeMissing` | CRASH | `agent-tool-cw02-pending-committed` / `AGENT_TOOL_CW02_PENDING_COMMITTED` | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW02_PendingCommitted_Should_AbandonOnlyAfterAuthoritativeMissing"` | fresh provider proves Budget/Checkpoint Missing and dispatch false before Abandoned |
| CW03 | PostgreSQL response loss | `CrashWindow_CW03_AmbiguousReserve_Should_RemainPendingUntilAuthoritativeRead` | PGL | — | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW03_AmbiguousReserve_Should_RemainPendingUntilAuthoritativeRead"` | no replacement or dispatch while reserve authority is unknown |
| CW04 | CrashWorker | `CrashWindow_CW04_RecoveredReservedBudgetWithMissingCheckpoint_Should_ReleaseAndAbandonWithoutDispatch` | CRASH | `agent-tool-cw04-budget-committed` / `AGENT_TOOL_CW04_BUDGET_COMMITTED` | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW04_RecoveredReservedBudgetWithMissingCheckpoint_Should_ReleaseAndAbandonWithoutDispatch"` | recover original ReservationId, prove checkpoint Missing/dispatch false, release original reservation, abandon Attempt, Dispatcher zero |
| CW05 | PostgreSQL response loss | `CrashWindow_CW05_LostReserveResponseBeforeBind_Should_RecoverOriginalReservation` | PGL | — | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW05_LostReserveResponseBeforeBind_Should_RecoverOriginalReservation"` | exact reservation binds once or reconciles Released |
| CW06 | CrashWorker | `CrashWindow_CW06_ReadyCommit_Should_SurviveRestart` | CRASH | `agent-tool-cw06-ready-committed` / `AGENT_TOOL_CW06_READY_COMMITTED` | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW06_ReadyCommit_Should_SurviveRestart"` | exact Ready reservation binding survives process death |
| CW07 | PostgreSQL response loss | `CrashWindow_CW07_AmbiguousCheckpointWrite_Should_RequireAuthoritativeLookup` | PGL | — | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW07_AmbiguousCheckpointWrite_Should_RequireAuthoritativeLookup"` | Ready remains fenced; no guessed Missing/Accepted |
| CW08 | CrashWorker | `CrashWindow_CW08_CheckpointCommit_Should_RecoverOriginalReceipt` | CRASH | `agent-tool-cw08-checkpoint-committed` / `AGENT_TOOL_CW08_CHECKPOINT_COMMITTED` | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW08_CheckpointCommit_Should_RecoverOriginalReceipt"` | original AuditId, AcceptedAt, and full detached checkpoint recovered |
| CW09 | PostgreSQL response loss | `CrashWindow_CW09_LostReceiptBeforeGateBind_Should_ConvergeWithoutNewReceipt` | PGL | — | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW09_LostReceiptBeforeGateBind_Should_ConvergeWithoutNewReceipt"` | Ready + checkpoint converges using first receipt only |
| CW10 | PostgreSQL response loss | `CrashWindow_CW10_LostAcceptedBindResponse_Should_RecoverAcceptedGateState` | PGL | — | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW10_LostAcceptedBindResponse_Should_RecoverAcceptedGateState"` | exact Accepted binding recovered by Gate read |
| CW11 | Fresh-provider restart | `CrashWindow_CW11_AcceptedBeforeDispatch_Should_ReconcileWithoutDispatch` | PGR | — | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW11_AcceptedBeforeDispatch_Should_ReconcileWithoutDispatch"` | original reservation Released, Attempt closed, Dispatcher zero |
| CW12 | CrashWorker | `CrashWindow_CW12_DispatchCasAmbiguity_Should_ReadDurableGateBeforeRecovery` | CRASH | `agent-tool-cw12-dispatch-cas-entered` / `AGENT_TOOL_CW12_DISPATCH_CAS_ENTERED` | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW12_DispatchCasAmbiguity_Should_ReadDurableGateBeforeRecovery"` | only authoritative false remains pre-dispatch; unknown stays fenced |
| CW13 | CrashWorker | `CrashWindow_CW13_DispatchStartedCommit_Should_BePostDispatchUnknown` | CRASH | `agent-tool-cw13-dispatch-started-committed` / `AGENT_TOOL_CW13_DISPATCH_STARTED_COMMITTED` | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW13_DispatchStartedCommit_Should_BePostDispatchUnknown"` | PostDispatchUnknown; no release, replay, or inferred Tool result |
| CW14 | PostgreSQL response loss | `CrashWindow_CW14_ReleasedBudgetResponseLoss_Should_NotReleaseTwice` | PGL | — | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW14_ReleasedBudgetResponseLoss_Should_NotReleaseTwice"` | release protocol resumes with one budget terminal effect |
| CW15 | CrashWorker | `CrashWindow_CW15_ReleasePendingCommit_Should_ResumeFinalizationAndPublish` | CRASH | `agent-tool-cw15-release-pending-committed` / `AGENT_TOOL_CW15_RELEASE_PENDING_COMMITTED` | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW15_ReleasePendingCommit_Should_ResumeFinalizationAndPublish"` | finalize/query audit then publish Released once |
| CW16 | CrashWorker | `CrashWindow_CW16_ReleasedReceiptCommit_Should_ReturnAlreadyReleased` | CRASH | `agent-tool-cw16-released-receipt-committed` / `AGENT_TOOL_CW16_RELEASED_RECEIPT_COMMITTED` | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW16_ReleasedReceiptCommit_Should_ReturnAlreadyReleased"` | same immutable receipt projected as AlreadyReleased |
| CW17 | Unit fake | `CrashWindow_CW17_AccountabilityFailure_Should_PreserveControlReceipt` | REC | — | `rtk dotnet test tests/Runtime/Agent/CrestCreates.Agent.Tools.Tests/CrestCreates.Agent.Tools.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW17_AccountabilityFailure_Should_PreserveControlReceipt"` | control terminal/receipt unchanged; projection may retry independently |
| CW18 | PostgreSQL deterministic race | `CrashWindow_CW18_CleanupRace_Should_PreserveLiveReconciliationEvidence` | PGT | — | `rtk dotnet test tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.Tests/CrestCreates.Runtime.Persistence.PostgreSql.Tests.csproj --filter "FullyQualifiedName~CrashWindow_CW18_CleanupRace_Should_PreserveLiveReconciliationEvidence"` | cleanup skips/loses CAS; live observation/checkpoint remains |

Unit-fake rows cover boundaries whose correctness depends only on call ordering
and injected acknowledgement loss. PostgreSQL response-loss rows require real
commit/read/CAS behavior but not process death. CrashWorker rows are reserved
for restart durability or a pre/post-commit distinction that an in-process fake
cannot prove. Every cancellation case uses its own bounded operational token
after caller cancellation; cancellation is never evidence that a commit did not
occur.

---

## 16. Review Checklist

Each Slice review records yes/no evidence for all applicable questions:

1. Is logical key + AttemptId the only recovery identity?
2. Is provider-issued AuditId returned unchanged for all identical retries?
3. Does full comparison include every INV-04 field and nullable value?
4. Are returned snapshots detached?
5. Is Pending durable before Reserve can start?
6. Is one reservation recoverable by Attempt identity after response loss?
7. Does valid Denied create one stable Abandoned receipt?
8. Does later same-fingerprint Acquire create a new Attempt and re-evaluate?
9. Is Ready required before Record and before BindAccepted?
10. Does dispatch require exact lease/fencing/receipt/reservation proof?
11. Can renewal change current expiry without changing frozen checkpoint time?
12. Do Pending/Ready/Accepted survive expiry and block replacement?
13. Can provider failure, lag, timeout, cancellation, or cleanup become Missing?
14. Does Pending+authoritative double-Missing abandon only with dispatch false?
15. Do Ready/Accepted + Budget Missing conflict without erasing evidence?
16. Is `StillPending` mutable observation metadata rather than a terminal receipt?
17. Do concurrent reconcilers converge on one immutable terminal receipt?
18. Does restart reconciliation have zero Dispatcher dependencies/calls?
19. Are post-dispatch true/unknown states left Indeterminate/PostDispatchUnknown?
20. Does PostgreSQL register complete Gate/Budget/Auditor implementations?
21. Are all SQL identities tenant-complete and all values parameterized?
22. Are V001–V006 unchanged and V007 checksummed/shape-validated?
23. Does cleanup protect Ready and every other non-cleanable state?
24. Can Accountability fail without changing control state?
25. Are all durable JSON roots generated with no reflection fallback?
26. Do Runtime/Contract Kit projects avoid provider references/types?
27. Do worker tests prove both pre-commit ambiguity and post-commit response loss?
28. Does the original native binary execute the real provider and print the
    sentinel?
29. Does every one of the 70 Case IDs still map to an exact test file/name?
30. Does the write result carry only the frozen
    `AgentToolGovernancePreDispatchReceipt`, while lookup alone carries the
    detached complete Checkpoint?
31. Do Runtime and PostgreSQL call the same pure Abstractions comparer with no
    provider-private field ledger?
32. Can the independent terminal receipt tombstone remain queryable after its
    Attempt and mutable observation are cleaned up?
33. Are all Spec-normative test names present in the typed manifest and exact
    concrete wrappers?
34. Are F07 and F24 activated only in Slice 3 and Slice 5 respectively?
35. Does every CW row name its execution mode, exact test/file/filter, worker
    scenario when needed, and sentinel when process-killed?
36. Does PostgreSQL use `RemoveAll<TContract>()` plus one durable registration
    so both composition orders and `IEnumerable<T>` resolve no mixed owner?

Any “no” blocks the next Slice or final closure.

---

## 17. Plan Review Findings

### P01 — Durable checkpoint with volatile Gate/Budget would preserve split registration

Closed by one PostgreSQL composition that implements and registers the complete
Phase 8f Gate, Budget, and Auditor participants, plus C12 and startup type checks.

### P02 — Decision Audit is not always backed by a Gate Attempt

Closed by keeping `agent_tool_governance_decisions` identity-scoped without a
mandatory Attempt FK. Early pre-acquire decisions remain durable; budget-denial
decisions are linked and validated compositionally when a Pending Attempt exists.

### P03 — Runtime reconciler needs durable receipt persistence without a provider dependency

Closed by `IAgentToolPreDispatchReconciliationStore` in Agent Tool Abstractions.
InMemory and PostgreSQL implement the same observation/terminal CAS contract;
cleanup remains provider-internal.

### P04 — Agent Tool capability cannot leak into generic Runtime Persistence contracts

Closed by `IAgentToolPreDispatchPersistenceCapabilities` in Agent Tool
Abstractions. The PostgreSQL provider reports FullDurable through that contract;
the generic provider capability surface remains unchanged.

### P05 — Retention values left open would make cleanup tests non-normative

Closed by freezing the 7/14/30/90-day defaults in Slice 6 and validating every
participant against the seven-day maximum reconciliation window.

### P06 — Creating every future `[Fact]` in Slice 1 would leave committed Red or skipped tests

Closed by creating the complete typed ID/name manifest and partial runner files
first, then adding active `[Fact]` wrappers at the start of each owning Red
Slice. No permanent `Skip` or fake Green placeholder is allowed.

### P07 — Existing CrashWorker/AotHost already own process and native evidence

Closed by extending those fixtures with deterministic Agent Tool scenarios and
one sentinel. A parallel worker/native fixture is allowed only if review proves
the existing protocol cannot isolate a required crash boundary.

### P08 — BindAccepted review fix could remain inconsistent in response-loss logic

Closed by requiring Ready in the public contract, transition table, immediate
bind-retry branch, Case tests, and Slice 3 review search. Pending can only bind a
reservation.

### P09 — Plan public contracts drifted from the approved Spec

Closed by freezing `AgentToolGovernancePreDispatchReceipt`,
`GetPreDispatchStateAsync`, write-result Receipt-only semantics, and read-result
Receipt plus detached complete Checkpoint semantics. The existing
`AgentToolGovernanceAuditHandle` is an explicit mainline cutover target.

### P10 — PostgreSQL could not consume a comparer owned by the concrete Runtime

Closed by moving the pure projection, snapshot, and sole
`AgentToolGovernancePreDispatchComparer` into Agent Tool Abstractions. Runtime,
InMemory, PostgreSQL, and tests call that exact implementation; a second
provider comparer is forbidden.

### P11 — Attempt FK conflicted with terminal receipt retention

Closed by splitting mutable FK-bound reconciliation observations from immutable
identity-complete terminal receipt tombstones. Only observations cascade with
Attempt cleanup; receipts have no destructive Attempt FK and expire by their
own retention.

### P12 — Five Spec-normative test names disappeared from Plan ownership

Closed by §7.5, the one-row Case ledger, typed manifest requirement, exact files,
owning Slices, and concrete wrapper classes for all five names.

### P13 — Slice 2 activated cases before their owning infrastructure existed

Closed by assigning F07 exclusively to Slice 3/INV and F24 exclusively to Slice
5/PGM. Slice 2 now owns only executable comparer/InMemory semantic Red cases.

### P14 — Crash and generated-JSON evidence was categorical rather than executable

Closed by 18 exact CW rows, eight process-kill scenarios/sentinels, exact test
commands, and 21 exact CLR roots with owning context, storage/use, nested
coverage, test, and Slice. CW04 explicitly proves recover Reservation → release
→ abandon → Dispatcher zero.

### P15 — PostgreSQL participant ownership depended on registration order

Closed by mandatory `RemoveAll<TContract>()` plus one durable registration for
every complete participant and
`PostgreSql_Registration_Order_Should_Not_Produce_MixedParticipants` across
both composition orders and enumerable resolution.

No unresolved Spec- or Plan-level direction finding remains. This Plan is
approved for implementation in the fixed Slice order.

---

## 18. Completion Criteria

Implementation is complete only when:

- all 70 Case Matrix rows and all 18 Crash Windows have executable evidence;
- InMemory passes every semantic shared Contract Case without a durability
  claim;
- PostgreSQL passes the same semantics plus restart, response-loss, crash,
  migration, tenant, concurrency, retention, and complete Phase 8f participant
  contracts;
- no legacy Handle/receipt-free dispatch/Record-only recovery path remains in
  the production mainline;
- write/read contracts retain the approved Receipt-only versus
  Receipt-plus-Checkpoint split, and all implementations use the one shared
  Abstractions semantic comparer;
- authoritative Missing, Denied Abandoned receipts, Ready retention, and
  StillPending observation semantics match the approved Spec exactly;
- terminal reconciliation receipts remain queryable independently after
  eligible Attempt/observation cleanup;
- PostgreSQL registration is order-independent and resolves no development
  participant through either single or enumerable DI resolution;
- the reconciler never dispatches and Accountability never authorizes;
- the original NativeAOT binary links and runs the complete PostgreSQL scenario;
- full regression/boundary commands and `git diff --check` pass;
- `memory.md` is updated only with evidence-backed support language.

This Plan does not authorize automatic Tool replay, exactly-once external side
effects, Outbox delivery, Agent Memory durability, or any second runtime path.
