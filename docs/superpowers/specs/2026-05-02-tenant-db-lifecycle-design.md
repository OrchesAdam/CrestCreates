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

### New Abstractions

**Public contracts** (in `CrestCreates.Application.Contracts`):

| Interface | Method | Purpose |
|-----------|--------|---------|
| `ITenantDatabaseInitializer` | `Task<TenantDatabaseInitializeResult> InitializeAsync(TenantInitializationContext, CancellationToken)` | Ensure physical database exists for independent tenant (do NOT use EF Core `EnsureCreatedAsync` — that bypasses migrations) |
| `ITenantMigrationRunner` | `Task<TenantMigrationResult> RunAsync(TenantInitializationContext, CancellationToken)` | Run `Database.MigrateAsync()` on tenant DB |
| `ITenantDataSeeder` | `Task<TenantSeedResult> SeedAsync(TenantInitializationContext, CancellationToken)` | Seed admin user, default role, permissions |
| `ITenantSettingDefaultsSeeder` | `Task<TenantSettingDefaultsResult> SeedAsync(TenantInitializationContext, CancellationToken)` | Write default settings via existing Setting Management services (not direct table writes) |
| `ITenantFeatureDefaultsSeeder` | `Task<TenantFeatureDefaultsResult> SeedAsync(TenantInitializationContext, CancellationToken)` | Write default feature values via existing Feature Management services (not direct table writes) |

**Internal store** (in `CrestCreates.Application`, not in Contracts — it returns Domain entities and serves the orchestrator):

| Interface | Method | Purpose |
|-----------|--------|---------|
| `ITenantInitializationStore` | `TryBeginInitializationAsync` / `ForceBeginInitializationAsync` / `GetLatestAsync` / `UpdateAsync` | Internal persistence for initialization records and atomic status transitions. Works with Domain entity `TenantInitializationRecord`. |

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

**TenantDto extensions** (in `CrestCreates.Application.Contracts` — must mirror the entity fields):

| Field | Type | Purpose |
|-------|------|---------|
| `InitializationStatus` | `TenantInitializationStatus` | Exposed to API consumers so `CreateAsync` / queries reflect init state |
| `InitializedAt` | `DateTime?` | When initialization completed |
| `LastInitializationError` | `string?` | Public-safe error summary, surfaced when status is `Failed` |

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

**ITenantInitializationStore** (in `CrestCreates.Application` — internal to the Application layer, not exposed in Contracts):

```csharp
/// <summary>
/// Internal persistence abstraction for the orchestrator.
/// Returns Domain entities; lives in Application layer, not Contracts.
/// </summary>
public interface ITenantInitializationStore
{
    /// <summary>Atomically transitions Pending/Failed → Initializing, computes AttemptNo,
    /// inserts record. Returns null if transition fails.</summary>
    Task<TenantInitializationRecord?> TryBeginInitializationAsync(
        Guid tenantId, string correlationId, CancellationToken cancellationToken);

    /// <summary>Atomically transitions Initializing → Initializing (same state, new record),
    /// for force-retry from a stuck Initializing state. Returns null if not Initializing.</summary>
    Task<TenantInitializationRecord?> ForceBeginInitializationAsync(
        Guid tenantId, string correlationId, string reason, CancellationToken cancellationToken);

    Task<TenantInitializationRecord?> GetLatestAsync(Guid tenantId, CancellationToken cancellationToken);
    Task UpdateAsync(TenantInitializationRecord record, CancellationToken cancellationToken);
}
```

This gives the orchestrator a single persistence abstraction for record CRUD and atomic state transitions. Without it, implementers would scatter record persistence and status updates across the orchestrator or phase handlers. The store works with the Domain entity `TenantInitializationRecord` internally — it is not exposed to consumers of `Application.Contracts`.

**Store implementation**: The interface lives in `CrestCreates.Application`. The EF Core implementation lives in `CrestCreates.OrmProviders.EFCore` (or `CrestCreates.Infrastructure` if a shared persistence project exists), where it has access to the host `DbContext` for atomic `UPDATE` + `INSERT` in a single transaction.

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
TenantAppService.CreateAsync(CreateTenantDto) → returns TenantDto
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
│     │   (internal; result consumed by AppService, not returned to caller)
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
│     │   │   ├─ Append {Name, Status=Running, StartedAt} to StepResultsJson
│     │   │   └─ Ensure physical database exists (CREATE DATABASE IF NOT EXISTS).
│     │   │      Do NOT call EF Core EnsureCreatedAsync — that bypasses migrations.
│     │   └─ Phase 2: ITenantMigrationRunner.RunAsync
│     │       ├─ Update Record.CurrentStep = "Migration"
│     │       ├─ Append {Name, Status=Running, StartedAt} to StepResultsJson
│     │       └─ Run Database.MigrateAsync() on tenant DB
│     │
│     ├─ Phase 3: ITenantDataSeeder.SeedAsync
│     │   └─ Admin user, default role, permissions
│     ├─ Phase 4: ITenantSettingDefaultsSeeder.SeedAsync
│     │   └─ Must call existing Setting Management services, not direct table writes
│     ├─ Phase 5: ITenantFeatureDefaultsSeeder.SeedAsync
│     │   └─ Must call existing Feature Management services, not direct table writes
│     │
│     ├─ 4d. On success:
│     │     ├─ Tenant.InitializationStatus = Initialized, InitializedAt = now
│     │     ├─ Record.Status = Initialized, CompletedAt = now, CurrentStep = null
│     │     └─ AppService returns TenantDto (InitializationStatus = Initialized)
│     │
│     └─ 4e. On phase-level business failure:
│           ├─ Persist Tenant status + error + Record + step results in one host-DB transaction
│           └─ AppService returns TenantDto (InitializationStatus = Failed,
│               LastInitializationError = sanitized message).
│               Caller uses GetInitializationStatusAsync for full step breakdown.
│               CreateAsync does NOT throw for business failures.
│
│     └─ 4f. On infrastructure failure (host-DB transaction fails):
│           └─ Throw — no safe result to return; caller handles as system error
│
│     In all failure cases: independent tenant DB changes from prior phases
│     are NOT rolled back. Retry relies on phase idempotency to safely re-apply.
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
| Independent tenant DB (physical DB creation, Migrate, seed, defaults) | Each phase is its own connection/transaction. No cross-DB two-phase commit. |

**Failure recovery model**: If an independent-DB phase fails after host DB state is written, the host DB reflects `Failed` with step-level error details. Retry restarts from phase 1 and relies on phase idempotency (database already exists, zero pending migrations, seed existence checks) to safely re-apply. No cross-DB rollback is attempted.

### Scenario Routing

| Condition | Strategy | Orchestrator Behavior |
|-----------|----------|-----------------------|
| `context.IsIndependentDatabase == true` | Independent tenant DB | DB Create → Migrate → Seed → Defaults. Seed/defaults write to the **tenant's independent DB**. |
| `context.IsIndependentDatabase == false` | Shared tenant data | Skip DB init + migration, seed + defaults only. Seed/defaults write to the **shared DB** using the default host/shared connection string, with `TenantId` filtering for data isolation. |
| Host DB migration | Out of scope | Handled by app startup / deployment migrator |

**Connection string resolution for shared tenants**: When `ConnectionString` is empty, the business `DbContext` (resolved via `IEfCoreDbContextOptionsContributor` or equivalent) falls back to the host/shared connection string configured at startup. `ICurrentTenant` provides the `TenantId` for row-level data filtering. The orchestrator itself does not resolve connection strings — it delegates to the `TenantInitializationContext` which the caller populates from the tenant entity.

### Public API Contract

```csharp
public interface ITenantAppService
{
    /// <summary>Creates tenant and triggers initialization.
    /// Returns TenantDto with InitializationStatus.
    /// If initialization fails, the tenant is preserved as Failed (status written to entity).
    /// Caller uses GetInitializationStatusAsync for full step details.</summary>
    Task<TenantDto> CreateAsync(CreateTenantDto input);

    Task<TenantInitializationResult> RetryInitializationAsync(Guid tenantId);
    Task<TenantInitializationResult> GetInitializationStatusAsync(Guid tenantId);
    Task<TenantInitializationResult> ForceRetryInitializationAsync(Guid tenantId);
    Task ForceFailInitializationAsync(Guid tenantId);
    // ... existing methods
}
```

| Method | Permission | Dynamic API | Accepted tenant status |
|--------|------------|-------------|------------------------|
| `RetryInitializationAsync` | `Tenant.RetryInitialization` | `POST /api/tenants/{id}/retry-initialization` | `Pending`, `Failed` |
| `GetInitializationStatusAsync` | `Tenant.Read` | `GET /api/tenants/{id}/initialization-status` | Any |
| `ForceRetryInitializationAsync` | `Tenant.AdminForceRetry` | `POST /api/tenants/{id}/force-retry-initialization` | `Pending`, `Failed`, `Initializing` |
| `ForceFailInitializationAsync` | `Tenant.AdminForceRetry` | `POST /api/tenants/{id}/force-fail-initialization` | `Initializing` only |

**CreateAsync contract**: Returns `TenantDto` which includes the new fields `InitializationStatus`, `InitializedAt`, and `LastInitializationError`. If the orchestrator fails during initialization (business failure), the tenant entity is persisted with `Failed` status and `LastInitializationError` set, and `CreateAsync` still returns the `TenantDto` — it does not throw for initialization business failures. The caller inspects `TenantDto.InitializationStatus` and uses `GetInitializationStatusAsync` for the full step breakdown. If the host-DB transaction itself fails (infrastructure failure), `CreateAsync` throws.

**Force API scope**: `ForceFailInitializationAsync` only transitions `Initializing` → `Failed` (records error "manually marked as failed"). `ForceRetryInitializationAsync` accepts `Pending`/`Failed`/`Initializing` and uses `ForceBeginInitializationAsync` which gates on those three statuses. `Initialized` tenants are always rejected by force endpoints — re-initializing a successfully initialized tenant requires a separate future feature.

### Lifecycle Rules

| Scenario | Behavior |
|----------|----------|
| Success | `Tenant.InitializationStatus = Initialized` |
| Failure | Tenant preserved, status = `Failed`, error + record written |
| Retry | Only `Failed` or `Pending` tenants can retry |
| Already initialized | Retry denied: status = `Initialized` |
| Cleanup | Separate `DeleteTenantAsync` / `AbortTenantCreationAsync`, never auto-delete on failure |
| Concurrent retry | Atomic transition rejects second caller → conflict |
| Stuck at `Initializing` | If a process crashes mid-initialization, the tenant remains `Initializing` indefinitely. Recovery: `ForceFailInitializationAsync` updates the latest `Initializing` record to `Failed` (creating a recovery record only if no active record exists), and sets Tenant status to `Failed`. `ForceRetryInitializationAsync` uses `ForceBeginInitializationAsync` to start a new attempt from any of Pending/Failed/Initializing. Only admin-level APIs perform these operations — the standard retry endpoint only accepts `Pending`/`Failed`. |

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
| `TryBeginInitializationAsync` single transaction | Status transition (Pending/Failed → Initializing) + AttemptNo + record insert in one host-DB transaction. Returns new record, or null if transition fails. |
| `ForceBeginInitializationAsync` single transaction | Status transition (Pending/Failed/Initializing → Initializing) + AttemptNo + record insert + `reason` logged. Returns new record, or null if tenant is `Initialized` or in conflict. Used by `ForceRetryInitializationAsync`. |
| Null return → conflict | Caller gets conflict without touching any state in the non-transition case |
| UNIQUE INDEX on `(TenantId, AttemptNo)` | Safety net against duplicate attempt numbers within the transaction |
| No partial state | If record insert fails, the entire transaction rolls back — tenant is never left at `Initializing` with no record |

Both atomic methods live on `ITenantInitializationStore` (in `CrestCreates.Application`):

```csharp
/// <summary>Normal path: accepts Pending/Failed → Initializing.</summary>
Task<TenantInitializationRecord?> TryBeginInitializationAsync(
    Guid tenantId, string correlationId, CancellationToken cancellationToken);

/// <summary>Force path: accepts Pending/Failed/Initializing → Initializing (new record, new attempt).
/// Used by ForceRetryInitializationAsync to recover stuck or pre-failed tenants.</summary>
Task<TenantInitializationRecord?> ForceBeginInitializationAsync(
    Guid tenantId, string correlationId, string reason, CancellationToken cancellationToken);
```

`ForceFailInitializationAsync` is a separate operation that atomically transitions `Initializing` → `Failed`. It updates the latest `Initializing` record (if one exists) to `Failed` with the error "manually marked as failed". Only if no active `Initializing` record exists does it create a new recovery record. It does not start an initialization chain.

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
| `ITenantDatabaseInitializer` | Check if physical database exists; `CREATE DATABASE IF NOT EXISTS` — no-op if already present. Does NOT call EF Core `EnsureCreatedAsync`. |
| `ITenantMigrationRunner` | `Database.MigrateAsync()` — migration history table ensures no-op when no pending migrations |
| `ITenantDataSeeder` | Check admin role/user existence before insert |
| `ITenantSettingDefaultsSeeder` | Provider-specific upsert or get-or-create pattern, via existing Setting Management services |
| `ITenantFeatureDefaultsSeeder` | Same as settings, via existing Feature Management services |

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
| `CreateTenant_WhenInitializationFails_ShouldExposeFailureConsistently` | `CreateAsync` returns `TenantDto` with `InitializationStatus = Failed` and `LastInitializationError` set; `GetInitializationStatusAsync` returns full step breakdown. No throw for business failure. |
| `ForceRetryInitialization_FromInitializing_ShouldRecoverStuckTenant` | `ForceRetryInitializationAsync` on `Initializing` tenant uses `ForceBeginInitializationAsync`, restarts chain, completes successfully |
| `EfCoreTenantDatabaseInitializer_ShouldNotUseEnsureCreatedWithMigrations` | `ITenantDatabaseInitializer` only checks/creates physical database; does not call EF Core `EnsureCreatedAsync` which bypasses migrations |

### Additional Unit Tests

| Test | What it verifies |
|------|-----------------|
| `Initialization_WithConcurrentRequest_ShouldReturnConflict` | Two callers both see Failed, one gets lock via `TryBeginInitializationAsync`, other gets conflict |
| `RetryTenantInitialization_OnAlreadyInitialized_ShouldBeDenied` | Status = `Initialized` rejects retry |
| `Orchestrator_WithSharedDbContext_ShouldSkipPhase1And2` | `IsIndependentDatabase == false` → phase 1+2 not called |
| `EfCoreMigrationRunner_NoPendingMigrations_ShouldBeNoOp` | Idempotency of EF Core migration runner |
| `DataSeeder_DuplicateCall_ShouldNotCreateDuplicates` | Idempotency of seed phase |
| `SettingsDefaultsSeeder_UpsertPattern_ShouldBeIdempotent` | Provider-specific upsert works on retry |
| `ForceFailInitialization_OnlyFromInitializing_ShouldRejectOthers` | `ForceFailInitializationAsync` only transitions `Initializing` → `Failed`; calling on `Pending`/`Initialized`/`Failed` throws |

### Test Layer Placement

| Layer | What it tests | Test doubles |
|-------|---------------|--------------|
| Application unit | Orchestrator sequence, shared/independent branching, failure recording, retry, concurrency lock, force recovery | Fake phase handlers + fake `ITenantInitializationStore`. `TryBeginInitializationAsync` and `ForceBeginInitializationAsync` must simulate atomic transitions, not just Get+Set |
| OrmProviders tests | `EfCoreTenantDatabaseInitializer`, `EfCoreTenantMigrationRunner` | SQLite or provider-compatible test DB |
| Integration | Tenant status after creation, record persistence, seed/defaults queryable | Reuse existing integration harness |
| Optional E2E | admin can login | Only after auth test foundation is stable |

### Testing Principles

- Application unit tests are the primary layer — they validate orchestrator logic without real databases
- Migration failure tested at two levels: fake runner (orchestrator error handling) and real runner (migration behavior)
- Integration tests verify data is written correctly, not authentication
- Concurrent lock tests use `TryBeginInitializationAsync` and `ForceBeginInitializationAsync` with atomic transition semantics, not generic mock Get+Set
- Admin login verification is optional E2E, not a gate
- **Existing tests that verify "bootstrap failure deletes tenant" must be replaced** — the new design preserves the tenant on failure. Any legacy test asserting tenant deletion after bootstrap failure is testing a retired path and must be updated to assert tenant preservation with `Failed` status.
