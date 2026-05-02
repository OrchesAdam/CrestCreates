# Tenant Database Lifecycle — Design Spec

**Feature plan**: `docs/review/feature-plans/tenant-db-lifecycle.xml`
**Date**: 2026-05-02
**Status**: draft

## 1. Architecture & Components

### Call Chain

```
TenantAppService.CreateAsync
  → TenantManager.CreateAsync          // Domain: tenant aggregate rules only
  → TenantRepository.InsertAsync       // Write host DB tenant metadata
  → TenantInitializationOrchestrator.InitializeAsync  // Application: full init chain
       → ITenantDatabaseInitializer.InitializeAsync   // Independent DB only
       → ITenantMigrationRunner.RunAsync              // Independent DB only
       → ITenantDataSeeder.SeedAsync                  // Both shared & independent
       → ITenantSettingDefaultsSeeder.SeedAsync
       → ITenantFeatureDefaultsSeeder.SeedAsync
       → Update Tenant.InitializationStatus / InitializedAt / LastInitializationError
       → Write TenantInitializationRecord
```

TenantManager is a Domain-layer component — it handles tenant aggregate creation rules (name validation, uniqueness) and repository insertion. It does **not** call the orchestrator. `TenantAppService` owns the orchestration: create tenant → then initialize.

### New Abstractions (in `CrestCreates.Application.Contracts`)

| Interface | Method | Purpose |
|-----------|--------|---------|
| `ITenantDatabaseInitializer` | `Task<TenantDatabaseInitializeResult> InitializeAsync(TenantInitializationContext, CancellationToken)` | Ensure DB exists for independent tenant |
| `ITenantMigrationRunner` | `Task<TenantMigrationResult> RunAsync(TenantInitializationContext, CancellationToken)` | Run migrations on tenant DB |
| `ITenantDataSeeder` | `Task<TenantSeedResult> SeedAsync(TenantInitializationContext, CancellationToken)` | Seed admin user, default role, permissions |
| `ITenantSettingDefaultsSeeder` | `Task<TenantSettingDefaultsResult> SeedAsync(TenantInitializationContext, CancellationToken)` | Write default settings for tenant |
| `ITenantFeatureDefaultsSeeder` | `Task<TenantFeatureDefaultsResult> SeedAsync(TenantInitializationContext, CancellationToken)` | Write default feature values for tenant |

Existing `TenantBootstrapper` implements `ITenantDataSeeder` — it already handles admin user, role, and permission seeding.

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
    public bool IsIndependentDatabase => !string.IsNullOrEmpty(ConnectionString);
    public string CorrelationId { get; init; }
    public Guid? RequestedByUserId { get; init; }
}
```

### Status Model

**Enum** (in `CrestCreates.Domain.Shared`):

```csharp
public enum InitializationStatus
{
    Pending,
    Initializing,
    Initialized,
    Failed
}
```

**Tenant entity extensions** (in `CrestCreates.Domain`):

| Field | Type | Purpose |
|-------|------|---------|
| `InitializationStatus` | `InitializationStatus` | Current initialization state for availability checks |
| `InitializedAt` | `DateTime?` | When initialization completed successfully |
| `LastInitializationError` | `string?` | Sanitized, user-safe error message for diagnostics |

**TenantInitializationRecord entity** (in `CrestCreates.Domain`):

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | `Guid` | Primary key |
| `TenantId` | `Guid` | FK to Tenant |
| `Status` | `InitializationStatus` | Result status for this attempt |
| `CurrentStep` | `string?` | Which phase is executing (null when complete) |
| `StepResultsJson` | `string` | JSON array: `[{Name, Status, StartedAt, CompletedAt, Error}, ...]` |
| `Error` | `string?` | Detailed technical error (connection strings redacted) |
| `StartedAt` | `DateTime` | When this attempt started |
| `CompletedAt` | `DateTime?` | When this attempt finished |
| `AttemptNo` | `int` | Monotonically increasing per tenant |
| `CorrelationId` | `string` | Links to structured logs |

**TenantInitializationResult** (in `CrestCreates.Application.Contracts`):

| Field | Type | Purpose |
|-------|------|---------|
| `Success` | `bool` | Overall outcome |
| `Status` | `InitializationStatus` | Result status |
| `Error` | `string?` | Public-safe error message |
| `CorrelationId` | `string` | Links to logs and records |
| `Steps` | `IReadOnlyList<TenantInitializationStep>` | Per-step results |

`TenantInitializationStep`:

| Field | Type |
|-------|------|
| `Name` | `string` |
| `Status` | `InitializationStatus` |
| `StartedAt` | `DateTime?` |
| `CompletedAt` | `DateTime?` |
| `Error` | `string?` |

## 2. Data Flow

### Create Tenant — Full Chain

```
TenantAppService.CreateAsync(CreateTenantDto)
│
├─ 1. TenantManager.CreateAsync
│     ├─ Validate name uniqueness
│     ├─ Create Tenant entity (InitializationStatus = Pending)
│     └─ Insert into host DB via TenantRepository
│
├─ 2. TenantInitializationOrchestrator.InitializeAsync(context)
│     │
│     ├─ 2a. Atomic status transition:
│     │     UPDATE Tenant SET InitializationStatus = 'Initializing'
│     │     WHERE <tenant primary key> = context.TenantId
│     │       AND InitializationStatus IN ('Pending', 'Failed')
│     │     → 0 rows = return conflict
│     │
│     ├─ 2b. Compute AttemptNo = max(existing AttemptNo for tenant) + 1
│     │     (safe because only one caller holds the Initializing lock per tenant)
│     │
│     ├─ 2c. Insert TenantInitializationRecord (AttemptNo, Status = Initializing, CorrelationId)
│     │     UNIQUE INDEX on (TenantId, AttemptNo) as safety net
│     │
│     ├─ IF context.IsIndependentDatabase:
│     │   ├─ Phase 1: ITenantDatabaseInitializer.InitializeAsync
│     │   │   ├─ Update Record.CurrentStep = "DatabaseInitialize"
│     │   │   ├─ Append {Name, Status=Running, StartedAt} to StepResultsJson
│     │   │   └─ Ensure database exists, create if needed
│     │   └─ Phase 2: ITenantMigrationRunner.RunAsync
│     │       ├─ Update Record.CurrentStep = "Migration"
│     │       └─ Run pending migrations on tenant DB
│     │
│     ├─ Phase 3: ITenantDataSeeder.SeedAsync
│     │   └─ Admin user, default role, permissions
│     ├─ Phase 4: ITenantSettingDefaultsSeeder.SeedAsync
│     ├─ Phase 5: ITenantFeatureDefaultsSeeder.SeedAsync
│     │
│     ├─ 2d. On success:
│     │     ├─ Tenant.InitializationStatus = Initialized, InitializedAt = now
│     │     ├─ Record.Status = Initialized, CompletedAt = now, CurrentStep = null
│     │     └─ Return TenantInitializationResult { Success = true, Steps = [...] }
│     │
│     └─ 2e. On failure at any phase (state persisted first in one transaction):
│           ├─ Tenant.InitializationStatus = Failed
│           ├─ Tenant.LastInitializationError = sanitized error message
│           ├─ Record.Status = Failed
│           ├─ Record.CurrentStep = null
│           ├─ Record.Error = detailed error (connection strings redacted)
│           ├─ Record.StepResultsJson updated with failed step
│           ├─ Record.CompletedAt = now
│           └─ Return TenantInitializationResult { Success = false, Error = ..., Steps = [...] }
│               OR throw TenantInitializationException (state already persisted either way)
```

### Retry Flow

```
TenantAppService.RetryInitializationAsync(tenantId)
│
├─ Load tenant, verify status ∈ { Pending, Failed }
├─ Resolve TenantInitializationContext from tenant entity
└─ TenantInitializationOrchestrator.InitializeAsync(context)
    ├─ Atomic transition Pending/Failed → Initializing
    ├─ AttemptNo = max(existing) + 1 (computed after lock acquired)
    └─ Same chain as create; retry restarts from phase 1, not the failed phase.
       Idempotency ensures phases 1..N-1 are no-ops.
```

### Scenario Routing

| Condition | Strategy | Orchestrator Behavior |
|-----------|----------|-----------------------|
| `context.IsIndependentDatabase == true` | Independent tenant DB | EnsureCreated → Migrate → Seed → Defaults |
| `context.IsIndependentDatabase == false` | Shared tenant data | Skip DB init + migration, seed + defaults only |
| Host DB | Out of scope | Handled by app startup / deployment migrator |

### Lifecycle Rules

| Scenario | Behavior |
|----------|----------|
| Success | `Tenant.InitializationStatus = Initialized` |
| Failure | Tenant preserved, status = `Failed`, error + record written |
| Retry | Only `Failed` or `Pending` tenants can retry |
| Already initialized | Retry denied: status = `Initialized` |
| Cleanup | Separate `DeleteTenantAsync` / `AbortTenantCreationAsync`, never auto-delete on failure |
| Concurrent retry | Atomic transition rejects second caller → conflict |

## 3. Error Handling

### Per-Phase Flow

```
Each phase:
  ├─ Update Record.CurrentStep = phase name
  ├─ Append {Name, Status=Running, StartedAt} to StepResultsJson
  ├─ Try
  │   └─ Execute phase
  ├─ Success
  │   └─ Update StepResultsJson entry (Status=Success, CompletedAt)
  └─ Catch
      ├─ Update StepResultsJson entry (Status=Failed, Error, CompletedAt)
      ├─ Persist Tenant status + Record + step results in one transaction
      └─ Stop chain — do not proceed to next phase
```

### Concurrency Protection

| Mechanism | Purpose |
|-----------|---------|
| Atomic status update | `WHERE InitializationStatus IN ('Pending', 'Failed')` — only the caller that transitions to `Initializing` proceeds |
| 0 rows affected | Return conflict: "tenant is already initializing or initialized" |
| UNIQUE INDEX on `(TenantId, AttemptNo)` | Safety net against duplicate attempt numbers under extreme race |
| AttemptNo computed after lock | `max(existing) + 1` while holding the `Initializing` lock — no two callers can compute the same number |

Repository method for the atomic transition:

```csharp
// Returns true if the transition succeeded (1 row affected), false if rejected
Task<bool> TryBeginInitializationAsync(Guid tenantId, CancellationToken cancellationToken);
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
