# Runtime Persistence — Architecture Design

> **Status:** Implemented and merged
> **Phase:** 9b, Issue #24, PR #71
> **Last updated:** 2026-08-03

## 1. Purpose and boundary

Runtime Persistence is the first durable authority for Workflow and HumanTask
execution. It owns the provider-neutral contracts and two supported provider
levels:

| Provider | Support level | Claims |
|---|---|---|
| InMemory | `FullSemantic` | Add/CAS, atomic multi-store commit, rollback, tenant isolation |
| PostgreSQL direct Npgsql | `FullDurable` | The InMemory semantics plus migrations, restart recovery, crash evidence, and Linux NativeAOT publish/link/run evidence |

Phase 9b does not implement deployment activation gates, dormant-instance
startup scans, reliable State → Accountability/Event delivery, Outbox delivery,
Agent Tool reconciliation, or a Descriptor definition repository. Those remain
owned by deployment governance, #25, and #70 respectively.

## 2. Execution authority

Durable execution is reconstructed from an exact `RuntimeDescriptorPin`:

```text
DescriptorRef + ContractHash + DefinitionHash
    → current activated Registry
    → exact reference and hash validation
    → executable Descriptor
```

`DescriptorSnapshot` is optional immutable evidence and an index. It does not
contain an executable `IDescriptor` payload and is never used as the definition
source. Missing descriptors, reference mismatches, hash mismatches, or invalid
snapshot evidence fail closed while leaving durable state available for
diagnosis and later recovery.

Pin validation is lazy when an instance is loaded for execution or transition.
Phase 9b does not scan all dormant instances during host startup or block a
deployment from removing an old descriptor version. Compatible deployments
must retain descriptor versions required by live durable instances.

## 3. Durable state contract

Open-ended `object?` values do not enter the durable model. Runtime state is
captured as an immutable `RuntimeStateValue` containing a stable `TypeId`, an
optional `SchemaRef`, and a JSON payload produced by explicitly registered
generated `JsonTypeInfo`.

```text
application CLR value
    → registered contributor / JsonTypeInfo
    → RuntimeStateValue(TypeId, SchemaRef, Json)
    → PostgreSQL jsonb
    → registry validation
    → deep CLR restore
```

Unregistered types fail before a transaction starts. Restored values are deep
snapshots; provider tracking objects and mutable payload references never
escape into callers.

## 4. Atomic suspension boundary

The Workflow suspension commit is the first business transaction boundary:

```text
HumanTask insert
    + Workflow compare-and-swap to Suspended
    + exact Descriptor Pin validation/reference
    + immutable suspension receipt
    → one commit or no visible state
```

All Workflow/HumanTask transitions use detached post-state values. A failed or
unknown commit does not advance the caller-visible pre-state. Concurrent writes
from the same old revision allow at most one winner.

Every lookup, update, correlation, primary key, unique constraint, and receipt
uses `RuntimeInstanceKey(TenantId, InstanceId)`. Host and tenant records with
the same instance ID remain distinct.

## 5. PostgreSQL provider kernel

The production path uses Npgsql directly. It provides:

- one ambient transaction coordinator shared by Runtime stores;
- checksummed migrations with an explicit migration-apply lock;
- validation-only startup with zero DDL;
- provider-owned schema manifest checks for columns, primary keys, ordered
  index columns/predicates, foreign-key tuples/deferrability, and named complete
  CHECK definitions with preserved boolean grouping;
- reciprocal Workflow/HumanTask/receipt foreign keys and lifecycle checks;
- durable state, Descriptor Snapshot evidence, suspension receipts, and audit
  envelopes in PostgreSQL.

The provider does not expose `DbContext`, `DbTransaction`, Npgsql types, or SQL
to Workflow/HumanTask abstractions. Provider errors are translated into stable
provider-neutral contract failures.

## 6. Accountability relationship

The PostgreSQL provider implements durable `IAuditSink` semantics:

```text
new AuditId
    → Accepted
same AuditId + same structured Integrity
    → Duplicate
same AuditId + different structured Integrity
    → Conflict
```

Accepted envelopes survive provider restart. This does not imply that every
committed Workflow state must already have reached the sink; that reliable
coupling is the #25 Outbox responsibility.

## 7. Evidence and tests

The acceptance chain includes:

- atomic suspension and crash rollback;
- restart with matching, missing, and mismatched Pins;
- registered state round-trip and unregistered-state pre-transaction failure;
- tenant-scoped lookup and concurrent CAS;
- receipt reconciliation and response-loss recovery;
- schema compatibility tampering cases;
- durable Audit Accepted/Duplicate/Conflict across restart;
- InMemory/PostgreSQL semantic parity;
- Linux x64 NativeAOT publish, link, and run of state, Pin, suspension, and
  audit retry paths.

