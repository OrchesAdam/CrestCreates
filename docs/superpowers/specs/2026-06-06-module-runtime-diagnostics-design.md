# Module Runtime Diagnostics Design

## Goal

Add runtime verification, visualization, and health check capabilities to the CrestCreates module system. Close the remaining gaps after compile-time topological sort, circular dependency detection, and `ConfigureServices` exception wrapping have been completed.

## Scope

| Area | Decision |
|---|---|
| Runtime dependency verification | Yes — fail-fast on startup if any module dependency cannot resolve |
| Lifecycle phase timing | Yes — record elapsed time per module per phase |
| Failure diagnostics | Yes — capture module name, phase, and error message on failure |
| Structured log output | Yes — log module diagnostics summary at startup |
| Health Check endpoint integration | Yes — add `modules` entry to existing `/health` endpoint |
| Independent diagnostic API endpoint | No — use existing `/health` endpoint |
| Persistent storage | No — in-memory only, startup diagnostics only |
| Continuous runtime monitoring | No — that is the health check system's responsibility |

## Non-Goals

| Non-goal | Reason |
|---|---|
| Do not introduce a standalone `/api/diagnostics/modules` endpoint | `/health` already serves this role |
| Do not persist diagnostics to database | Startup-time diagnostics; audit logging handles persistence |
| Do not add continuous module health monitoring | Existing `TenantHealthCheck`, `DatabaseHealthCheck` cover runtime monitoring |
| Do not modify `IModule` or `ModuleBase` | Diagnostics are injected via code generation, keeping the module contract unchanged |
| Do not introduce runtime reflection | All diagnostic collection is code-generated with static type references |

## Current State

| Component | Current behavior | Gap |
|---|---|---|
| `ModuleAutoInitializer` (BuildTasks) | Executes lifecycle hooks with generic try/catch/log/rethrow on `ConfigureServices`; other phases are bare calls | No timing, no structured diagnostics, no failure detail capture |
| `ModuleAutoInitializer` (SourceGenerator) | Same pattern, with `ILogger` calls for phase entry | No timing, no structured diagnostics store |
| `CrestCreates.HealthCheck` | `TenantHealthCheck`, `IHealthCheckService` | No module-level health information |
| `CrestCreates.HealthCheck.Mvc` | `MemoryHealthCheck`, `DatabaseHealthCheck`, `RedisHealthCheck`, `HealthController` | No module health check |
| `ModuleBase` | `Name`, `Description`, `Version` virtual properties | Not used for diagnostics |

## Architecture

### New Project: `CrestCreates.ModuleDiagnostics`

```
framework/src/CrestCreates.ModuleDiagnostics/
├── CrestCreates.ModuleDiagnostics.csproj
├── Modules/
│   └── ModuleDiagnosticsModule.cs
├── Stores/
│   ├── IModuleDiagnosticsStore.cs
│   ├── ModuleDiagnosticsStore.cs
│   └── ModulePhaseDiagnostic.cs
├── Timing/
│   └── ModulePhaseTimer.cs
└── HealthChecks/
    └── ModuleHealthCheck.cs
```

| Component | Location | Responsibility |
|---|---|---|
| `ModulePhaseTimer` | `Timing/` | Value-type stopwatch wrapper. `StartNew(moduleName, phase)` → `Stop(status)` / `StopFailed(ex)` → `ModulePhaseDiagnostic`. Zero heap allocation. |
| `ModulePhaseDiagnostic` | `Stores/` | Readonly struct: `ModuleName`, `Phase`, `Status` (enum), `Elapsed` (TimeSpan), `ErrorMessage` (string?). |
| `IModuleDiagnosticsStore` | `Stores/` | In-memory store interface: `Record()`, `GetAll()`, `GetByModule()`, `GetFailed()`, `HasFailures`, `TotalCount`. |
| `ModuleDiagnosticsStore` | `Stores/` | Default implementation. `ConcurrentDictionary<string, List<ModulePhaseDiagnostic>>` keyed by module name. Write-once, read-only after startup. |
| `ModuleDiagnosticsModule` | `Modules/` | `[CrestModule]`, registers `IModuleDiagnosticsStore` as singleton in `OnConfigureServices`. |
| `ModuleHealthCheck` | `HealthChecks/` | `[HealthCheck]` attributed `IHealthCheck`. Reads store, returns `Healthy` if all pass, `Unhealthy` if any module failed. |

### Dependency Direction

```
CrestCreates.Modularity
        ↓
CrestCreates.ModuleDiagnostics   (new)
        ↓
CrestCreates.HealthCheck         (extended: references ModuleDiagnostics)
        ↓
CrestCreates.HealthCheck.AspNetCore / HealthCheck.Mvc
```

`ModuleDiagnostics` depends only on `Modularity`, `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions`, and `Microsoft.Extensions.Logging.Abstractions`. It does not depend on AspNetCore, MVC, or any Web layer — usable in Worker Service scenarios.

### Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Modularity\CrestCreates.Modularity.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
</Project>
```

## Data Model

### ModulePhaseTimer

```csharp
namespace CrestCreates.ModuleDiagnostics.Timing;

public readonly struct ModulePhaseTimer
{
    private readonly string _moduleName;
    private readonly string _phase;
    private readonly long _startTimestamp;

    private ModulePhaseTimer(string moduleName, string phase, long startTimestamp)
    {
        _moduleName = moduleName;
        _phase = phase;
        _startTimestamp = startTimestamp;
    }

    public static ModulePhaseTimer StartNew(string moduleName, string phase)
    {
        return new ModulePhaseTimer(moduleName, phase, Stopwatch.GetTimestamp());
    }

    public ModulePhaseDiagnostic Stop(ModulePhaseStatus status)
    {
        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        return new ModulePhaseDiagnostic(_moduleName, _phase, status, elapsed, null);
    }

    public ModulePhaseDiagnostic StopFailed(Exception ex)
    {
        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        return new ModulePhaseDiagnostic(_moduleName, _phase, ModulePhaseStatus.Failed, elapsed, ex.Message);
    }
}
```

### ModulePhaseDiagnostic

```csharp
namespace CrestCreates.ModuleDiagnostics.Stores;

public enum ModulePhaseStatus
{
    Success,
    Failed
}

public readonly struct ModulePhaseDiagnostic
{
    public string ModuleName { get; }
    public string Phase { get; }
    public ModulePhaseStatus Status { get; }
    public TimeSpan Elapsed { get; }
    public string? ErrorMessage { get; }

    public ModulePhaseDiagnostic(string moduleName, string phase, ModulePhaseStatus status, TimeSpan elapsed, string? errorMessage)
    {
        ModuleName = moduleName;
        Phase = phase;
        Status = status;
        Elapsed = elapsed;
        ErrorMessage = errorMessage;
    }
}
```

### IModuleDiagnosticsStore

```csharp
namespace CrestCreates.ModuleDiagnostics.Stores;

public interface IModuleDiagnosticsStore
{
    void Record(ModulePhaseDiagnostic diagnostic);
    IReadOnlyList<ModulePhaseDiagnostic> GetAll();
    IReadOnlyList<ModulePhaseDiagnostic> GetByModule(string moduleName);
    IReadOnlyList<ModulePhaseDiagnostic> GetFailed();
    bool HasFailures { get; }
    int TotalCount { get; }
}
```

## Code Generation Changes

### BuildTasks Path (`GenerateAggregatedModuleCode.cs`)

**Current** (no diagnostics):
```csharp
// RegisterAllModules:
try {
    new WebModule().OnConfigureServices(services);
} catch (Exception ex) { ... throw; }

// InitializeAllModulesAsync:
var module0 = serviceProvider.GetService<WebModule>();
if (module0 != null) await module0.OnPreInitializeAsync();
// ... Init, PostInit, AppInit without timing
```

**Target** (with diagnostics):
```csharp
// RegisterAllModules — ConfigureServices phase:
var timer0 = ModulePhaseTimer.StartNew("WebModule", "ConfigureServices");
try
{
    new WebModule().OnConfigureServices(services);
    _diagnostics.Record(timer0.Stop(ModulePhaseStatus.Success));
}
catch (Exception ex)
{
    _diagnostics.Record(timer0.StopFailed(ex));
    throw;
}

// InitializeAllModulesAsync — PreInit/Init/PostInit/AppInit phases:
// Each lifecycle call is wrapped with the same timer → record pattern.

// After all modules initialized, output summary:
var summary = _diagnostics.GetAll();
foreach (var d in summary)
{
    logger.LogInformation("[ModuleDiagnostics] {ModuleName}: {Status} ({Phase} {Elapsed}ms)", ...);
}
```

**Key rules for generated code:**
- One method call per line. No chained calls as method arguments.
- Timer, try/catch, Record, throw — each on its own line.
- Summary log output is a simple foreach loop, one `LogInformation` per diagnostic record.

### SourceGenerator Path (`ModuleSourceGenerator.cs`)

Same pattern applied to `AutoModuleRegistration.g.cs`. The `InitializeModulesAsync` method currently has bare lifecycle calls; each gets wrapped with `ModulePhaseTimer`.

### Static Store Access

Generated code accesses the store via a static field on the generated class:

```csharp
private static readonly ModuleDiagnosticsStore _diagnostics = new();
```

This static instance is the **authoritative store**. `ModuleDiagnosticsModule` registers this same instance into DI via a factory:

```csharp
services.AddSingleton<IModuleDiagnosticsStore>(_ => ModuleAutoInitializer.DiagnosticsStore);
```

This ensures `ModuleHealthCheck` (resolved from DI) reads from the same store that the generated code writes to.

The generated class exposes the store via an `internal static` property:

```csharp
internal static ModuleDiagnosticsStore DiagnosticsStore => _diagnostics;
```

## Visualization

### Startup Log Output

After all modules are initialized, the generated code outputs one structured log line per module:

```
[ModuleDiagnostics] HealthCheckModule: Success (PreInit 0.1ms, Init 9.2ms, PostInit 0.0ms, AppInit 1.3ms, ConfigureServices 3.1ms)
[ModuleDiagnostics] SecurityModule: Failed (Init 5.0ms → Unable to resolve IPasswordHasher)
[ModuleDiagnostics] AuditLoggingModule: Success (PreInit 0.2ms, Init 4.1ms, PostInit 0.1ms, AppInit 0.5ms, ConfigureServices 1.0ms)
```

Format: fixed string concatenation in generated code. No `StringBuilder`, no formatter reflection, AoT-friendly.

Log level: `Information` for all-success modules, `Error` for failed modules.

### Health Check Endpoint Integration

`ModuleHealthCheck` implements `IHealthCheck`, attributed with `[HealthCheck(Name = "Modules", Tags = new[] { "modules" })]`.

The source generator `HealthCheckSourceGenerator` picks it up and generates `AddModuleHealthCheck` extension method.

**Healthy response** (all modules passed):
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:01.234",
  "checks": [
    {"name": "modules", "status": "Healthy", "duration": "00:00:00.001", "data": {"totalModules": 46, "failedModules": 0, "totalPhases": 230, "failedPhases": 0}}
  ]
}
```

**Unhealthy response** (one or more modules failed):
```json
{
  "status": "Unhealthy",
  "totalDuration": "00:00:00.456",
  "checks": [
    {"name": "modules", "status": "Unhealthy", "duration": "00:00:00.001", "data": {"totalModules": 46, "failedModules": 2, "failedDetails": [{"module": "SecurityModule", "phase": "Init", "error": "Unable to resolve IPasswordHasher"}, {"module": "DataCoreModule", "phase": "ConfigureServices", "error": "Connection string not configured"}]}}
  ]
}
```

HTTP status: 503 when unhealthy.

## Failure Strategy

**Fail-fast**: If any module phase throws, the generated code records the failure and rethrows. The application does not start in a degraded state.

- `ConfigureServices` failure → host build fails → application does not start
- `PreInit` / `Init` / `PostInit` failure → `InitializeModulesAsync` throws → application does not start
- `AppInit` failure → `InitializeModulesAsync` throws → application does not start

The diagnostics store captures the failure details before rethrowing, so the `/health` endpoint (if the app somehow reaches a partially initialized state) can report which module failed and why.

No `IsRequired` flag or optional module support in this implementation. All framework modules are required.

## Testing Strategy

### Unit Tests (`CrestCreates.ModuleDiagnostics.Tests`)

#### Happy Path

| Test | Verifies |
|---|---|
| `ModulePhaseTimer_StartNew_Stop_ShouldRecordElapsedTime` | Timer produces non-zero `Elapsed` |
| `ModuleDiagnosticsStore_Record_ShouldStoreDiagnostic` | `GetAll()` includes recorded item |
| `ModuleDiagnosticsStore_GetByModule_ShouldReturnAllPhasesForModule` | Filter by module name returns all its phases |
| `ModuleDiagnosticsStore_GetFailed_ShouldReturnOnlyFailures` | `GetFailed()` filters correctly |
| `ModuleHealthCheck_AllSuccess_ShouldReturnHealthy` | All phases success → `Healthy` result |
| `ModuleHealthCheck_EmptyStore_ShouldReturnHealthy` | No diagnostics yet → `Healthy` (startup not complete) |

#### Failure Path

| Test | Verifies |
|---|---|
| `ModulePhaseTimer_StopFailed_ShouldCaptureErrorMessage` | `StopFailed(ex)` records `Status = Failed` and `ErrorMessage = ex.Message` |
| `ModuleDiagnosticsStore_Record_FailedPhase_ShouldSetHasFailures` | Any failure recorded → `HasFailures` is `true` |
| `ModuleDiagnosticsStore_GetByModule_WithPartialFailure_ShouldReturnAllPhases` | Even with failed phases, the module's successful phases are still returned |
| `ModuleDiagnosticsStore_GetAll_AfterMixedResults_ShouldContainBothSuccessAndFailure` | `GetAll()` returns both success and failure records |
| `ModuleHealthCheck_AnyFailure_ShouldReturnUnhealthy` | ≥1 failure → `Unhealthy` result with failure details |
| `ModuleHealthCheck_OnlyFailedModule_ShouldListFailureInData` | Response data includes `failedModules` count and `failedDetails` |
| `ModuleHealthCheck_MultipleModulesFailed_ShouldCountAll` | Multiple failures all appear in `failedDetails` |

#### Generated Code Structure

| Test | Verifies |
|---|---|
| `GeneratedCode_WrapsEachPhaseWithTimer` | Every lifecycle call is surrounded by `ModulePhaseTimer.StartNew` / `Stop` |
| `GeneratedCode_PreInitFailure_RecordsThenRethrows` | Catch block calls `_diagnostics.Record(...)` on its own line, then `throw` on the next line |
| `GeneratedCode_InitFailure_DoesNotSwallowException` | Exception propagates; no empty catch blocks |
| `GeneratedCode_AppInitFailure_IncludesModuleNameInLog` | Log output contains the failing module's name and phase |

### Integration Tests

| Test | Verifies |
|---|---|
| `ModuleInit_AllPhases_ShouldBeRecordedInStore` | After `WebApplicationFactory` startup, store has 5 phases per module |
| `ModuleInit_Failure_ShouldPreventStartup` | A module that throws in `OnInitializeAsync` prevents app startup |
| `HealthEndpoint_IncludesModuleStatus` | `GET /health` response includes `modules` check entry |
| `HealthEndpoint_ModuleFailure_Returns503` | When a module fails (test-only `FailingModule`), `/health` returns 503 |
| `ModuleInit_PartialFailure_DoesNotReachLaterPhases` | If `PreInit` fails for module N, modules after it in the sorted list do not execute |

## Acceptance Criteria

| Criterion | Expected result |
|---|---|
| All module lifecycle phases are timed | Store contains one `ModulePhaseDiagnostic` per module per phase executed |
| Failure records error message | Failed phase diagnostic has `ErrorMessage` set and `Status = Failed` |
| Fail-fast on initialization failure | Any module phase exception prevents application startup |
| Startup log output | Structured log lines output per module with phase timings |
| `/health` includes module status | `modules` entry in health check response |
| `/health` returns 503 on module failure | Unhealthy status when any module failed |
| Zero heap allocation for timing | `ModulePhaseTimer` is a readonly struct using `Stopwatch.GetTimestamp()` |
| No reflection | All diagnostic collection is compile-time generated code |
| AoT compatible | No `MakeGenericType`, `Assembly.GetTypes`, or runtime IL emit |
| Single main chain | Only the generated `ModuleAutoInitializer` path is enhanced; the legacy runtime `RegisterAllModules` is unchanged |

## Changes to Existing Projects

| Project | Change |
|---|---|
| `CrestCreates.BuildTasks/GenerateAggregatedModuleCode.cs` | Update `GenerateCode()` to emit timer-wrapped lifecycle calls and summary log output |
| `CrestCreates.CodeGenerator/ModuleGenerator/ModuleSourceGenerator.cs` | Update `GenerateAutoModuleRegistration()` to emit timer-wrapped lifecycle calls and summary log output |
| `CrestCreates.HealthCheck/CrestCreates.HealthCheck.csproj` | Add `ProjectReference` to `CrestCreates.ModuleDiagnostics` |
| `CrestCreates.HealthCheck.AspNetCore/` | No changes (module diagnostics health check registers automatically via `[HealthCheck]` attribute) |
| `CrestCreates.HealthCheck.Mvc/` | No changes (same reason) |
