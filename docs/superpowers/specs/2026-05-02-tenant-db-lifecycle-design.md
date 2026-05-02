# Tenant Database Lifecycle — Design Spec

**Feature plan**: `docs/review/feature-plans/tenant-db-lifecycle.xml`
**Date**: 2026-05-02
**Status**: draft

> This spec supersedes `docs/review/feature-plans/tenant-db-lifecycle.xml` for implementation details. The XML feature plan describes goals and scope; this document defines the concrete architecture, data flow, error handling, and testing strategy.

## 1. Architecture & Components

### Call Chain

```
TenantAppService.CreateAsync
  → TenantRepository.FindByNameAsync     // Application: uniqueness check
  → TenantManager.CreateAsync            // Domain: create aggregate, validate domain rules
  → TenantRepository.InsertAsync         // Application: persist to host DB
  → TenantInitializationOrchestrator.InitializeAsync  // Application: full init chain
       → ITenantDatabaseInitializer.InitializeAsync   // Independent DB only
       → ITenantMigrationRunner.RunAsync              // Independent DB only
       → ITenantDataSeeder.SeedAsync                  // Both shared & independent
       → ITenantSettingDefaultsSeeder.SeedAsync
       → ITenantFeatureDefaultsSeeder.SeedAsync
       → Update Tenant.InitializationStatus / InitializedAt / LastInitializationError
       → Write TenantInitializationRecord
```

`TenantAppService` owns the full coordination: it checks uniqueness via repository, calls `TenantManager.CreateAsync` (which only constructs the aggregate and validates domain rules — no I/O), persists via repository, then invokes the orchestrator. `TenantManager` is a Domain-layer component that does **not** call repositories or the orchestrator.

### New Abstractions (in `CrestCreates.Application.Contracts`)

| Interface | Method | Purpose |
|-----------|--------|---------|
| `ITenantDatabaseInitializer` | `Task<TenantDatabaseInitializeResult> InitializeAsync(TenantInitializationContext, CancellationToken)` | Ensure DB exists for independent tenant |
| `ITenantMigrationRunner` | `Task<TenantMigrationResult> RunAsync(TenantInitializationContext, CancellationToken)` | Run migrations on tenant DB |
| `ITenantDataSeeder` | `Task<TenantSeedResult> SeedAsync(TenantInitializationContext, CancellationToken)` | Seed admin user, default role, permissions |
| `ITenantSettingDefaultsSeeder` | `Task<TenantSettingDefaultsResult> SeedAsync(TenantInitializationContext, CancellationToken)` | Write default settings for tenant |
| `ITenantFeatureDefaultsSeeder` | `Task<TenantFeatureDefaultsResult> SeedAsync(TenantInitializationContext, CancellationToken)` | Write default feature values for tenant |

Existing `TenantBootstrapper` implements `ITenantDataSeeder` — it already handles admin user, role, and permission seeding. The old `ITenantBootstrapper` interface is **retired**: TenantManager no longer calls `ITenantBootstrapper.BootstrapAsync` directly. The single main chain is `TenantInitializationOrchestrator.InitializeAsync`, which calls `ITenantDataSeeder.SeedAsync` as one phase. No dual-track compatibility shim — existing call sites in `TenantManager` are updated, and `TenantBootstrapper` is renamed or adapted to the new interface.

### EF Core Implementations (in `CrestCreates.OrmProviders.EFCore`)

| Class | Implements |
|-------|------------|
| `EfCoreTenantDatabaseInitializer` | `ITenantDatabaseInitializer` |
| `EfCoreTenantMigrationRunner` | `ITenantMigrationRunner` |

### Orchestrator (in `CrestCreates.Application`)

| Class | Purpose |
|-------|---------|
| `TenantInitializationOrchestrator` | Injected with all 5 interfaces. Single `InitializeAsync(TenantInitializationContext)` entry. Branches on `IsIndependentDatabase`: empty → skip DB init + migration; non-empty → full chain. Manages Tenant status updates and TenantInitializationRecord writes. |

### Context Object (in `CrestCreates.Application.Contracts`)

```csharp
public class TenantInitializationContext
{
    public Guid TenantId { get; init; }
    public string TenantName { get; init; }
    public string? ConnectionString { get; init; }
    public bool IsIndependentDatabase => !string.IsNullOrWhiteSpace(ConnectionString);
    public string CorrelationId { get; init; }
    public Guid? RequestedByUserId { get; init; }
}
```

### Status Model

**Overall initialization status** (in `CrestCreates.Domain.Shared`):

```csharp
public enum TenantInitializationStatus
{
    Pending,
    Initializing,
    Initialized,
    Failed
}
```

**Per-step status** (in `CrestCreates.Domain.Shared`):

```csharp
public enum TenantInitializationStepStatus
{
    Running,
    Succeeded,
    Failed,
    Skipped
}
```

`TenantInitializationStatus` is used on the Tenant entity and TenantInitializationRecord for the overall outcome. `TenantInitializationStepStatus` is used in StepResultsJson entries and TenantInitializationStep for per-phase tracking. The two enums are intentionally separate — overall state and step-level state have different semantics.

**Tenant entity extensions** (in `CrestCreates.Domain`):

| Field | Type | Purpose |
|-------|------|---------|
| `InitializationStatus` | `TenantInitializationStatus` | Current initialization state for availability checks |
| `InitializedAt` | `DateTime?` | When initialization completed successfully |
| `LastInitializationError` | `string?` | Sanitized, user-safe error message for diagnostics |

**TenantInitializationRecord entity** (in `CrestCreates.Domain`):

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | `Guid` | Primary key |
| `TenantId` | `Guid` | FK to Tenant |
| `Status` | `TenantInitializationStatus` | Overall result for this attempt |
| `CurrentStep` | `string?` | Which phase is executing (null when complete) |
| `StepResultsJson` | `string` | JSON array: `[{Name, Status, StartedAt, CompletedAt, Error}, ...]` where Status uses `TenantInitializationStepStatus` |
| `Error` | `string?` | Detailed technical error (connection strings redacted) |
| `StartedAt` | `DateTime` | When this attempt started |
| `CompletedAt` | `DateTime?` | When this attempt finished |
| `AttemptNo` | `int` | Monotonically increasing per tenant |
| `CorrelationId` | `string` | Links to structured logs |

**ITenantInitializationRecordRepository** (in `CrestCreates.Application.Contracts`):

```csharp
public interface ITenantInitializationRecordRepository
{
    Task<TenantInitializationRecord?> GetLatestAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<int> GetMaxAttemptNoAsync(Guid tenantId, CancellationToken cancellationToken);
    Task InsertAsync(TenantInitializationRecord record, CancellationToken cancellationToken);
    Task UpdateAsync(TenantInitializationRecord record, CancellationToken cancellationToken);
}
```

This gives the orchestrator a single persistence abstraction for record CRUD. Without it, implementers would scatter record persistence across the orchestrator or phase handlers.

**TenantInitializationResult** (in `CrestCreates.Application.Contracts`):

| Field | Type | Purpose |
|-------|------|---------|
| `Success` | `bool` | Overall outcome |
| `Status` | `TenantInitializationStatus` | Result status |
| `Error` | `string?` | Public-safe error message |
| `CorrelationId` | `string` | Links to logs and records |
| `Steps` | `IReadOnlyList<TenantInitializationStep>` | Per-step results |

`TenantInitializationStep`:

| Field | Type |
|-------|------|
| `Name` | `string` |
| `Status` | `TenantInitializationStepStatus` |
| `StartedAt` | `DateTime?` |
| `CompletedAt` | `DateTime?` |
| `Error` | `string?` |

## 2. Data Flow

### Create Tenant — Full Chain

```
TenantAppService.CreateAsync(CreateTenantDto)
│
├─ 1. TenantRepository.FindByNameAsync(normalizedName)
│     └─ If exists → throw duplicate
│
├─ 2. TenantManager.CreateAsync(dto)
│     ├─ Create Tenant entity (InitializationStatus = Pending)
│     └─ Validate domain rules (no I/O)
│
├─ 3. TenantRepository.InsertAsync(tenant)
│     └─ Persist tenant metadata to host DB
│
├─ 4. TenantInitializationOrchestrator.InitializeAsync(context)
│     │
│     ├─ 4a. TryBeginInitializationAsync(tenantId, correlationId):
│     │     Atomically, in one host-DB transaction:
│     │       1. UPDATE Tenant SET InitializationStatus = 'Initializing'
│     │          WHERE <tenant primary key> = @id
│     │            AND InitializationStatus IN ('Pending', 'Failed')
│     │          → 0 rows affected = return null (conflict)
│     │       2. Compute AttemptNo = max(existing AttemptNo for tenant) + 1
│     │       3. INSERT TenantInitializationRecord
│     │          (AttemptNo, Status = Initializing, CorrelationId)
│     │       4. Return the new TenantInitializationRecord
│     │
│     ├─ IF context.IsIndependentDatabase:
│     │   ├─ Phase 1: ITenantDatabaseInitializer.InitializeAsync
│     │   │   ├─ Update Record.CurrentStep = "DatabaseInitialize"
│     │   │   ├─ Append {Name, Status=Running, StartedAt} to StepResultsJson (using TenantInitializationStepStatus.Running)
│     │   │   └─ Ensure database exists, create if needed
│     │   └─ Phase 2: ITenantMigrationRunner.RunAsync
│     │       ├─ Update Record.CurrentStep = "Migration"
│     │       ├─ Append {Name, Status=Running, StartedAt} to StepResultsJson (using TenantInitializationStepStatus.Running)
│     │       └─ Run pending migrations on tenant DB
│     │
│     ├─ Phase 3: ITenantDataSeeder.SeedAsync
│     │   └─ Admin user, default role, permissions
│     ├─ Phase 4: ITenantSettingDefaultsSeeder.SeedAsync
│     ├─ Phase 5: ITenantFeatureDefaultsSeeder.SeedAsync
│     │
│     ├─ 4d. On success:
│     │     ├─ Tenant.InitializationStatus = Initialized, InitializedAt = now
│     │     ├─ Record.Status = Initialized, CompletedAt = now, CurrentStep = null
│     │     └─ Return TenantInitializationResult { Success = true, Steps = [...] }
│     │
│     └─ 4e. On failure at any phase:
│           Phase-level business failure (e.g., migration conflict, seed constraint violation):
│             ├─ Persist Tenant status + error + Record + step results in one host-DB transaction
│             └─ Return TenantInitializationResult { Success = false, Error = ..., Steps = [...] }
│
│           Infrastructure failure (host-DB transaction fails, state cannot be written):
│             └─ Throw — no safe result to return; caller handles as system error
│
│           In both cases: independent tenant DB changes from prior phases are NOT rolled back.
│           Retry relies on phase idempotency to safely re-apply.
```

### Retry Flow

```
TenantAppService.RetryInitializationAsync(tenantId)
│
├─ Load tenant, verify status ∈ { Pending, Failed }
├─ Resolve TenantInitializationContext from tenant entity
└─ TenantInitializationOrchestrator.InitializeAsync(context)
    ├─ TryBeginInitializationAsync: atomic transition + AttemptNo + record insert
    │   └─ null = return conflict
    └─ Same chain as create; retry restarts from phase 1.
       Idempotency ensures phases 1..N-1 are no-ops.
```

### Transaction Boundaries

Host DB and independent tenant DB are **separate resources** — they cannot share a transaction.

| Resource | Transaction scope |
|----------|-------------------|
| Host DB (Tenant status, TenantInitializationRecord) | Atomic within host DB. Status transition + record insert/update happen in one host-DB transaction. |
| Independent tenant DB (EnsureCreated, Migrate, seed, defaults) | Each phase is its own connection/transaction. No cross-DB two-phase commit. |

**Failure recovery model**: If an independent-DB phase fails after host DB state is written, the host DB reflects `Failed` with step-level error details. Retry restarts from phase 1 and relies on phase idempotency (EnsureCreated no-op, zero pending migrations, seed existence checks) to safely re-apply. No cross-DB rollback is attempted.

### Scenario Routing

| Condition | Strategy | Orchestrator Behavior |
|-----------|----------|-----------------------|
| `context.IsIndependentDatabase == true` | Independent tenant DB | EnsureCreated → Migrate → Seed → Defaults. Seed/defaults write to the **tenant's independent DB**. |
| `context.IsIndependentDatabase == false` | Shared tenant data | Skip DB init + migration, seed + defaults only. Seed/defaults write to the **shared DB** using the default host/shared connection string, with `TenantId` filtering for data isolation. |
| Host DB migration | Out of scope | Handled by app startup / deployment migrator |

**Connection string resolution for shared tenants**: When `ConnectionString` is empty, the business `DbContext` (resolved via `IEfCoreDbContextOptionsContributor` or equivalent) falls back to the host/shared connection string configured at startup. `ICurrentTenant` provides the `TenantId` for row-level data filtering. The orchestrator itself does not resolve connection strings — it delegates to the `TenantInitializationContext` which the caller populates from the tenant entity.

### Public API Contract

Retry is exposed on `ITenantAppService`:

```csharp
public interface ITenantAppService
{
    Task<TenantDto> CreateAsync(CreateTenantDto input);
    Task<TenantInitializationResult> RetryInitializationAsync(Guid tenantId);
    Task<TenantInitializationResult> GetInitializationStatusAsync(Guid tenantId);
    Task<TenantInitializationResult> ForceRetryInitializationAsync(Guid tenantId);
    Task<TenantInitializationResult> ForceFailInitializationAsync(Guid tenantId);
    // ... existing methods
}
```

| Method | Permission | Dynamic API |
|--------|------------|-------------|
| `RetryInitializationAsync` | `Tenant.RetryInitialization` | `POST /api/tenants/{id}/retry-initialization` |
| `GetInitializationStatusAsync` | `Tenant.Read` | `GET /api/tenants/{id}/initialization-status` |
| `ForceRetryInitializationAsync` | `Tenant.AdminForceRetry` | `POST /api/tenants/{id}/force-retry-initialization` |
| `ForceFailInitializationAsync` | `Tenant.AdminForceRetry` | `POST /api/tenants/{id}/force-fail-initialization` |

`RetryInitializationAsync` returns the same `TenantInitializationResult` type as the create flow — callers get the full step breakdown regardless of which path triggered initialization.

`RetryInitializationAsync` only accepts `Pending`/`Failed` tenants. `ForceRetryInitializationAsync` and `ForceFailInitializationAsync` are admin-level recovery endpoints that operate on any status including `Initializing` — they exist to recover tenants stuck after a process crash.

### Lifecycle Rules

| Scenario | Behavior |
|----------|----------|
| Success | `Tenant.InitializationStatus = Initialized` |
| Failure | Tenant preserved, status = `Failed`, error + record written |
| Retry | Only `Failed` or `Pending` tenants can retry |
| Already initialized | Retry denied: status = `Initialized` |
| Cleanup | Separate `DeleteTenantAsync` / `AbortTenantCreationAsync`, never auto-delete on failure |
| Concurrent retry | Atomic transition rejects second caller → conflict |
| Stuck at `Initializing` | If a process crashes mid-initialization, the tenant remains `Initializing` indefinitely. Recovery: an admin-level `ForceFailInitializationAsync` marks it `Failed` and writes a record with error "Initialization timed out or process terminated". `ForceRetryInitializationAsync` allows restart from any status including `Initializing`. Only the designated admin API performs this — the standard retry endpoint only accepts `Pending`/`Failed`. |

## 3. Error Handling

### Per-Phase Flow

```
Each phase:
  ├─ Update Record.CurrentStep = phase name
  ├─ Append {Name, Status=Running, StartedAt} to StepResultsJson (using TenantInitializationStepStatus.Running)
  ├─ Try
  │   └─ Execute phase
  ├─ Success
  │   └─ Update StepResultsJson entry (Status=TenantInitializationStepStatus.Succeeded, CompletedAt)
  └─ Catch
      ├─ Update StepResultsJson entry (Status=TenantInitializationStepStatus.Failed, Error, CompletedAt)
      ├─ Persist Tenant status + Record + step results in one host-DB transaction
      ├─ Note: independent tenant DB changes from prior phases are NOT rolled back (no cross-DB transaction).
      │   The tenant DB may be partially initialized. Retry relies on phase idempotency to safely re-apply.
      └─ Stop chain — do not proceed to next phase
```

### Concurrency Protection

| Mechanism | Purpose |
|-----------|---------|
| `TryBeginInitializationAsync` single transaction | Status transition + AttemptNo + record insert in one host-DB transaction. Returns the new record, or null if the transition fails. |
| Status gate | `WHERE InitializationStatus IN ('Pending', 'Failed')` — only allowed states can enter Initializing. `Initialized` and `Initializing` are rejected. |
| Null return → conflict | Caller gets conflict without touching any state in the non-transition case |
| UNIQUE INDEX on `(TenantId, AttemptNo)` | Safety net against duplicate attempt numbers within the transaction |
| No partial state | If record insert fails, the entire transaction rolls back — tenant is never left at `Initializing` with no record |

Atomic initialization entry point (on `ITenantInitializationRecordRepository`):

```csharp
/// <summary>
/// Atomically transitions tenant to Initializing, computes AttemptNo,
/// inserts a new TenantInitializationRecord, and returns it.
/// Returns null if the status transition fails (tenant is not Pending/Failed,
/// or another caller already acquired the lock).
/// All three operations run in a single host-DB transaction.
/// </summary>
Task<TenantInitializationRecord?> TryBeginInitializationAsync(
    Guid tenantId,
    string correlationId,
    CancellationToken cancellationToken);
```

### Error Categories

| Category | Recorded where | Surfaced how |
|----------|----------------|--------------|
| DB creation failure | `Tenant.LastInitializationError` + `StepResultsJson[].Error` | `TenantInitializationResult.Error` |
| Migration failure | Same | Same |
| Seed failure | Same | Same |
| Settings/Feature defaults failure | Same | Same |

### Error Message Policy

| Location | Content | Constraint |
|----------|---------|------------|
| `Tenant.LastInitializationError` | Sanitized, user-safe summary | No stack traces, no connection strings |
| `Record.StepResultsJson[].Error` | Detailed technical error | Redact connection strings; cap max length, truncate overflow |
| `TenantInitializationResult.Error` | Same as `LastInitializationError` | Safe for API response |
| Full exception | Structured logs | Linked via `CorrelationId` |

### Idempotency Per Phase

| Phase | Strategy |
|-------|----------|
| `ITenantDatabaseInitializer` | `EnsureCreated` — no-op if database already exists |
| `ITenantMigrationRunner` | Migration history table — no pending migrations → no-op |
| `ITenantDataSeeder` | Check admin role/user existence before insert |
| `ITenantSettingDefaultsSeeder` | Provider-specific upsert or get-or-create pattern |
| `ITenantFeatureDefaultsSeeder` | Same as settings |

## 4. Testing Strategy

### Core Tests

| Test | What it verifies |
|------|-----------------|
| `CreateTenant_ShouldInitializeDatabaseAndSeedTenantData` | Independent DB: full chain success, verify admin user/role/permission/ settings/features data written |
| `CreateTenant_WithMigrationFailure_ShouldRecordFailedStatus` | App test: fake `ITenantMigrationRunner` throws → status = Failed, error + step results persisted. EF runner test: real migration failure behavior |
| `RetryTenantInitialization_ShouldBeIdempotent` | Retry on Failed tenant restarts from phase 1, all phases no-op, ends in Initialized |
| `TenantIndependentDatabase_ShouldUseTenantConnectionString` | Non-empty connection string triggers DB init + migration; empty skips both |
| `TenantSharedDatabase_ShouldSeedWithoutMigration` | Empty connection string skips phase 1+2, still runs seed + defaults |
| `Initialization_ShouldPersistStepResultsJson` | Prevents "returned result but didn't persist to record" bug |
| `Initialization_ShouldSanitizePublicError_AndKeepCorrelationId` | Public error has no connection string / stack trace; CorrelationId links to logs |
| `Initialization_ShouldComputeAttemptNoAfterLock` | AttemptNo = max(existing) + 1 computed only after acquiring Initializing lock |
| `Initialization_WhenRecordInsertUniqueViolation_ShouldReturnConflictOrRetrySafely` | Extreme concurrent unique index violation produces no dirty state |
| `Initialization_OnFailure_ShouldPreserveTenantAndNotDelete` | Failure records status + error on Tenant and Record; tenant entity is NOT deleted — supersedes old "bootstrap failure deletes tenant" behavior |

### Additional Unit Tests

| Test | What it verifies |
|------|-----------------|
| `Initialization_WithConcurrentRequest_ShouldReturnConflict` | Two callers both see Failed, one gets lock via `TryBeginInitializationAsync`, other gets conflict |
| `RetryTenantInitialization_OnAlreadyInitialized_ShouldBeDenied` | Status = `Initialized` rejects retry |
| `Orchestrator_WithSharedDbContext_ShouldSkipPhase1And2` | `IsIndependentDatabase == false` → phase 1+2 not called |
| `EfCoreMigrationRunner_NoPendingMigrations_ShouldBeNoOp` | Idempotency of EF Core migration runner |
| `DataSeeder_DuplicateCall_ShouldNotCreateDuplicates` | Idempotency of seed phase |
| `SettingsDefaultsSeeder_UpsertPattern_ShouldBeIdempotent` | Provider-specific upsert works on retry |

### Test Layer Placement

| Layer | What it tests | Test doubles |
|-------|---------------|--------------|
| Application unit | Orchestrator sequence, shared/independent branching, failure recording, retry, concurrency lock | Fake phase handlers + fake repository/uow. `TryBeginInitializationAsync` must simulate atomic transition, not just Get+Set |
| OrmProviders tests | `EfCoreTenantDatabaseInitializer`, `EfCoreTenantMigrationRunner` | SQLite or provider-compatible test DB |
| Integration | Tenant status after creation, record persistence, seed/defaults queryable | Reuse existing integration harness |
| Optional E2E | admin can login | Only after auth test foundation is stable |

### Testing Principles

- Application unit tests are the primary layer — they validate orchestrator logic without real databases
- Migration failure tested at two levels: fake runner (orchestrator error handling) and real runner (migration behavior)
- Integration tests verify data is written correctly, not authentication
- Concurrent lock tests use a `TryBeginInitializationAsync` semantics, not generic mock Get+Set
- Admin login verification is optional E2E, not a gate
- **Existing tests that verify "bootstrap failure deletes tenant" must be replaced** — the new design preserves the tenant on failure. Any legacy test asserting tenant deletion after bootstrap failure is testing a retired path and must be updated to assert tenant preservation with `Failed` status.
