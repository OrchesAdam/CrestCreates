# Runtime Persistence — Usage Guide

> **Status:** Implemented. PostgreSQL direct Npgsql is `NativeAOT-verified`.

## 1. Choose a provider

Use the InMemory provider for tests, local scenarios, and semantic contract
fixtures:

```csharp
using CrestCreates.Runtime.Persistence.InMemory;

builder.Services.AddCrestCreatesInMemoryRuntimePersistence();
```

It preserves atomic multi-store transactions and rollback, but it is not a
process-durable or restart-recovery provider.

Use PostgreSQL for the production durable runtime:

```csharp
using CrestCreates.Runtime.Persistence.PostgreSql;

builder.Services.AddCrestCreatesPostgreSqlRuntimePersistence(
    new PostgreSqlRuntimePersistenceOptions
    {
        ConnectionString = builder.Configuration.GetConnectionString("Runtime")!,
        Schema = "crest_runtime",
        ApplyMigrations = false,
        CommandTimeoutSeconds = 30
    });
```

The PostgreSQL registration adds the Runtime transaction coordinator, Workflow
and HumanTask stores, suspension receipt store, Descriptor Snapshot evidence
store, and durable `IAuditSink`.

`ApplyMigrations = false` performs validation only and executes no DDL. It
fails closed when the schema is missing, a migration is pending, or the actual
columns, constraints, indexes, foreign keys, or CHECK definitions differ from
the provider manifest. Set `ApplyMigrations = true` only for an explicitly
authorized schema owner.

## 2. Register application state contracts

State types must have a stable TypeId and generated JSON metadata. Register them
through a contributor; do not serialize `object` with reflection or rely on
`JsonElement` round-tripping.

```csharp
public sealed class OrderRuntimeStateContributor : IRuntimeStateContractContributor
{
    public void Contribute(IRuntimeStateContractBuilder builder)
    {
        builder.Add(
            "orders.approval-state.v1",
            OrderRuntimeJsonContext.Default.OrderApprovalState,
            [typeof(OrderApprovalState)]);
    }
}
```

Register the contributor with the host:

```csharp
builder.Services.AddSingleton<IRuntimeStateContractContributor,
    OrderRuntimeStateContributor>();
```

An unregistered value is rejected before the transaction begins. State values
are copied into immutable `RuntimeStateValue` envelopes and restored as deep
CLR values after restart.

## 3. Use tenant-scoped runtime keys

All Runtime reads and writes carry the tenant boundary explicitly:

```csharp
var key = new RuntimeInstanceKey(tenantId, workflowInstanceId);
var workflow = await workflowStore.GetAsync(key, cancellationToken);
```

`tenantId == null` represents host scope. Never use a bare string instance ID
for Runtime lookup, CAS, correlation, or completion events.

## 4. Pin execution authority

Persist the exact Pin with every durable Workflow/HumanTask instance:

```csharp
var pin = new RuntimeDescriptorPin
{
    Ref = workflowDescriptor.Ref,
    ContractHash = workflowDescriptor.ContractHash,
    DefinitionHash = workflowDescriptor.DefinitionHash,
    SnapshotId = optionalEvidenceSnapshotId
};
```

After restart, resolve the Pin from the currently activated Registry. The
Descriptor Snapshot is evidence only. If the Registry cannot resolve the exact
reference or either hash differs, recovery stops fail-closed and the durable
record remains available for diagnosis.

## 5. Suspension and recovery rules

Workflow code must use the runtime suspension committer/coordinator so that the
HumanTask insert, Workflow Suspended CAS, Pin validation, and receipt share one
transaction. Do not call the HumanTask and Workflow stores as unrelated writes.

The receipt gives a stable operation identity for response-loss reconciliation:

```text
matching receipt + matching integrity → committed
no receipt + exact pre-state       → not committed
missing or conflicting evidence     → fail closed / diagnose
```

## 6. Audit sink expectations

The durable sink guarantees Accepted/Duplicate/Conflict persistence across
restart. Identical retries are Duplicate; the same AuditId with different
structured integrity is Conflict. It does not provide a State Commit → Audit
delivery guarantee. Add the #25 Outbox when that coupling is required.

