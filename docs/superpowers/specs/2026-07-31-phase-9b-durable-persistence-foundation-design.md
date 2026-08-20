# Phase 9b — Durable Persistence Foundation Design Spec

**Date**: 2026-07-31  
**Issue**: [#24 — Phase 9b Durable Persistence Foundation](https://github.com/OrchesAdam/CrestCreates/issues/24)  
**Depends on**: [#39 — Phase 9a Accountability Runtime Foundation](https://github.com/OrchesAdam/CrestCreates/issues/39)  
**Prepares**: #25 transactional Outbox composition  
**Status**: APPROVED / Ready for Implementation Plan  
**Provider decision**: PostgreSQL 16 baseline, direct Npgsql, no EF Core on the production runtime-persistence mainline

---

## 1. Decision Summary

Phase 9b delivers one durable runtime authority, not a general ORM layer and not
an executable Descriptor repository.

The production mainline is:

```text
explicit Runtime State contracts
    + exact Descriptor Pins
    + tenant-scoped instance keys
    + provider-neutral transaction coordinator
    -> direct-Npgsql PostgreSQL stores
```

The first atomic business boundary is Workflow suspension:

```text
HumanTaskInstance insert
    + WorkflowInstance Running -> Suspended CAS transition
    + exact Workflow/HumanTask Descriptor Pins
    + optional DescriptorSnapshot evidence reference validation
    = one PostgreSQL transaction
```

After restart, executable definitions come only from the current activated
Workflow and HumanTask registries. Persisted state proves which exact definition
was used through immutable pins. It does not reconstruct executable definitions.

`DescriptorSnapshotStore` is an immutable evidence/index authority. Phase 6f
snapshots contain refs, hashes, and relationship facts but no `IDescriptor`
payload. Snapshot persistence can help explain and verify a deployment; it
cannot execute one.

The durable `IAuditSink` preserves Phase 9a Accepted/Duplicate/Conflict
semantics across restart. It does not make state-to-accountability delivery
reliable. That coupling remains a #25 Outbox responsibility.

---

## 2. Why the Issue Direction Must Change

### 2.1 DescriptorSnapshot is not executable state

`DescriptorSnapshot` stores:

- `DescriptorRef`;
- descriptor state and kind;
- Contract and Definition hash values;
- relationship facts;
- package/snapshot identity.

It deliberately stores no concrete `IDescriptor` instance or definition
payload. Deserializing it cannot reconstruct `WorkflowDescriptor.Steps`,
HumanTask outcomes, executor bindings, or generated delegates.

The corrected recovery rule is:

```text
persist exact pin
    -> load current activated registry
    -> resolve exact namespace/id/version
    -> recompute the canonical hashes
    -> require exact structured equality
    -> execute the exact descriptor object returned by validation
```

Any miss or mismatch fails closed and leaves the durable record unchanged.

### 2.2 Current suspension is a split commit

Today:

```text
HumanTaskStepExecutor
    -> IHumanTaskRuntime.CreateAsync
    -> HumanTask store SaveAsync

WorkflowExecutionRunner
    -> WorkflowInstance.Status = Suspended
    -> Workflow store SaveAsync
```

A process failure between the writes exposes a HumanTask whose Workflow still
appears Running. Phase 9b must move persistence ownership out of
`HumanTaskStepExecutor`: the executor prepares a suspension intent; a
runner-owned suspension committer persists both state changes in one transaction.

### 2.3 Open object graphs are not durable AOT contracts

The following durable/event fields currently contain `object?`:

- `WorkflowInstance.Variables`;
- `WorkflowInstance.StepVariables`;
- `WorkflowStepResult.Output`;
- `HumanTaskInstance.Input`;
- `HumanTaskInstance.Output`;
- `HumanTaskCompletedEvent.Result`.

Container-only copying does not isolate their mutable values. Generic
`object` JSON serialization also does not preserve the concrete type under
NativeAOT and commonly reloads as `JsonElement`.

Phase 9b therefore introduces an immutable `RuntimeStateValue` envelope backed
by an explicitly registered source-generated JSON contract. Open objects may
exist at request or ephemeral execution boundaries, but they must be captured
into `RuntimeStateValue` before entering a durable model, event, or transaction.

### 2.4 Current Store APIs do not carry tenant authority

`GetAsync(string)`, `GetByIdAsync(string)`, and
`GetByWaitingHumanTaskId(string)` cannot prove tenant isolation. Phase 9b makes
tenant scope part of every instance key, correlation, CAS predicate, foreign
key, unique constraint, and completion event.

### 2.5 Durable audit acceptance is not reliable state coupling

`IAuditSink` is a write contract. Phase 9b can prove that an accepted envelope
survives restart and retains Duplicate/Conflict behavior. It cannot prove:

```text
state commit -> AuditEnvelope eventually reaches IAuditSink
```

Accountability recording and Workflow lifecycle notification remain post-commit
in Phase 9b. #25 must append a delivery record inside the state transaction and
deliver it later.

---

## 3. Goal

Deliver the first production-grade durable runtime authority using one
PostgreSQL provider.

A Workflow suspension commits its HumanTask and Workflow state atomically with
immutable Descriptor Pins. Under a compatible deployment, a fresh process can
load the tenant-scoped records, validate exact executable descriptors, restore
explicitly registered state payloads with their original CLR semantics, complete
the HumanTask, and resume the Workflow without stale overwrite.

Missing executable descriptors, hash mismatch, state-contract mismatch,
concurrency conflict, ambiguous commit outcome, and incompatible schema all
fail closed while preserving durable evidence for diagnosis and reconciliation.

Also provide a durable `IAuditSink` that implements the finalized Phase 9a sink
contract. Reliable state-to-Accountability/event delivery stays in #25.

---

## 4. Boundary

### 4.1 In scope

- Exactly one relational production provider: PostgreSQL through direct Npgsql.
- Provider Kernel:
  - one configured `NpgsqlDataSource`;
  - connection/session ownership;
  - ambient transaction joining;
  - provider exception translation;
  - health/startup compatibility validation.
- Ordered, checksummed, lock-protected, repeatable migrations.
- Explicit tenant-scoped Runtime instance keys and query scopes.
- Immutable exact Descriptor Pins for Workflow and HumanTask state.
- Exact Registry-based pin capture and resolution.
- Explicit AOT-safe Runtime State contracts using application-owned
  `JsonSerializerContext` / `JsonTypeInfo`.
- Deep state snapshot semantics.
- Workflow and HumanTask durable stores with explicit create and CAS update
  operations.
- Atomic Workflow suspension commit.
- Immutable durable DescriptorSnapshot evidence/index store.
- Commit-response-loss reconciliation for suspension.
- Durable `IAuditSink` implementing the Phase 9a shared contract suite.
- Runner-free shared Store/Transaction contract cases.
- Real PostgreSQL restart, crash-window, concurrency, migration, and
  independent-process fixtures.
- A linux-x64 NativeAOT publish-link-run fixture which executes the real Npgsql
  provider and database round trip.
- A same-transaction enlistment probe proving that #25 can co-locate its
  PostgreSQL Outbox store in the Provider Kernel without changing Runtime
  abstractions.

### 4.2 Out of scope

- Executable Descriptor payload persistence or reconstruction.
- Descriptor activation, Registry mutation, package import, rollback, or remote
  synchronization.
- Multiple database providers.
- EF Core as the Phase 9b production provider.
- General repository/UoW replacement.
- Exposing `DbContext`, `DbConnection`, `DbTransaction`, `NpgsqlConnection`,
  `NpgsqlTransaction`, provider enums, or provider exceptions through Runtime
  abstractions.
- HumanTask completion-event reliable delivery after its state commit.
- Workflow/Capability state-to-Accountability reliable delivery.
- Outbox schema/API, append semantics, dispatcher, retry, inbox, or broker
  integration.
- Agent Tool pre-dispatch reconciliation (#70).
- Durable Agent Memory stores (#55 or a Phase 9b+ follow-up).
- Organization, DataPermission, DescriptorDraft, cache consistency, audit query
  product, retention UI, WORM, lineage, or compliance platform.
- Encrypt-at-rest platform design. Deployment/database encryption remains an
  operational requirement; application-level envelope encryption needs a
  separate contract.

### 4.3 Compatibility position

This is an intentional Runtime Store contract cutover:

- the old string-only lookup APIs are removed from the mainline;
- ambiguous `SaveAsync` upsert semantics are replaced by explicit create/CAS;
- durable fields/events no longer carry open `object?`;
- in-memory stores are migrated to the same contracts and remain test/local
  providers;
- the old Runtime Store exceptions in Metadata are retired as part of the
  cutover; provider/store failures move to Runtime Persistence Abstractions,
  while Descriptor Pin validation failures remain Metadata-owned. No duplicate
  exception hierarchy or dependency-reversing type forward is introduced;
- there is no durable fallback that silently invokes the old shallow-snapshot
  or tenant-unscoped path.

### 4.4 Provider support tiers

Phase 9b supports two explicit provider tiers:

```text
PostgreSQL Provider — Full Durable Runtime Provider
    -> Add/CAS Store contracts
    -> atomic Runtime transaction across participating Stores
    -> suspension atomicity and rollback
    -> process durability and restart recovery
    -> migrations and schema compatibility
    -> real PostgreSQL NativeAOT evidence

InMemory Provider — Full Semantic Runtime Provider
    -> the same Add/CAS Store contracts
    -> one atomic Runtime transaction across participating Stores
    -> the same observable suspension atomicity and rollback semantics
    -> no process durability claim
    -> no restart recovery claim
    -> no migration or database NativeAOT claim
```

The Workflow Runner always uses the same
`IRuntimeTransactionCoordinator -> Stores` mainline. InMemory is not permitted
to retain split HumanTask/Workflow commits merely because it is local or used by
tests.

Any future provider wired into the Workflow suspension mainline must qualify as
a Full Semantic Runtime Provider. A Store-only fake that cannot atomically
coordinate all suspension participants may be used inside a narrow unit test,
but it is not registered, documented, or advertised as a supported Workflow
Runtime provider.

---

## 5. Ownership and Dependency Direction

### 5.1 Contract ownership

| Concern | Canonical owner |
|---|---|
| Descriptor identity and canonical hashes | Metadata abstractions/runtime |
| Workflow state transitions | Workflow runtime |
| HumanTask state transitions and assignee preparation | HumanTask runtime |
| Runtime instance key, state envelope, transaction contract | Runtime Persistence Abstractions |
| Audit acceptance semantics | Accountability `IAuditSink` |
| SQL, schema, migration, sessions, provider mapping | PostgreSQL provider |
| Reliable delivery | #25 Outbox |

### 5.2 Proposed projects

```text
src/Runtime/Persistence/
  CrestCreates.Runtime.Persistence.Abstractions/
    RuntimeInstanceKey
    RuntimeTenantScope
    RuntimeStateValue
    Runtime State contract interfaces
    IRuntimeTransactionCoordinator
    provider-neutral transaction/store failures

src/Persistence/
  CrestCreates.Runtime.Persistence.PostgreSql/
    Provider Kernel
    migrations
    Workflow/HumanTask/Snapshot/Audit implementations
    provider-owned persistence DTOs and generated JSON context

tests/Shared/
  CrestCreates.Runtime.Persistence.Testing/
    runner-free Store/Transaction contract drivers and cases

tests/Persistence/
  CrestCreates.Runtime.Persistence.PostgreSql.Tests/
  CrestCreates.Runtime.Persistence.PostgreSql.CrashWorker/
  CrestCreates.Runtime.Persistence.PostgreSql.AotHost/
  CrestCreates.Runtime.Persistence.PostgreSql.AotFixture/
```

The PostgreSQL project may reference Workflow, HumanTask, Metadata, and
Accountability **Abstractions** to implement their interfaces. It must not
reference concrete Workflow/HumanTask runtimes or any Platform/Web project.

Runtime projects do not reference the PostgreSQL provider. A composition root
opts into it.

---

## 6. Core Contracts

### 6.1 RuntimeInstanceKey and RuntimeTenantScope

```csharp
public readonly record struct RuntimeInstanceKey(
    string? TenantId,
    string InstanceId);

public readonly record struct RuntimeTenantScope(string? TenantId);
```

Normative rules:

- `InstanceId` is non-empty and is compared with `StringComparison.Ordinal`.
- `TenantId == null` means exact host scope, never wildcard scope.
- Query APIs require `RuntimeTenantScope`; no overload implies “all tenants”.
- Cross-tenant administration, if added later, needs a separately authorized
  interface and cannot reuse `null`.
- A HumanTask correlated to a Workflow must have the same tenant component.
- `HumanTaskCompletedEvent` and `WorkflowContinuationRequest` carry the exact
  HumanTask key and Workflow key, not only bare IDs.

PostgreSQL must not use a nullable `tenant_id` directly inside ordinary unique
constraints. PostgreSQL treats nulls as distinct unless special semantics are
selected. The schema uses:

```text
tenant_scope_kind smallint not null  # 0 = host, 1 = tenant
tenant_id         text     not null  # "" only when kind = host
```

with a check constraint:

```text
(kind = 0 and tenant_id = '')
or
(kind = 1 and tenant_id <> '')
```

All primary/unique/foreign keys begin with `(tenant_scope_kind, tenant_id)`.
This avoids sentinel collision and makes host uniqueness explicit.

### 6.2 RuntimeDescriptorPin

`RuntimeDescriptorPin` belongs with Metadata contracts because it describes an
immutable Descriptor identity, not database mapping.

```csharp
public sealed record RuntimeDescriptorPin
{
    public required DescriptorRef Ref { get; init; }
    public required CanonicalHash ContractHash { get; init; }
    public required CanonicalHash DefinitionHash { get; init; }
    public string? SnapshotId { get; init; }
}
```

Why structured `CanonicalHash` rather than only strings:

- `CanonicalHash.Value` is a digest of the canonical payload;
- algorithm version, contract version, purpose, scope, and canonical shape
  version are metadata on the `CanonicalHash`;
- the digest alone does not identify those hash-contract fields;
- durable recovery must fail closed when the hash profile changes, even if a
  digest happens to be equal.

Pin validation requires:

- non-empty namespace and ID;
- an exact non-null version;
- exact expected Descriptor kind/namespace for the owning runtime;
- structured Contract hash equality;
- structured Definition hash equality;
- `ContractHash.Purpose == Contract`;
- `DefinitionHash.Purpose == Definition`;
- internal-full scope for executable recovery;
- optional snapshot evidence entry agreement on Ref and digest values.

The Phase 6f `SnapshotEntry` currently stores only digest strings. Therefore a
Snapshot can corroborate Ref and digest values, but only the Runtime Pin and
live recomputation can prove the structured hash profile.

### 6.3 Pin capture and resolution

```csharp
public interface IRuntimeDescriptorPinResolver<TDescriptor>
    where TDescriptor : IVersionedDescriptor
{
    ResolvedRuntimeDescriptor<TDescriptor> Capture(TDescriptor descriptor);

    ResolvedRuntimeDescriptor<TDescriptor> Resolve(
        RuntimeDescriptorPin pin);
}

public sealed record ResolvedRuntimeDescriptor<TDescriptor>
    where TDescriptor : IVersionedDescriptor
{
    public required TDescriptor Descriptor { get; init; }
    public required RuntimeDescriptorPin Pin { get; init; }
}
```

`Resolve` uses the current activated type-specific Registry and
`IDescriptorStableHashBuilder`. It does not use `IGlobalDescriptorRegistry`
because that registry does not provide a sufficiently strong exact-version
contract for this recovery path.

Execution consumes `ResolvedRuntimeDescriptor.Descriptor` directly. It must not
validate a pin and then query the Registry again, because that would reopen a
validate/use race if activation changes.

Registry activation itself is expected to publish an immutable usable Registry
view. Hot-swap semantics are outside Phase 9b; a mutable Registry that changes
an object after `Resolve` violates this contract.

Pin validation is intentionally lazy:

- Phase 9b validates an exact Pin when a durable instance is loaded for
  execution or transition.
- Provider/Host startup validates database schema compatibility, Runtime State
  registrations, and static composition; it does not scan every persisted
  Workflow/HumanTask Pin.
- A dormant instance whose old Descriptor version is absent does not by itself
  prevent Host startup.
- Attempting to execute or transition that instance fails closed and leaves its
  durable state unchanged.
- A compatible deployment must retain every exact Descriptor version required
  by live durable instances.
- Deployment-wide inventory analysis, activation blocking, and “safe to remove
  old Descriptor version” governance remain outside Phase 9b.

### 6.4 RuntimeStateValue

Durable Runtime state uses an immutable envelope:

```csharp
public sealed record RuntimeStateValue
{
    public required string TypeId { get; init; }
    public DescriptorRef? SchemaRef { get; init; }
    public required string JsonPayload { get; init; }
}
```

Rules:

- `TypeId` is an explicit stable contract identifier such as
  `procurement.request-state/v1`; it is never an assembly-qualified CLR name.
- `SchemaRef`, when present, must be exact-versioned.
- `JsonPayload` contains exactly one JSON value.
- no `JsonElement` is retained as the domain representation;
- no payload setter or mutable byte buffer is exposed;
- no type discriminator inside the JSON is trusted to select a CLR type;
- TypeId selects one pre-registered contract before deserialization.
- `RuntimeStateValue? == null` means “no value/output was supplied”.
- a typed null is a non-null `RuntimeStateValue` with the registered TypeId and
  JSON payload `null`;
- untyped `Capture(null)` is rejected because it cannot select a stable TypeId;
  callers use `Capture<T>(value)` for typed nulls.

The listed durable fields change to `RuntimeStateValue`:

```text
WorkflowInstance.Variables       Dictionary<string, RuntimeStateValue>
WorkflowInstance.StepVariables   Dictionary<string, RuntimeStateValue>
WorkflowStepResult.Output        RuntimeStateValue?
HumanTaskInstance.Input          RuntimeStateValue?
HumanTaskInstance.Output         RuntimeStateValue?
HumanTaskCompletedEvent.Result   RuntimeStateValue?
WorkflowContinuationRequest.Result RuntimeStateValue?
```

Open `object?` may remain on explicit request/ephemeral step-result APIs for
short business DX, but the Runtime captures it before:

- mutating a durable instance;
- opening a database transaction;
- creating a durable-capable event.

The durable `WorkflowExecutionRequest` mainline carries already captured
`RuntimeStateValue` entries (normally produced through a small injected state
factory/bag builder). A legacy `Dictionary<string, object?>` overload must not
remain as a silent serialization fallback. If a short source-compatibility shim
is required during the implementation PR, it delegates to explicit capture,
rejects untyped null/unregistered values, and is removed before Phase 9b exits.

### 6.5 Runtime State registration

```csharp
public interface IRuntimeStateContractRegistry
{
    RuntimeStateValue Capture(object? value);
    RuntimeStateValue Capture<T>(T value);
    object? Restore(RuntimeStateValue value);
    T Restore<T>(RuntimeStateValue value);
}

public interface IRuntimeStateContractContributor
{
    void Contribute(RuntimeStateContractBuilder builder);
}
```

Each contribution binds:

```text
stable TypeId
    + exact CLR Type
    + optional exact SchemaRef
    + JsonTypeInfo<T> from an application-owned generated context
```

The concrete typed registration invokes
`JsonSerializer.Serialize(value, JsonTypeInfo<T>)` and
`JsonSerializer.Deserialize(payload, JsonTypeInfo<T>)`. The mainline never
calls an overload that accepts only `Type`, never uses
`DefaultJsonTypeInfoResolver`, and never scans assemblies.

The existing JSON Contract Root BuildTasks remain the source of
`[JsonSerializable]` roots and immutable generated manifests. Runtime State
contributors reference generated `JsonTypeInfo<T>` properties. Startup
validation requires every contributed CLR type to be present in the paired
generated `AllDirectRootTypes` manifest.

Application-owned contexts declare state types through the existing explicit
root mechanism, for example:

```csharp
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonContractExplicitRoot(typeof(ProcurementWorkflowState))]
public sealed partial class ProcurementRuntimeStateJsonContext
    : JsonSerializerContext
{
}
```

BuildTasks, not handwritten `[JsonSerializable]`, add the root and generated
manifest. The contributor then binds a stable TypeId to
`ProcurementRuntimeStateJsonContext.Default.ProcurementWorkflowState`.

Startup fails on:

- duplicate TypeId;
- one CLR type mapped to incompatible TypeIds in the same Host;
- missing generated `JsonTypeInfo`;
- missing generated manifest root;
- open generic, pointer, by-ref-like, or unsupported root;
- invalid/ambiguous SchemaRef;
- a TypeId or payload exceeding configured bounded limits.

### 6.6 Deep snapshot semantics

`WorkflowInstance.Snapshot()` and `HumanTaskInstance.Snapshot()` can no longer
copy only containers while retaining opaque mutable values.

With immutable `RuntimeStateValue`:

- framework collections are copied;
- each state value can be safely reused because its fields are immutable;
- every `Restore` call constructs a new CLR object through its registered
  `JsonTypeInfo`;
- provider persistence DTOs are freshly materialized and never escape.

Tests must mutate:

- the original request object after capture;
- the first restored object after read;
- nested list/dictionary members.

Neither mutation may affect later reads.

### 6.7 Explicit create and CAS store contracts

The old “upsert `SaveAsync`” contract does not distinguish creation from a
stale update. Phase 9b replaces it.

Conceptual Workflow contract:

```csharp
public interface IWorkflowInstanceStore
{
    Task AddAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        WorkflowInstance instance,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    Task<WorkflowInstance?> GetAsync(
        RuntimeInstanceKey key,
        CancellationToken cancellationToken = default);

    Task<WorkflowInstance?> GetByWaitingHumanTaskAsync(
        RuntimeInstanceKey humanTaskKey,
        CancellationToken cancellationToken = default);
}
```

Conceptual HumanTask contract:

```csharp
public interface IHumanTaskInstanceStore
{
    Task AddAsync(
        HumanTaskInstance instance,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        HumanTaskInstance instance,
        long expectedRevision,
        CancellationToken cancellationToken = default);

    Task<HumanTaskInstance?> GetAsync(
        RuntimeInstanceKey key,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(
        RuntimeInstanceKey workflowKey,
        CancellationToken cancellationToken = default);

    // Every other assignee/candidate/organization/position query also requires
    // RuntimeTenantScope as its first authority argument.
}
```

Instances carry `long Revision`:

- new unsaved instance: `Revision == 0`;
- successful insert stores revision 1;
- update SQL includes `WHERE revision = expectedRevision`;
- successful update increments revision exactly once;
- zero updated rows means provider-neutral `RuntimeConcurrencyException`;
- no update method falls back to insert;
- no insert method falls back to update.

The old `ConcurrencyStamp` is removed from the new mainline instead of keeping
two concurrency truths. A short compile-time migration shim may exist only in
the implementation PR that changes all call sites; it must not ship as a
second runtime contract.

### 6.8 IRuntimeTransactionCoordinator

```csharp
public interface IRuntimeTransactionCoordinator
{
    ValueTask ExecuteAsync(
        Func<CancellationToken, ValueTask> work,
        CancellationToken cancellationToken = default);

    ValueTask<T> ExecuteAsync<T>(
        Func<CancellationToken, ValueTask<T>> work,
        CancellationToken cancellationToken = default);
}
```

Normative behavior:

- propagation is fixed to Required:
  - no ambient Runtime transaction: open one;
  - existing ambient Runtime transaction: join it;
- no `RequiresNew`, provider choice, isolation enum, or transaction handle is
  exposed;
- root success commits;
- exception/cancellation before commit rolls back;
- nested success does not independently commit;
- stores automatically use the current provider session;
- a store call outside a transaction opens its own short transaction;
- one ambient database session cannot be used by concurrent child operations;
  the Provider Kernel detects this and fails closed;
- caller cancellation is honored before/during database work, but a successful
  COMMIT is never reported as cancelled merely because cancellation raced the
  acknowledgement;
- if cancellation and COMMIT acknowledgement loss make the outcome ambiguous,
  `RuntimeTransactionCommitUnknownException` wins over a false cancellation or
  rollback claim;
- there is no automatic replay after an ambiguous COMMIT outcome.

The provider uses Read Committed plus explicit CAS predicates and unique
constraints. Serializable is not the default because the required correctness
comes from operation-specific constraints, not from retrying arbitrary business
delegates.

### 6.9 Provider-neutral transaction failures

Required failure categories:

```text
RuntimeConcurrencyException
RuntimeDuplicateEntityException
RuntimePersistenceUnavailableException
RuntimeTransactionCommitUnknownException
RuntimePersistenceContractException
RuntimeStateContractException
RuntimeDescriptorPinValidationException  # Metadata-owned
```

No `PostgresException`, `NpgsqlException`, SQLSTATE, connection, command, or
transaction object escapes the provider. Provider diagnostics may log SQLSTATE
and a generated failure ID internally, but public messages and records contain
only safe provider-neutral codes.

---

## 7. DescriptorSnapshot Evidence Store

### 7.1 Contract

```csharp
public interface IDescriptorSnapshotStore
{
    Task<DescriptorSnapshotWriteResult> WriteAsync(
        DescriptorSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<DescriptorSnapshot?> GetAsync(
        string snapshotId,
        CancellationToken cancellationToken = default);

    Task<SnapshotEntry?> GetEntryAsync(
        string snapshotId,
        DescriptorRef descriptorRef,
        CancellationToken cancellationToken = default);
}
```

Write status:

```text
Accepted   first immutable value for SnapshotId
Duplicate  exact same full persisted snapshot content
Conflict   same SnapshotId, different persisted content
```

Conflict never overwrites the accepted record.

The provider computes a dedicated `DescriptorSnapshotPersistenceHash` over all
persisted Snapshot fields, entries, and relationships with an explicit
canonical writer. It includes Contract/Definition digest values and normalized
collection order. It does not reuse Phase 6f package identity and does not use
ordinary JSON text equality as the immutable-content decision.

The Phase 9b identity contract is frozen as follows. The canonical projection
includes `SnapshotId`, `PackageId`, `PackageVersion`, `CreatedAt`, every
`SnapshotEntry` field (`Ref.Namespace`, `Ref.Id`, `Ref.Version`,
`DescriptorName`, `Kind`, `State`, `ContractHash`, `DefinitionHash`,
`SupersededById`), and every relationship field (`From`, `To`, `Kind`, `Role`,
`SourcePath`, `Strength`, `IsRuntimeBinding`). Descriptor and relationship
collections are sorted by their complete value projection before writing;
reordering either collection is therefore a `Duplicate`, while changing any
persisted field is a `Conflict`. The writer emits one fixed field order and a
versioned profile (`descriptor-snapshot-persistence-v1`); provider code only
stores and compares the resulting digest.

### 7.2 Authority boundary

The Store can answer:

- which refs and hashes a snapshot recorded;
- which relationships it recorded;
- when and from which package it came;
- whether an optional Runtime Pin has matching evidence.

It cannot answer:

- how to instantiate a Descriptor;
- which executor delegate should run;
- whether a Registry binding currently exists;
- whether current code is compatible.

No method returns `IDescriptor`.

### 7.3 SnapshotId caution

Snapshot identity has evolved since the original 6f design. The current builder
derives `SnapshotId` from the structured package manifest hash, which includes
the exact descriptor hash entries. The durable Store still treats SnapshotId as
caller-owned identity and checks the complete persisted content. It does not
assume the prefix is collision-proof or recompute executable authority from it.

---

## 8. Workflow/HumanTask Mainline Changes

### 8.1 Workflow creation

Before the initial Workflow insert:

1. Resolve the chosen `WorkflowDescriptor`.
2. Capture a structured Workflow `RuntimeDescriptorPin`.
3. Capture every input variable through `IRuntimeStateContractRegistry`.
4. Fail before any transaction when state is unregistered.
5. Create `WorkflowInstance` with tenant-scoped key, pin, revision 0.
6. Insert it through the Store; the provider commits revision 1.
7. Publish post-commit lifecycle/accountability notification under the existing
   Phase 9a best-effort semantics.

### 8.2 HumanTask preparation

HumanTask creation is split into:

```text
prepare
    -> resolve exact descriptor
    -> capture pin
    -> resolve assignee
    -> create unsaved HumanTaskInstance

persist
    -> standalone HumanTask: AddAsync in its own transaction
    -> Workflow suspension: AddAsync inside suspension transaction
```

A narrow `IHumanTaskInstancePreparer` owns preparation so Workflow does not
duplicate HumanTask descriptor or assignee rules.

`HumanTaskStepExecutor` returns a prepared suspension intent. It does not call a
Store and does not commit.

### 8.3 Atomic suspension commit

`WorkflowExecutionRunner` owns the business transition and calls an internal
`IWorkflowSuspensionCommitter`.

Pre-transaction:

1. Validate the current Workflow pin and retain its resolved descriptor object.
2. Prepare HumanTask and its pin.
3. Capture/validate every state value.
4. Validate tenant equality and Workflow step correlation.
5. Allocate stable caller-owned:
   - `SuspensionOperationId`;
   - HumanTask instance ID;
   - Workflow and HumanTask keys.
6. Build a detached post-state Workflow snapshot:
   - status `Suspended`;
   - waiting HumanTask key;
   - current step result;
   - immutable pins;
   - expected old revision.

Transaction:

```text
IRuntimeTransactionCoordinator.ExecuteAsync
    -> HumanTaskStore.AddAsync(preparedTask)
    -> optional Snapshot evidence entry check
    -> WorkflowStore.UpdateAsync(postState, expectedRevision)
    -> append immutable SuspensionOperationReceipt
    -> commit
```

The CAS update occurs after the task insert intentionally: a stale Workflow
revision rolls the entire transaction back, including the task.

After commit:

- update the caller-visible detached instances from the committed result;
- publish Workflow suspended lifecycle/accountability notification;
- never reinterpret a committed suspension as failed because post-commit
  notification failed.

### 8.4 Correlation constraints

Within one tenant:

- a Workflow has at most one current waiting HumanTask;
- one pending HumanTask can suspend at most one Workflow;
- a pending `(WorkflowInstanceId, WorkflowStepId)` correlation is unique;
- HumanTask.Workflow key and Workflow.WaitingHumanTask key must be reciprocal;
- both pins are stored on their owning records;
- a composite deferred foreign key may enforce the reciprocal waiting-task
  reference because the Workflow row already exists and both changes occur in
  one transaction.

### 8.5 Crash semantics

“Expose neither” means no partial **suspension effect**:

- the Workflow row was created earlier and remains visible at its previous
  Running revision;
- the inserted HumanTask is not visible;
- no Suspended revision is visible;
- no post-commit notification is emitted.

The real crash test terminates a worker process after the HumanTask INSERT has
reached PostgreSQL but before the Workflow UPDATE/COMMIT. A fresh process and
fresh connection then query the state.

An exception-only fault-injection test is useful but does not replace the
independent-process crash test.

### 8.6 Commit response loss and reconciliation

A COMMIT can succeed while its acknowledgement is lost. The Provider must not:

- claim rollback;
- retry the arbitrary delegate;
- create a new HumanTask ID;
- overwrite a later revision.

The caller receives `RuntimeTransactionCommitUnknownException`. Reconciliation
first queries an immutable `SuspensionOperationReceipt` by tenant scope and
`SuspensionOperationId`, then verifies its Workflow/HumanTask keys, transition
revisions, Pins, and operation integrity. Current rows may already have advanced
through a later legitimate completion/continuation; reconciliation verifies
that their revision/correlation history is compatible with the receipt rather
than requiring them to equal the original Suspended post-state forever.

The receipt is not an Outbox record and has no delivery state. It is a compact
provider-neutral commit fact:

```text
tenant scope
SuspensionOperationId
Workflow key
HumanTask key
Workflow from/to revision
operation integrity
committed-at
```

Same operation ID plus identical integrity is a reconciliation duplicate. Same
operation ID plus different integrity is a conflict. The receipt is inserted in
the suspension transaction and is immutable.

Operation integrity is a structured `CanonicalHash` produced by a dedicated
canonical writer over tenant scope, operation ID, both instance keys,
from/to revision, both full Descriptor Pins, correlation, and hashes of every
persisted state value. It does not use `ToString()`, ordinary JSON text
equality, reflection serialization, or a hash of only the task ID.

Possible outcomes:

| Observation | Result |
|---|---|
| matching immutable receipt and compatible current lineage | committed |
| no receipt, pre-state Workflow, and no task | not committed; an identical operation may be retried |
| receipt conflict, partial effect, or incompatible lineage | invariant violation; fail closed |
| database unavailable / observation inconclusive | indeterminate; fail closed |

The real response-loss fixture lets a worker commit and terminate before
returning an application response. A fresh process reconciles the committed
state.

### 8.7 Restart and recovery

For a suspended Workflow:

1. Load by exact `RuntimeInstanceKey`.
2. Load the waiting HumanTask by exact tenant-scoped key.
3. Validate reciprocal correlation and revisions.
4. Resolve the Workflow Pin from the current activated Workflow Registry.
5. Resolve the HumanTask Pin from the current activated HumanTask Registry.
6. If either Pin has `SnapshotId`, verify the matching evidence entry.
7. Restore Runtime State values through registered TypeIds.
8. Execute only the descriptor objects returned by pin resolution.

Any failure before execution:

- returns/throws a typed fail-closed recovery condition;
- does not alter status, revision, pins, or payload;
- records no fabricated success/failure lifecycle transition;
- leaves raw rows available to provider diagnostics.

### 8.8 HumanTask completion and Workflow continuation

Completion requests and completion events carry:

- HumanTask `RuntimeInstanceKey`;
- Workflow `RuntimeInstanceKey`, when correlated;
- completion event ID;
- exact tenant scope;
- immutable `RuntimeStateValue?` result.

HumanTask completion validates the HumanTask pin before its CAS transition.
Workflow continuation validates the Workflow pin and reciprocal correlation
before its CAS transition.

Phase 9b does **not** claim reliable completion-to-continuation delivery:

```text
HumanTask Completed commit
    -> process crash before local event/continuation
    -> Workflow may remain Suspended
```

The durable completion record and stable event ID make the fact replayable.
#25 must append the completion delivery record in the same transaction and
deliver it later.

---

## 9. PostgreSQL Provider Design

### 9.1 Why direct Npgsql

As of this design review:

- Microsoft documents EF Core NativeAOT and query precompilation as highly
  experimental and recommends against production deployment:
  <https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries>
- Npgsql documents first-class NativeAOT/trimming support from 8.0:
  <https://www.npgsql.org/doc/compatibility.html#nativeaot-and-trimming>

Phase 9b therefore uses direct commands and readers. This does not change the
separate support level of existing EF Core integrations.

### 9.2 Provider rules

- `NpgsqlDataSource` is the connection-pool root.
- `NpgsqlSlimDataSourceBuilder` is preferred when the required feature set is
  sufficient.
- No dynamic JSON POCO mapping.
- JSON is serialized/deserialized before provider binding with generated
  `JsonTypeInfo`; parameters contain text/UTF-8 data and explicit PostgreSQL
  types.
- No reflection mapper or runtime materializer generation.
- Column names and SQL are hand-written constants.
- Dynamic identifiers are limited to a validated configured schema name and
  are safely quoted.
- Cancellation is passed to every async open/command/reader operation.
- Commands are bounded by configured timeouts.
- No hidden retry of a transaction delegate.

### 9.3 Logical schema

```text
crest_runtime_schema_migrations
  version PK
  name
  checksum
  applied_at

crest_descriptor_snapshots
  snapshot_id PK
  package_id
  package_version
  created_at
  content_hash
  snapshot_json

crest_descriptor_snapshot_entries
  snapshot_id FK
  descriptor_namespace
  descriptor_id
  descriptor_version
  contract_hash_value
  definition_hash_value
  ...
  PK(snapshot_id, namespace, id, version)

crest_workflow_instances
  tenant_scope_kind
  tenant_id
  instance_id
  revision
  status
  workflow_pin_json
  waiting_human_task_id
  suspension_operation_id
  state_json
  lifecycle fields
  PK(tenant_scope_kind, tenant_id, instance_id)

crest_human_task_instances
  tenant_scope_kind
  tenant_id
  instance_id
  revision
  status
  human_task_pin_json
  workflow_instance_id
  workflow_step_id
  suspension_operation_id
  state_json
  assignee/candidate fields
  completion fields
  PK(tenant_scope_kind, tenant_id, instance_id)

crest_runtime_operation_receipts
  tenant_scope_kind
  tenant_id
  operation_id
  operation_kind
  workflow_instance_id
  human_task_instance_id
  workflow_from_revision
  workflow_to_revision
  structured_integrity fields/json
  committed_at
  PK(tenant_scope_kind, tenant_id, operation_id)

crest_accountability_envelopes
  sink_id
  audit_id
  structured_integrity fields/json
  first_accepted_at
  envelope_json
  PK(sink_id, audit_id)
```

Final column decomposition is an implementation-plan decision, but the
tenant/revision/status/correlation/operation fields used by constraints and CAS
must be first-class columns rather than hidden only inside JSON.

List query ordering is contract-owned, not database-default collation:

- pending HumanTasks: `CreatedAt`, then `InstanceId` ordinal;
- Snapshot entries: Namespace, ID, Version ordinal/numeric;
- test-driver Audit reads: AuditId ordinal.

The provider uses an explicit `C`/bytewise ordering where safe or sorts detached
bounded results with `StringComparer.Ordinal`; it never relies on the database's
deployment-local default collation.

### 9.4 Persistence DTOs

Provider-owned DTOs:

- are not public Runtime models;
- contain no Npgsql types;
- use a provider-owned source-generated `JsonSerializerContext`;
- store Runtime State as `RuntimeStateValue`, not `object`;
- validate bounded payload sizes before SQL;
- are mapped to new domain instances on every read.

### 9.5 CAS SQL

Conceptually:

```sql
update crest_workflow_instances
set revision = revision + 1,
    status = @status,
    ...
where tenant_scope_kind = @tenant_scope_kind
  and tenant_id = @tenant_id
  and instance_id = @instance_id
  and revision = @expected_revision;
```

Affected rows:

- one: success, return new revision;
- zero: `RuntimeConcurrencyException`;
- more than one: schema/provider invariant failure.

### 9.6 Transaction session ownership

The PostgreSQL project has one internal ambient session accessor shared by its
Store implementations. The public coordinator exposes only a callback.

#25's PostgreSQL Outbox implementation is expected to be co-located in or built
as a provider extension against this Provider Kernel. Its public
`IOutboxStore` remains provider-neutral. Phase 9b does not expose Npgsql merely
to make the composition test easy.

---

## 10. Migration Contract

### 10.1 Migration format

Migrations are immutable embedded SQL resources:

```text
V001__runtime_kernel.sql
V002__workflow_humantask.sql
V003__descriptor_snapshot.sql
V004__accountability_sink.sql
```

Each build embeds:

- ordered version;
- stable name;
- SHA-256 checksum of exact resource bytes.

### 10.2 Apply algorithm

1. Open a dedicated migration connection.
2. Acquire a PostgreSQL advisory lock scoped to the configured database/schema.
3. Ensure the migration history table exists.
4. Read applied versions in order.
5. Fail if:
   - an applied checksum differs;
   - the database contains a newer unknown version;
   - versions are missing/out of order;
   - configured schema is invalid.
6. Apply each pending migration in its own transaction.
7. Insert its history row in that same transaction.
8. Release the advisory lock.

An interrupted migration either:

- committed together with its history row; or
- rolled back and is safely attempted on the next startup.

“Reapply” means validate and no-op, not execute DDL again.

### 10.3 Runtime startup

Production default:

- application startup validates schema compatibility;
- automatic apply is explicit Host configuration, not implicit on the first
  request;
- request handlers never run DDL;
- a newer incompatible database fails Host startup.

---

## 11. Durable IAuditSink

### 11.1 Semantics

For `(SinkId, AuditId)`:

| Existing row | Incoming structured Integrity | Result |
|---|---|---|
| none | any valid value | Accepted |
| same | exactly equal | Duplicate |
| same | different | Conflict |

Accepted and Duplicate do not expose `ExistingIntegrity`. Conflict returns the
accepted structured Integrity. All results preserve the provider-local first
acceptance time when known.

### 11.2 Atomic write pattern

The implementation must be correct under concurrent writers. A typical shape:

1. attempt insert with unique `(sink_id, audit_id)`;
2. on unique conflict, read the accepted Integrity in the same short
   transaction;
3. compare the full structured `CanonicalHash`;
4. return Duplicate or Conflict;
5. never update the envelope.

### 11.3 Serialization boundary

- The sink receives a Phase 9a safe immutable snapshot.
- It does not sanitize again.
- It serializes with `AccountabilityJsonSerializerContext`.
- It does not use Npgsql dynamic JSON POCO mapping.
- It stores the complete structured Integrity, not only `.Value`.
- Test-only reads are implemented through
  `IAuditSinkContractDriver`; no product query interface is added.

### 11.4 Explicit reliability limit

This Phase proves:

```text
accepted by durable sink -> survives provider/process restart
```

It does not prove:

```text
Workflow state committed -> envelope accepted by sink
```

That second implication requires #25.

---

## 12. System Invariants

### INV-01 — Suspension is one visibility boundary

HumanTask insert and Workflow Suspended CAS transition are visible together or
neither suspension effect is visible.

### INV-02 — Descriptor Pins are executable recovery authority

Every durable Workflow/HumanTask execution record has an exact immutable Pin.
Executable code comes from the current activated Registry only.

### INV-03 — DescriptorSnapshot is evidence only

No Snapshot Store API returns, constructs, or invokes `IDescriptor`.

### INV-04 — Pin validation is exact and fail-closed

Namespace, ID, version, Contract hash, Definition hash, and all structured hash
metadata match exactly. Execution uses the object returned by validation.

### INV-05 — Pin failure never mutates state

Missing/mismatched descriptors leave status, revision, pins, correlation, and
payload unchanged.

### INV-06 — Durable state has explicit AOT contracts

Every persisted value has a stable TypeId and one registered generated
`JsonTypeInfo`; no reflection or polymorphic fallback exists.

### INV-07 — Unregistered values fail before transaction

No SQL mutation or transaction begins when state capture cannot resolve a
contract.

### INV-08 — State snapshots are deep

Caller mutation of original or previously restored nested payloads cannot alter
stored state or later reads.

### INV-09 — Tenant scope is in every authority key

Reads, writes, CAS, correlations, foreign keys, and uniqueness all include exact
tenant scope. Host scope is exact and is never wildcard.

### INV-10 — One stale revision has at most one winner

No stale writer silently inserts, overwrites, or retries as a fresh transition.

### INV-11 — Correlation is reciprocal and tenant-local

A waiting Workflow and pending HumanTask reference each other in the same
tenant and step operation.

### INV-12 — Provider objects do not escape

Every read returns a detached domain snapshot. Npgsql sessions, readers,
commands, parameters, and exceptions remain provider-internal.

### INV-13 — Commit unknown is not rollback

An ambiguous COMMIT outcome is reconciled by stable identity. It is never
blindly replayed.

### INV-14 — Audit acceptance is immutable

Accepted envelopes survive restart. Duplicate/Conflict never overwrite the
accepted row.

### INV-15 — State-to-notification reliability is not claimed

Post-commit lifecycle, completion, and Accountability calls retain an explicit
crash window until #25.

### INV-16 — Migrations are immutable evidence

Applied migration version/checksum pairs cannot be rewritten. Newer or changed
schema fails closed.

### INV-17 — Outbox can join later without abstraction change

#25 can add its PostgreSQL Store to the same Provider Kernel and transaction
without adding provider types to existing Runtime contracts.

### INV-18 — NativeAOT claims require execution

Build, trim analysis, generated JSON tests, or publish-only success do not prove
NativeAOT. The original native artifact must execute the real PostgreSQL path.

### INV-19 — Provider support tiers are explicit

Any provider wired into the Workflow suspension mainline preserves Add/CAS,
atomic suspension, and rollback semantics. InMemory is a Full Semantic Runtime
Provider and passes those cases; it does not inherit PostgreSQL durability,
restart, migration, or database NativeAOT claims.

---

## 13. Case Matrix

### 13.1 Happy

| ID | Case | Expected |
|---|---|---|
| H01 | Create Workflow with registered typed input | revision 1 and exact Workflow Pin persist |
| H02 | Suspend at HumanTask | task + Workflow Suspended + pins commit together |
| H03 | Restart with matching registries | exact CLR state restores |
| H04 | Complete task after restart | task CAS succeeds and continuation can resume |
| H05 | Resume Workflow | exact pinned Workflow descriptor executes |
| H06 | Persist DescriptorSnapshot | immutable evidence and entries survive restart |
| H07 | Accept AuditEnvelope | Accepted and preserved across restart |
| H08 | Apply empty-database migrations | schema reaches exact supported version |

### 13.2 Boundary

| ID | Case | Expected |
|---|---|---|
| B01 | Same Workflow ID in two tenants | isolated records/revisions |
| B02 | Same HumanTask ID in host and tenant | distinct records |
| B03 | Null tenant query | exact host only |
| B04 | Same old revision raced | one winner |
| B05 | Same suspension operation replayed | reconcile; no second task |
| B06 | Same operation ID with different integrity | conflict; original receipt remains |
| B07 | CLR type renamed, stable TypeId retained | current registered JsonTypeInfo restores |
| B08 | Typed null/no-output distinction | stable documented semantics |
| B09 | Duplicate Snapshot content | Duplicate |
| B10 | Identical Audit retry | Duplicate, original FirstAcceptedAt |
| B11 | Nested transaction | joins root and cannot commit independently |
| B12 | Deterministic list queries | stable ordinal/key order |
| B13 | Host starts with unresolved dormant instance Pin | schema startup may succeed; executing that instance fails closed |

### 13.3 Failure

| ID | Case | Expected |
|---|---|---|
| F01 | Crash after task insert, before Workflow update | pre-state Workflow, no task |
| F02 | Missing Workflow descriptor | fail closed, state unchanged |
| F03 | Missing HumanTask descriptor | fail closed, state unchanged |
| F04 | Definition digest mismatch | fail closed |
| F05 | Contract digest mismatch | fail closed |
| F06 | Equal digest but hash profile mismatch | fail closed |
| F07 | Unregistered input CLR type | fail before transaction |
| F08 | Unknown stored TypeId after restart | fail closed; raw row preserved |
| F09 | SchemaRef mismatch | fail closed |
| F10 | Cross-tenant task correlation | rejected before/inside transaction |
| F11 | Stale Workflow suspension revision | task insert rolled back |
| F12 | COMMIT response lost | typed unknown; reconcile |
| F13 | Same SnapshotId, changed content | Conflict; original remains |
| F14 | Same AuditId, changed Integrity | Conflict; original remains |
| F15 | Applied migration checksum changed | startup fails |
| F16 | Database schema newer than runtime | startup fails |
| F17 | Concurrent commands on one ambient session | fail closed |
| F18 | Payload exceeds configured bound | fail before SQL |

### 13.4 Composition

| ID | Case | Expected |
|---|---|---|
| C01 | State + test enlistment probe commit | both visible |
| C02 | State + test enlistment probe rollback | neither visible |
| C03 | HumanTask completion committed, local event lost | completion durable; no false reliable-resume claim |
| C04 | State committed, Audit sink unavailable | state remains committed; #25 gap explicit |
| C05 | InMemory Full Semantic Provider | passes Add/CAS, atomic suspension, and rollback contracts without durability claims |
| C06 | Fresh DI provider against same DB | no provider tracking/session identity dependency |

### 13.5 NativeAOT

| ID | Case | Expected |
|---|---|---|
| A01 | Publish `CrestCreatesPublishMode=aot` | native link completes |
| A02 | Native migration/health validation | succeeds against PostgreSQL |
| A03 | Native typed state round trip | original CLR contract semantics |
| A04 | Native suspension/restart query | atomic state visible |
| A05 | Native pin resolution | exact structured match |
| A06 | Native Audit retry | Accepted then Duplicate |
| A07 | No dynamic JSON/reflection fallback | static/binary and execution gates pass |

---

## 14. TDD Test Architecture

Tests are created before provider implementation. Test names are a requirements
ledger, not incidental implementation coverage.

### 14.1 Shared runner-free test kit

```text
tests/Shared/CrestCreates.Runtime.Persistence.Testing/
  Workflow/
    IWorkflowInstanceStoreContractDriver.cs
    WorkflowInstanceStoreContractCases.cs
  HumanTask/
    IHumanTaskInstanceStoreContractDriver.cs
    HumanTaskInstanceStoreContractCases.cs
  Snapshots/
    IDescriptorSnapshotStoreContractDriver.cs
    DescriptorSnapshotStoreContractCases.cs
  Transactions/
    IRuntimeTransactionContractDriver.cs
    RuntimeTransactionContractCases.cs
  State/
    IRuntimeStateContractDriver.cs
    RuntimeStateContractCases.cs
  Fixtures/
    RuntimePersistenceContractFixture.cs
  Assertions/
    RuntimePersistenceContractAssertions.cs
```

Like `CrestCreates.Accountability.Testing`, this project:

- references only public provider-neutral contracts;
- contains static async cases;
- contains no xUnit runner dependency;
- contains no Npgsql/Testcontainers dependency;
- does not discover implementations;
- lets each provider test project own `[Fact]` wrappers.

The PostgreSQL test project owns the runner integration and fixture base:

```text
PostgreSqlRuntimeContractTestBase
  -> PostgreSqlRuntimeCollectionFixture
  -> isolated schema lease
  -> driver factory
  -> fresh-provider factory

PostgreSqlWorkflowInstanceStoreContractTests
PostgreSqlHumanTaskInstanceStoreContractTests
PostgreSqlDescriptorSnapshotStoreContractTests
PostgreSqlRuntimeTransactionContractTests
PostgreSqlAuditSinkContractTests
```

The base class contains lifecycle plumbing only. Contract meaning remains in the
runner-free static cases, so adding another provider cannot inherit PostgreSQL
behavior accidentally.

### 14.2 Driver responsibilities

`IWorkflowInstanceStoreContractDriver` can:

- create a store;
- create isolated tenant keys;
- create pinned instances with registered state;
- read raw revision/status through public Store contracts;
- reset only its isolated test scope.

`IRuntimeTransactionContractDriver` can:

- create Store instances sharing one coordinator;
- execute a neutral enlistment probe;
- observe committed/rolled-back probe values;
- never expose a provider transaction.

Restart behavior belongs to the PostgreSQL fixture, not the shared driver,
because “fresh process/provider” is provider lifecycle evidence.

### 14.3 PostgreSQL fixture

```text
PostgreSqlRuntimeCollectionFixture
  -> one postgres:16-alpine container per collection
  -> unique database or schema per test
  -> migration apply
  -> NpgsqlDataSource factory
  -> fresh IServiceProvider factory
  -> no shared open tracking connection
```

Each test:

- owns a unique schema `itest_{guid}`;
- can dispose and rebuild the service provider without recreating the schema;
- cleans only its explicit schema;
- uses database time only where provider acceptance time is authoritative;
- does not depend on test execution order.

### 14.4 Crash worker

An independent executable accepts:

```text
--scenario crash-after-human-task-insert
--scenario commit-without-response
```

For the crash scenario it signals the parent after PostgreSQL confirms the
HumanTask INSERT inside the open transaction. The parent terminates the worker,
waits for connection cleanup, creates a fresh provider, and asserts no partial
suspension.

For response loss the worker commits successfully and terminates without
returning an application acknowledgement. The parent reconciles by stable keys.

No test-only fault hook is added to public production abstractions.

---

## 15. Acceptance Test Skeleton

The following names are normative. The implementation plan may add tests but
must not rename/remove these without a Spec change.

### 15.1 Contract and architecture

```text
RuntimeInstanceKey_Should_RequireExplicitTenantScope
RuntimeTenantScope_Null_ShouldMeanExactHostNotWildcard
RuntimeAbstractions_Should_Not_ExposeProviderTypes
RuntimeProjects_Should_Not_ReferencePostgreSqlProvider
PostgreSqlProvider_Should_ReferenceRuntimeAbstractionsOnly
RuntimeStateContractRegistry_Should_RejectDuplicateTypeId
RuntimeStateContractRegistry_Should_RequireGeneratedRootManifest
RuntimeStateMainline_Should_Not_UseReflectionFallback
DescriptorSnapshot_Should_Not_Be_ExecutableAuthority
StoreContracts_Should_Not_ExposeUpsertSaveAsync
WorkflowRuntimeProviders_Should_DeclareExplicitSupportTier
```

### 15.2 Runtime state

```text
RegisteredStatePayload_ShouldRoundTripWithExactClrType
RegisteredStatePayload_ShouldPreserveStableTypeIdAcrossClrRename
UnregisteredStatePayload_ShouldFailBeforeTransaction
UntypedNullStatePayload_ShouldFailBeforeTransaction
TypedNullStatePayload_ShouldRoundTripWithTypeId
UnknownStateTypeId_OnRestart_ShouldFailClosedWithoutMutation
MismatchedStateSchemaRef_ShouldFailClosed
Snapshot_Should_DeepCopyRegisteredStatePayload
NestedPayloadMutation_ShouldNotAffectLaterRead
OversizedStatePayload_ShouldFailBeforeSql
```

### 15.3 Suspension and recovery

```text
SuspensionCommit_Should_AtomicallyPersistWorkflowAndHumanTask
Crash_BetweenHumanTaskAndWorkflowWrite_ShouldExposeNoPartialSuspension
StaleWorkflowRevision_ShouldRollbackInsertedHumanTask
Restart_WithMatchingDescriptorPin_ShouldResumeWorkflow
Restart_WithMissingWorkflowDescriptor_ShouldFailClosed
Restart_WithMissingHumanTaskDescriptor_ShouldFailClosed
Restart_WithMismatchedDefinitionHash_ShouldFailClosed
Restart_WithMismatchedContractHash_ShouldFailClosed
Restart_WithMismatchedHashProfile_ShouldFailClosed
FailedPinValidation_ShouldNotChangeRevisionOrStatus
CommitResponseLoss_Should_PreserveCommittedStateForReconciliation
IdenticalSuspensionRetry_ShouldNotCreateSecondHumanTask
ConflictingSuspensionOperationRetry_ShouldFailClosed
HostStartup_WithUnresolvedDormantPin_ShouldSucceedSchemaValidation
Execution_WithUnresolvedDormantPin_ShouldFailClosed
```

### 15.4 Tenant and concurrency

```text
TenantScopedLookup_ShouldNotReturnOtherTenantInstance
HostAndTenantSameId_ShouldRemainDistinct
HostScopeUniqueConstraint_ShouldRejectDuplicateId
WaitingHumanTaskCorrelation_ShouldBeTenantScopedAndUnique
CrossTenantWorkflowHumanTaskCorrelation_ShouldFail
ConcurrentTransition_FromSameRevision_ShouldAllowOneWinner
Create_ShouldNotOverwriteExistingInstance
Update_ShouldNotInsertMissingInstance
QueryResults_ShouldHaveDeterministicOrder
```

### 15.5 Descriptor evidence

```text
DescriptorSnapshotStore_ShouldSurviveRestart
DescriptorSnapshotStore_ShouldReturnDetachedSnapshot
DescriptorSnapshotStore_ShouldReturnDuplicateForIdenticalContent
DescriptorSnapshotStore_ShouldRejectSameIdentityDifferentContent
DescriptorPinWithSnapshotId_ShouldRequireMatchingEvidenceEntry
DescriptorPinWithoutSnapshotId_ShouldResolveFromRegistry
SnapshotEntry_ShouldNotReplaceRegistryResolution
```

### 15.6 Audit

```text
AcceptedAuditEnvelope_ShouldSurviveRestart
IdenticalAuditRetry_ShouldReturnDuplicate
ConflictingAuditRetry_ShouldReturnConflict
ConflictingAuditRetry_ShouldNotOverwriteAcceptedEnvelope
PostgreSqlAuditSink_ShouldPassSharedContractCases
DurableAuditSink_ShouldNotAddProductQueryInterface
StateCommit_ShouldNotClaimAuditDeliveryGuarantee
```

### 15.7 Transaction, migration, composition

```text
NestedRuntimeTransaction_ShouldJoinOuterCommit
NestedRuntimeTransaction_ShouldRollbackWithOuterFailure
ConcurrentUseOfAmbientSession_ShouldFailClosed
Migration_ShouldCreateSchemaFromEmptyDatabase
Migration_ShouldReapplyWithoutMutation
Migration_ShouldResumeAfterInterruptedAttempt
Migration_ShouldRejectChangedAppliedChecksum
Migration_ShouldRejectUnknownNewerSchema
OutboxStore_ShouldBeAbleToEnlistWithoutProviderLeak
EnlistedProbeAndState_ShouldCommitTogether
EnlistedProbeAndState_ShouldRollbackTogether
InMemoryRuntimeProvider_ShouldPassAtomicSuspensionContractCases
InMemoryRuntimeProvider_ShouldPassRollbackContractCases
InMemoryRuntimeProvider_ShouldNotClaimProcessDurability
```

### 15.8 NativeAOT

```text
RegisteredStatePayload_ShouldRoundTripUnderNativeAot
PostgreSqlRuntimeFixture_ShouldPublishLinkAndRunNativeBinary
NativeBinary_ShouldExecuteSuspensionAndAuditRetryAgainstPostgreSql
NativeBinary_ShouldEmitRuntimePersistenceSentinel
```

### 15.9 Invariant-to-test ledger

This ledger is normative. One test may prove more than one invariant, but every
invariant needs at least one direct failure-oriented case.

| Invariant | Primary tests |
|---|---|
| INV-01 | `SuspensionCommit_Should_AtomicallyPersistWorkflowAndHumanTask`; `Crash_BetweenHumanTaskAndWorkflowWrite_ShouldExposeNoPartialSuspension` |
| INV-02 | `Restart_WithMatchingDescriptorPin_ShouldResumeWorkflow`; `DescriptorSnapshot_Should_Not_Be_ExecutableAuthority` |
| INV-03 | `DescriptorSnapshot_Should_Not_Be_ExecutableAuthority`; `SnapshotEntry_ShouldNotReplaceRegistryResolution` |
| INV-04 | `Restart_WithMismatchedDefinitionHash_ShouldFailClosed`; `Restart_WithMismatchedHashProfile_ShouldFailClosed` |
| INV-05 | `FailedPinValidation_ShouldNotChangeRevisionOrStatus` |
| INV-06 | `RegisteredStatePayload_ShouldRoundTripWithExactClrType`; `RuntimeStateMainline_Should_Not_UseReflectionFallback` |
| INV-07 | `UnregisteredStatePayload_ShouldFailBeforeTransaction` |
| INV-08 | `Snapshot_Should_DeepCopyRegisteredStatePayload`; `NestedPayloadMutation_ShouldNotAffectLaterRead` |
| INV-09 | `TenantScopedLookup_ShouldNotReturnOtherTenantInstance`; `HostScopeUniqueConstraint_ShouldRejectDuplicateId` |
| INV-10 | `ConcurrentTransition_FromSameRevision_ShouldAllowOneWinner`; `Update_ShouldNotInsertMissingInstance` |
| INV-11 | `WaitingHumanTaskCorrelation_ShouldBeTenantScopedAndUnique`; `CrossTenantWorkflowHumanTaskCorrelation_ShouldFail` |
| INV-12 | `RuntimeAbstractions_Should_Not_ExposeProviderTypes`; `DescriptorSnapshotStore_ShouldReturnDetachedSnapshot` |
| INV-13 | `CommitResponseLoss_Should_PreserveCommittedStateForReconciliation` |
| INV-14 | `AcceptedAuditEnvelope_ShouldSurviveRestart`; `ConflictingAuditRetry_ShouldNotOverwriteAcceptedEnvelope` |
| INV-15 | `StateCommit_ShouldNotClaimAuditDeliveryGuarantee` |
| INV-16 | `Migration_ShouldRejectChangedAppliedChecksum`; `Migration_ShouldRejectUnknownNewerSchema` |
| INV-17 | `OutboxStore_ShouldBeAbleToEnlistWithoutProviderLeak`; `EnlistedProbeAndState_ShouldRollbackTogether` |
| INV-18 | `PostgreSqlRuntimeFixture_ShouldPublishLinkAndRunNativeBinary`; `NativeBinary_ShouldExecuteSuspensionAndAuditRetryAgainstPostgreSql` |
| INV-19 | `WorkflowRuntimeProviders_Should_DeclareExplicitSupportTier`; `InMemoryRuntimeProvider_ShouldPassAtomicSuspensionContractCases`; `InMemoryRuntimeProvider_ShouldNotClaimProcessDurability` |

---

## 16. Red-Green-Review Slices

### Slice 1 — Freeze contracts

Red:

- add all architecture tests;
- add shared test kit interfaces/cases;
- add compile failures for old string-only APIs and open durable state fields.

Green:

- introduce tenant keys, state envelopes, pins, revision, new Store contracts;
- adapt in-memory providers.

Review:

- verify there is one mainline;
- verify no provider/API implementation has started before contracts stabilize.

### Slice 2 — State and Pin correctness

Red:

- registration, manifest, deep-copy, missing TypeId, missing/mismatch Pin cases.

Green:

- Runtime State Registry;
- Pin capture/resolution;
- source-generated JSON integration.

Review:

- search for reflection/default resolver/polymorphic fallback;
- verify structured hash equality;
- verify execution consumes the validated object.

### Slice 3 — Provider Kernel and migrations

Red:

- empty/apply/reapply/interrupted/checksum/newer-schema cases;
- transaction join/rollback cases.

Green:

- direct Npgsql kernel;
- embedded migrations;
- provider-neutral failure translation.

Review:

- inspect every public type dependency;
- inspect cancellation, timeouts, and session lifetime.

### Slice 4 — Workflow/HumanTask Stores and suspension

Red:

- tenant, explicit create/update, CAS, reciprocal correlation, atomic
  suspension cases.

Green:

- PostgreSQL Store mappings;
- HumanTask preparer;
- runner-owned suspension committer.

Review:

- force crash window;
- verify the old executor write path is removed.

### Slice 5 — Restart and reconciliation

Red:

- fresh-provider matching/missing/mismatch/unknown-TypeId cases;
- real crash worker;
- response-loss worker.

Green:

- recovery orchestration;
- operation identity reconciliation.

Review:

- verify fail-closed paths do not update durable rows;
- verify no automatic transaction delegate retry.

### Slice 6 — Snapshot evidence and Audit sink

Red:

- immutable Snapshot cases;
- reuse every Phase 9a shared sink case;
- restart cases.

Green:

- Snapshot Store;
- durable `IAuditSink`.

Review:

- verify neither feature is represented as executable/reliable-delivery
  authority.

### Slice 7 — #25 composition seam and NativeAOT

Red:

- enlistment probe commit/rollback;
- native publish-link-run fixture.

Green:

- provider-internal enlistment seam;
- native Host with real state and Audit paths.

Review:

- inspect published warnings;
- execute original binary;
- confirm no Outbox product implementation entered Phase 9b.

---

## 17. NativeAOT Exit Evidence

The AOT Host uses:

- one application-owned generated JSON context with a non-trivial mutable nested
  state DTO;
- generated Runtime/Workflow/HumanTask provider persistence contexts;
- `AccountabilityJsonSerializerContext`;
- direct Npgsql PostgreSQL access;
- exact Descriptor registries and canonical hash runtime.

The native scenario:

1. validates/applies migrations;
2. captures registered input state;
3. creates Workflow;
4. atomically suspends with HumanTask;
5. disposes its service provider;
6. creates a fresh provider;
7. loads and validates pins;
8. restores the exact input CLR type;
9. writes one AuditEnvelope;
10. retries it and observes Duplicate;
11. emits:

```text
CRESTCREATES_RUNTIME_PERSISTENCE_OK
CRESTCREATES_RUNTIME_STATE_AOT_OK
CRESTCREATES_DURABLE_AUDIT_OK
```

The fixture:

- publishes with `-p:CrestCreatesPublishMode=aot`;
- completes native link;
- launches the original native artifact, not `dotnet <dll>`;
- passes a real PostgreSQL connection string;
- asserts all sentinels and exit code 0;
- retains the publish log as Issue-local evidence.

---

## 18. Review Findings Already Closed by This Spec

### R01 — Snapshot payload confusion

Closed by separating exact Pin execution authority from Snapshot evidence
authority.

### R02 — Split suspension write

Closed by preparation-without-persistence plus runner-owned transaction.

### R03 — `object?` type drift

Closed by `RuntimeStateValue`, explicit TypeId, generated `JsonTypeInfo`, and
pre-transaction capture.

### R04 — Tenantless keys

Closed by exact tenant key/scope on all APIs and canonical non-null PostgreSQL
scope columns.

### R05 — Audit reliability overclaim

Closed by limiting Phase 9b to durable sink acceptance; #25 owns delivery.

### R06 — Digest-only Pin ambiguity

Closed by persisting the full structured `CanonicalHash`, not only `.Value`.

### R07 — Host tenant uniqueness

Closed by explicit `(tenant_scope_kind, tenant_id)` representation rather than
nullable unique-key behavior.

### R08 — Validate-then-requery race

Closed by returning and executing the exact descriptor object from Pin
resolution.

### R09 — HumanTask completion/resume crash window

Closed as a boundary statement, not by pretending Phase 9b solves it. Durable
facts are replayable; #25 supplies reliable delivery.

### R10 — Ambiguous Store upsert

Closed by explicit Add and expected-revision Update operations.

### R11 — COMMIT acknowledgement loss

Closed by stable suspension operation identity and read-based reconciliation,
with no blind delegate retry.

### R12 — PostgreSQL NULL tenant semantics

Closed by schema-level exact host/tenant representation and constraints.

---

## 19. Implementation Review Guardrails

The design is approved. These questions are Slice review gates for the
Implementation Plan; they do not require another detailed Spec review:

1. Does any execution path still obtain a Descriptor from Snapshot data?
2. Does `HumanTaskStepExecutor` still write independently?
3. Can any durable/event field still hold an open object graph?
4. Can any Store lookup omit tenant authority?
5. Can host-scope uniqueness be bypassed through SQL NULL behavior?
6. Does Pin equality compare all structured hash metadata?
7. Can Registry activation change the validated descriptor before execution?
8. Can a stale update fall back to insert/upsert?
9. Can COMMIT uncertainty trigger automatic business delegate replay?
10. Does any provider exception or transaction object cross abstractions?
11. Do migration tests prove checksum and newer-schema fail-closed behavior?
12. Does the AOT test execute Npgsql and the original linked binary?
13. Does any test or document imply state-to-Audit/event reliability before
    #25?
14. Can #25 enlist a provider-specific Store without modifying existing Runtime
    public contracts?
15. Are Agent Memory and Agent Tool reconciliation still outside this Issue?

Any “yes” to questions 1–10 or 13, or “no” to 11, 12, 14, or 15 blocks the
affected implementation Slice from closing.

---

## 20. Exit Criteria

Phase 9b is complete only when:

1. All normative acceptance test names exist and are mapped to Case Matrix IDs.
2. Shared Add/CAS, transaction, suspension atomicity, and rollback cases pass
   for both InMemory and PostgreSQL. PostgreSQL alone owns restart, migration,
   crash-process, and database NativeAOT evidence.
3. The real suspension crash test exposes no partial suspension.
4. Fresh-provider recovery succeeds only with exact Pins and registered state.
5. Missing/mismatch/unknown-contract recovery leaves durable state unchanged.
6. Same-revision concurrency has exactly one winner.
7. Same IDs across host/two tenants remain isolated by API and database
   constraints.
8. Commit-response-loss reconciliation distinguishes committed, not committed,
   invariant violation, and indeterminate.
9. Snapshot Store is immutable and has no executable-definition API.
10. PostgreSQL `IAuditSink` passes every Phase 9a shared sink case and restart
    cases.
11. Migration create/reapply/interruption/checksum/newer-schema cases pass.
12. A test-only enlistment probe commits/rolls back with state without provider
    leakage.
13. The linux-x64 native binary executes the real PostgreSQL State, Pin, and
    Audit paths and emits all sentinels.
14. Boundary tests find no PostgreSQL/Npgsql type in Runtime abstractions.
15. Documentation explicitly preserves #25's ownership of reliable delivery.
16. `memory.md` is updated with the implemented support tier only after the
    publish-link-run evidence exists.

---

## 21. Implementation-Plan Handoff

The implementation plan must:

- follow the Red-Green-Review slices in order;
- create tests and fixtures before each production slice;
- name exact files/projects and focused commands;
- include deletion/cutover tasks for the old Store API and executor-owned write
  path;
- include a generated JSON root ledger;
- include a Case Matrix-to-test-name ledger;
- include independent-process crash and response-loss orchestration;
- include the exact NativeAOT publish and run command;
- stop after each Review gate if an invariant is not yet executable.

This approved Spec authorizes creation of the Implementation Plan. SQL,
migration resource layout, DTO mapping, ambient session mechanics, fixture
orchestration, dependency-graph file placement, and exact file changes are Plan
decisions constrained by this Spec.
