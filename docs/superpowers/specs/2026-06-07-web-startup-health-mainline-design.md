# Web Startup And Health Mainline Design

## Goal

Unify the `CrestCreates.Web` host startup chain around generated module initialization, restore `/health` module initialization diagnostics, and keep the Web preset ergonomic without reintroducing runtime reflection or a long-term dual path.

The framework should feel close to the old API-controller project startup experience, but its maintained execution path must stay Minimal API, source-generated Dynamic API endpoints, and compile-time generated module initialization.

## Background

The SaaSHelpdesk sample exposed two related startup problems:

| Area | Observed behavior | Root issue |
|---|---|---|
| `/health` route | Returned 404 after host startup | Health endpoints were mapped from `HealthCheckAspNetCoreModule.OnApplicationInitializationAsync`, but module initialization was not reliably executing from the consuming app's generated initializer. |
| Module initialization | Framework-level `InitializeCrestAsync()` did not execute SaaSHelpdesk's generated `InitializeModulesAsync()` | The extension method lives in `CrestCreates.Web`, so compile-time binding resolves to the framework assembly's generated initializer, not the application assembly's generated initializer. |
| Module health info | `/health` returned `checks: []` after route mapping was restored | `AddModuleDiagnostics()` existed, and `ModuleHealthCheck("modules")` existed, but `CrestCreates.Web` did not wire it into the Web preset. |
| Migration/seeding startup | Both framework `CrestCreates.Web.Module.WebModule` and app `SaaSHelpdesk.Web.Modules.WebModule` could execute `HostMigrationAndSeedRunner` | Startup task ownership was ambiguous. The same application behavior existed in both framework and sample module initialization. |
| Tenant warnings | `/health` could trigger tenant resolution warnings | Health checks are infrastructure endpoints and should not run tenant resolution unless explicitly required by a health check. |

## Current Patch State To Preserve

These changes were already made during the debugging pass and should be treated as the baseline unless a later task deliberately replaces them:

| File | Current direction |
|---|---|
| `framework/src/CrestCreates.HealthCheck.AspNetCore/HealthCheckAspNetCoreEndpointRouteBuilderExtensions.cs` | Adds `MapCrestHealthChecks()` and maps `/health` from endpoint routing instead of module app-init. |
| `framework/src/CrestCreates.HealthCheck.AspNetCore/Modules/HealthCheckAspNetCoreModule.cs` | Registers health check services only; route mapping moved out of module initialization. |
| `framework/src/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs` | `AddCrestWeb()` calls `AddModuleDiagnostics()`; `MapCrestWeb()` calls `MapCrestHealthChecks()`. |
| `framework/src/CrestCreates.Web/Module/WebModule.cs` | No longer executes `HostMigrationAndSeedRunner`; application modules own application startup work. |
| `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs` | Calls app-generated `InitializeModulesAsync()` directly to avoid binding to the framework initializer. |
| `framework/src/CrestCreates.MultiTenancy.Abstract/SkipTenantResolutionMetadata.cs` | Marker metadata for endpoints that should skip tenant resolution. |
| `framework/src/CrestCreates.MultiTenancy/Middleware/MultiTenancyMiddleware.cs` | Skips tenant resolution when endpoint metadata or `/health` path indicates infrastructure endpoint. |

The verified runtime result for SaaSHelpdesk after these changes was:

- `/health` returned HTTP 200.
- Response included a `modules` check.
- The `modules` check reported 17 modules and 45 module phases.
- `HostMigrationAndSeedRunner` executed once.
- `/health` produced no tenant-resolution warning.

## Design Principles

| Principle | Design consequence |
|---|---|
| Generated mainline | Startup entry points should be emitted into the consuming app assembly when they need app-specific generated code. |
| Minimal API endpoint ownership | Endpoint mapping belongs in `MapCrestWeb()` and focused `MapCrest*()` helpers, not in module lifecycle hooks. |
| Single startup ownership | Framework modules should not execute sample/application migration and seeding logic. |
| Health as platform contract | `/health` should always exist when `AddCrestWeb()` + `MapCrestWeb()` are used, and it should include module initialization diagnostics by default. |
| AOT first | Avoid runtime assembly scanning, reflection fallback, and late-bound executor paths for startup, Dynamic API, and diagnostics. |
| Familiar Web experience | Keep host setup close to the traditional `builder.Services.AddCrestWeb<...>(); app.UseCrestWeb(); app.MapCrestWeb(); await app.Initialize...();` shape. |

## Target Architecture

### 1. Web Preset Surface

`CrestCreates.Web` should be the ergonomic host facade:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCrestWeb<SaaSHelpdeskWebModule>(builder.Configuration);

var app = builder.Build();

app.UseCrestWeb();
app.MapCrestWeb();

await app.InitializeCrestApplicationAsync();
await app.RunAsync();
```

The important detail is that `InitializeCrestApplicationAsync()` must be generated into the application assembly, not defined only by `CrestCreates.Web`.

Recommended long-term shape:

| Method | Owner | Responsibility |
|---|---|---|
| `AddCrestWeb<TModule>()` | Framework | Register Web preset services, module diagnostics, health checks, Dynamic API generated runtime, OpenAPI, auth integration, tenant middleware dependencies. |
| `UseCrestWeb()` | Framework | Add middleware in deterministic order. |
| `MapCrestWeb()` | Framework | Map framework-provided endpoints: health, generated Dynamic API, OpenAPI, auth endpoints. |
| `InitializeCrestApplicationAsync()` | Generated app code | Call the consuming app's generated module initializer and future generated startup-task executor. |

### 2. App-Local Generated Initializer

The current framework-level `InitializeCrestAsync()` is risky because it can only see generated code in the framework assembly. The next design step should move the convenient startup method into generated application code.

The generator should emit something equivalent to:

```csharp
namespace Microsoft.AspNetCore.Builder;

public static partial class CrestGeneratedApplicationInitializationExtensions
{
    public static Task InitializeCrestApplicationAsync(this WebApplication app)
    {
        return app.InitializeModulesAsync();
    }
}
```

This keeps application `Program.cs` concise while preserving compile-time binding to the generated initializer in the consuming app assembly.

### 3. Startup Tasks

Application-level startup work such as migration and seeding should not be hard-coded in `CrestCreates.Web.Module.WebModule`.

Preferred model:

```csharp
public interface ICrestStartupTask
{
    int Order { get; }

    Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken);
}
```

The generator can emit a task executor when tasks are statically known. For app-owned tasks, the application module should register or expose the task explicitly. This makes startup work discoverable, testable, and single-owned.

Short-term acceptable state:

- Keep `HostMigrationAndSeedRunner` execution only in `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Modules/WebModule.cs`.
- Do not re-add it to `framework/src/CrestCreates.Web/Module/WebModule.cs`.

Long-term preferred state:

- Move `HostMigrationAndSeedRunner` behind a startup task contract.
- Generate an app-local startup-task executor.
- Let the app module opt into the task once.

### 4. Health Endpoint

`/health` should be mapped by endpoint routing:

```csharp
app.MapCrestHealthChecks();
```

`MapCrestWeb()` should call that helper by default. This gives framework users the expected health endpoint without requiring them to remember a separate mapping call.

The endpoint should:

- Return framework health output in a stable JSON shape.
- Include the `modules` health check from `CrestCreates.ModuleDiagnostics`.
- Carry endpoint metadata that tells multi-tenancy middleware to skip tenant resolution.
- Be override-friendly later by options, not by duplicating route mapping paths.

### 5. Module Diagnostics

`AddCrestWeb()` should register module diagnostics as part of the Web preset:

```csharp
services.AddModuleDiagnostics();
```

This preserves the existing platform capability: module initialization information is visible through `/health`.

Current module diagnostics are tied to generated code and a static diagnostics store. That is acceptable for the current fix, but the cleaner architecture is:

| Phase | Store model |
|---|---|
| Short-term | Preserve generated diagnostics recording and `ModuleHealthCheck` behavior. |
| Medium-term | Prefer DI-owned `IModuleDiagnosticsStore` so generated initialization writes into a registered singleton. |
| Avoid | Runtime scanning of modules to reconstruct diagnostics. |

### 6. Tenant Resolution Skip

Infrastructure endpoints such as `/health` should skip tenant resolution by metadata.

Current acceptable implementation:

```csharp
endpoint.Metadata.GetMetadata<SkipTenantResolutionMetadata>() is not null
```

The fallback path check for `/health` is a transitional guard so the endpoint remains quiet if middleware order prevents endpoint metadata from being available. If routing order is normalized and tested, the fallback can be removed later.

Possible future refinement:

- Move endpoint metadata abstractions to a neutral ASP.NET Core abstraction package if more middleware uses the same pattern.
- Keep `CrestCreates.MultiTenancy.Abstract` as the short-term home because only multi-tenancy currently consumes it.

## Developer Experience

The desired experience for framework users:

- They call one Web preset registration method.
- They call one Web mapping method.
- They call one app-local generated initialization method.
- They do not need to know that Dynamic API is generated Minimal API internally.
- They can still add normal Minimal API endpoints, middleware, authentication, OpenAPI customization, and app-specific startup tasks.

The surface should remain close to traditional API-controller hosting:

```csharp
builder.Services.AddCrestWeb<MyWebModule>();

var app = builder.Build();

app.UseCrestWeb();
app.MapCrestWeb();

await app.InitializeCrestApplicationAsync();
await app.RunAsync();
```

This avoids making users choose between "old MVC style" and "raw Minimal API style". The framework owns the generated endpoint mapping and exposes a familiar preset.

## Non-Goals

| Non-goal | Reason |
|---|---|
| Reintroduce MVC controller wrappers | Dynamic API mainline is source-generated Minimal API. |
| Add runtime module scanning fallback | Violates AOT-first and generated-mainline principles. |
| Keep framework and app migration runners both active | Causes duplicate side effects and unclear ownership. |
| Create a standalone module diagnostics endpoint | `/health` is the established diagnostics surface. |
| Solve all MultiTenancy nullable/AOT warnings in this same change | Important cleanup, but separate from startup-chain correctness. |

## Testing Strategy

Required tests:

| Test | Purpose |
|---|---|
| `MapCrestWeb_ShouldMapHealthEndpoint` | Verifies `/health` is part of the Web preset. |
| `AddCrestWeb_ShouldRegisterModuleDiagnostics` | Verifies the `modules` health check is registered by default. |
| `HealthEndpoint_ShouldSkipTenantResolution` | Verifies health endpoint metadata prevents tenant resolution. |
| `MultiTenancyMiddleware_ShouldSkipTenantResolution_WhenEndpointHasMetadata` | Verifies middleware behavior independent of health checks. |
| SaaSHelpdesk integration startup check | Verifies `/health` returns `modules`, migrations run once, and no tenant warning is emitted. |
| Generator test for app-local initializer | Verifies generated `InitializeCrestApplicationAsync()` calls the consuming app's generated initializer. |
| Startup-task test | Verifies app startup tasks execute once in deterministic order. |

## Known Verification Notes

Commands already run during the debugging pass:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj -v:minimal --filter "FullyQualifiedName~CrestCreates.Web.Tests.Middlewares.MultiTenancyMiddlewareTests|FullyQualifiedName~CrestCreates.Web.Tests.CrestWebPresetTests" -m:1
dotnet build samples/SaaSHelpdesk/SaaSHelpdesk.Web/SaaSHelpdesk.Web.csproj -v:minimal
```

Observed result:

- Focused Web tests passed.
- SaaSHelpdesk build passed.
- SaaSHelpdesk `/health` returned healthy module diagnostics when its configured database was available.

Known caveats:

- Full `CrestCreates.Web.Tests` had one unrelated Dynamic API test-order/static-state failure: `ControllerOnlyProvider_ShouldBuildRegistryAndMapEndpoints`. The test passes alone.
- `CrestCreates.MultiTenancy` still emits existing nullable and AOT warnings. These should be handled as a separate cleanup unless they block the Web startup mainline.
- SaaSHelpdesk runtime verification requires its configured database to be available.

## Acceptance Criteria

The design is complete when:

1. A new Web app can use the Web preset without manually mapping `/health`.
2. `/health` returns HTTP 200 and includes the `modules` check.
3. `/health` does not trigger tenant-resolution warnings.
4. App module initialization runs from app-generated code, not framework-generated code.
5. App startup side effects such as migrations/seeding run exactly once.
6. No runtime reflection fallback is introduced for Dynamic API, module initialization, diagnostics, or startup tasks.
7. Tests make the generated mainline obvious to future maintainers.
