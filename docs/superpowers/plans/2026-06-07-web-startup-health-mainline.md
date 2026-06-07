# Web Startup And Health Mainline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stabilize the Web startup mainline so generated app-local initialization, `/health`, module diagnostics, and tenant-skip behavior all follow the AOT/source-generation-first path.

**Architecture:** The framework keeps the Web preset surface, health mapping, middleware order, and module diagnostics wiring. The consuming application owns the generated initializer and any app startup side effects. The maintained startup entry is app-local `InitializeCrestApplicationAsync()`, while the framework-level `InitializeCrestAsync()` becomes a deprecated compatibility surface that must not be treated as the preferred path.

**Tech Stack:** .NET 10, ASP.NET Core Minimal API, health checks, source generators, generated module initialization, xUnit, FluentAssertions, Testcontainers-backed SaaSHelpdesk verification, existing CrestCreates Web and module diagnostics test projects.

---

## File Structure

### Files To Preserve Or Update

- `framework/src/CrestCreates.HealthCheck.AspNetCore/HealthCheckAspNetCoreEndpointRouteBuilderExtensions.cs`
  - Owns `/health` endpoint mapping and endpoint metadata.
- `framework/src/CrestCreates.HealthCheck.AspNetCore/Modules/HealthCheckAspNetCoreModule.cs`
  - Registers health services only.
- `framework/src/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs`
  - Owns `AddCrestWeb()`, `UseCrestWeb()`, `MapCrestWeb()`, and the framework-level startup entry that should be deprecated in favor of app-local generated initialization.
- `framework/src/CrestCreates.Web/Module/WebModule.cs`
  - Must remain free of app migration or seeding side effects.
- `framework/tools/CrestCreates.CodeGenerator/ModuleGenerator/ModuleSourceGenerator.cs`
  - Generates module initialization and the app-local initializer surface.
- `framework/test/CrestCreates.CodeGenerator.Tests/ModuleGeneratorTests.cs`
  - Verifies generated module initialization output.
- `framework/test/CrestCreates.CodeGenerator.Tests/TestHelpers/SourceGeneratorTestHelper.cs`
  - Compiles generator inputs and captures generated source.
- `framework/test/CrestCreates.ModuleDiagnostics.Tests/HealthChecks/ModuleHealthCheckTests.cs`
  - Existing module diagnostics coverage to extend or preserve.
- `framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs`
  - Existing Web preset coverage, including the framework initializer exposure test that should no longer enshrine the wrong startup path.
- `framework/test/CrestCreates.Web.Tests/Middlewares/MultiTenancyMiddlewareTests.cs`
  - Existing tenant-skip behavior coverage.
- `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs`
  - Consumes the generated app-local initializer.
- `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Modules/WebModule.cs`
  - Temporary owner of `HostMigrationAndSeedRunner`.
- `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/Fixtures/HelpdeskWebApplicationFactory.cs`
  - Automated runtime verification entry point for SaaSHelpdesk.

### Current Generator Output To Treat As The Mainline

- `AutoModuleRegistration.g.cs`
  - Generated module registration output.
- Generated `InitializeModulesAsync`
  - The module initializer emitted by `ModuleSourceGenerator.cs`.
- Generated app-local `InitializeCrestApplicationAsync`
  - The maintained startup helper emitted into the consuming app assembly.

---

## Task 1: Stabilize The Existing Web Health Patch

**Files:**

- Review: `framework/src/CrestCreates.HealthCheck.AspNetCore/HealthCheckAspNetCoreEndpointRouteBuilderExtensions.cs`
- Review: `framework/src/CrestCreates.HealthCheck.AspNetCore/Modules/HealthCheckAspNetCoreModule.cs`
- Review: `framework/src/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs`
- Review: `framework/src/CrestCreates.Web/Module/WebModule.cs`
- Review: `framework/src/CrestCreates.MultiTenancy/Middleware/MultiTenancyMiddleware.cs`
- Test: `framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs`
- Test: `framework/test/CrestCreates.Web.Tests/Middlewares/MultiTenancyMiddlewareTests.cs`
- Test: `framework/test/CrestCreates.ModuleDiagnostics.Tests/HealthChecks/ModuleHealthCheckTests.cs`

- [ ] **Step 1: Confirm the Web preset maps `/health` through endpoint routing**

Check that `MapCrestWeb()` contains:

```csharp
app.MapCrestHealthChecks();
```

`/health` must stay in the Web mapping layer, not in module initialization.

- [ ] **Step 2: Confirm Web preset service registration still includes module diagnostics**

Check that `AddCrestWeb()` contains:

```csharp
services.AddModuleDiagnostics();
```

Do not replace this with a plain `AddHealthChecks()`-only path.

- [ ] **Step 3: Confirm the framework Web module stays side-effect free**

`framework/src/CrestCreates.Web/Module/WebModule.cs` should not execute `HostMigrationAndSeedRunner` or any other app-owned startup side effect.

Expected shape:

```csharp
namespace CrestCreates.Web.Module;

using CrestCreates.Modularity;

public class WebModule : ModuleBase
{
}
```

- [ ] **Step 4: Run the focused Web and module diagnostics tests**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj -v:minimal --filter "FullyQualifiedName~CrestCreates.Web.Tests.Middlewares.MultiTenancyMiddlewareTests|FullyQualifiedName~CrestCreates.Web.Tests.CrestWebPresetTests" -m:1
dotnet test framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj -v:minimal -m:1
```

Expected:

```text
Passed!
```

If these fail, fix the current patch before touching generator output.

---

## Task 2: Generate The App-Local Startup Entry

**Files:**

- Modify: `framework/tools/CrestCreates.CodeGenerator/ModuleGenerator/ModuleSourceGenerator.cs`
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/ModuleGeneratorTests.cs`
- Modify: `framework/test/CrestCreates.CodeGenerator.Tests/TestHelpers/SourceGeneratorTestHelper.cs`
- Modify after generator output exists: `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs`

- [ ] **Step 1: Verify the current generator entry points and emitted file name**

Run:

```powershell
rg "InitializeModulesAsync|AutoModuleRegistration.g.cs|InitializeCrestAsync|InitializeCrestApplicationAsync" framework/tools/CrestCreates.CodeGenerator framework/test/CrestCreates.CodeGenerator.Tests samples/SaaSHelpdesk -n
```

Expected:

- `ModuleSourceGenerator.cs` is the generator entry point.
- `AutoModuleRegistration.g.cs` is the generated module registration file name.
- `InitializeModulesAsync` is the generated module initializer being emitted.
- `InitializeCrestApplicationAsync` is not yet the maintained app-local entry, so the plan must add it to generator coverage.

- [ ] **Step 2: Add a generator test for the app-local initializer**

Add or extend a test in `framework/test/CrestCreates.CodeGenerator.Tests/ModuleGeneratorTests.cs` that asserts the generated app code contains:

```csharp
public static Task InitializeCrestApplicationAsync(this WebApplication app)
{
    return app.InitializeModulesAsync();
}
```

The assertion must inspect generated output for the consuming app assembly, not for `CrestCreates.Web`.

- [ ] **Step 3: Add the generator helper assertion that ties the new method to app-local output**

Update `framework/test/CrestCreates.CodeGenerator.Tests/TestHelpers/SourceGeneratorTestHelper.cs` so the test can locate the generated app-local file that contains `InitializeCrestApplicationAsync`.

The helper should keep the existing generated-source capture pattern and return the generated file text for inspection.

- [ ] **Step 4: Emit the app-local initializer from the source generator**

Extend `framework/tools/CrestCreates.CodeGenerator/ModuleGenerator/ModuleSourceGenerator.cs` so the consuming app receives:

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

Keep the generated initializer next to the generated module initializer logic so the two pieces stay aligned.

- [ ] **Step 5: Switch the SaaSHelpdesk entry point to the generated app-local method**

Update `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs` so it calls:

```csharp
await app.InitializeCrestApplicationAsync();
```

This is the maintained startup entry for applications.

- [ ] **Step 6: Run the generator-focused test**

Run:

```powershell
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj -v:minimal --filter "FullyQualifiedName~InitializeCrestApplicationAsync" -m:1
```

Expected:

```text
Passed!
```

Then run the broader module generator tests in the same project.

---

## Task 3: Keep Startup Side Effects Single-Owned

**Files:**

- Review: `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Modules/WebModule.cs`
- Review: `framework/src/CrestCreates.Web/Module/WebModule.cs`
- Review: `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/Fixtures/HelpdeskWebApplicationFactory.cs`
- Test: add or update a SaaSHelpdesk startup verification test

- [ ] **Step 1: Verify `HostMigrationAndSeedRunner` is owned only by the app module**

Run:

```powershell
rg "HostMigrationAndSeedRunner" framework samples -n
```

Expected:

- `samples/SaaSHelpdesk/SaaSHelpdesk.Web/Modules/WebModule.cs` is the only live owner of the migration and seeding runner.
- `framework/src/CrestCreates.Web/Module/WebModule.cs` does not execute it.

- [ ] **Step 2: Add a verification that the side effect runs once**

Use `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/Fixtures/HelpdeskWebApplicationFactory.cs` to drive the app startup path and assert the startup log or captured hook records a single migration and seeding execution.

Expected assertion shape:

```csharp
migrationStartCount.Should().Be(1);
```

The test must verify one execution, not just that startup completed.

- [ ] **Step 3: Defer the startup-task abstraction to a future phase**

Keep `ICrestStartupTask` and any generated startup-task executor out of the current implementation scope.

Future direction for the next plan:

```csharp
namespace CrestCreates.Modularity.Startup;

public interface ICrestStartupTask
{
    int Order { get; }

    Task ExecuteAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken);
}
```

This is a future platform capability, not part of the current stabilization patch.

- [ ] **Step 4: Confirm the maintained startup entry remains app-local**

`samples/SaaSHelpdesk/SaaSHelpdesk.Web/Program.cs` should call the generated app-local initializer, and the framework Web module should remain free of app startup side effects.

---

## Task 4: Preserve Module Diagnostics Without Duplicating The Existing Tests

**Files:**

- Review: `framework/test/CrestCreates.ModuleDiagnostics.Tests/HealthChecks/ModuleHealthCheckTests.cs`
- Review: `framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs`
- Review: `framework/src/CrestCreates.Web/CrestCreatesWebApplicationExtensions.cs`
- Review: generated module initializer output

- [ ] **Step 1: Reuse the existing module diagnostics coverage**

Run:

```powershell
dotnet test framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj -v:minimal -m:1
```

Expected:

```text
Passed!
```

Keep the existing module diagnostics tests as the baseline. Add stronger assertions only when a gap is proven.

- [ ] **Step 2: Keep the Web preset assertion narrow and explicit**

In `framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs`, preserve or extend the check that `AddCrestWeb()` registers the `modules` health check:

```csharp
registrations.Should().Contain(registration => registration.Name == "modules");
```

Do not duplicate the full diagnostics behavior already covered in the module diagnostics test project.

- [ ] **Step 3: Verify the health response through the SaaSHelpdesk runtime path later**

Treat the runtime `/health` response as the end-to-end proof, not as a separate duplicate unit test in this task.

- [ ] **Step 4: Keep generated diagnostics recording intact**

If a follow-up change moves the diagnostics store to DI, preserve the current `modules` health signal and the generated initializer output shape while doing so.

---

## Task 5: Keep Tenant Resolution Skipping Explicit And Transitional

**Files:**

- Review: `framework/src/CrestCreates.MultiTenancy.Abstract/SkipTenantResolutionMetadata.cs`
- Review: `framework/src/CrestCreates.MultiTenancy/Middleware/MultiTenancyMiddleware.cs`
- Review: `framework/src/CrestCreates.HealthCheck.AspNetCore/HealthCheckAspNetCoreEndpointRouteBuilderExtensions.cs`
- Test: `framework/test/CrestCreates.Web.Tests/Middlewares/MultiTenancyMiddlewareTests.cs`
- Test: `framework/test/CrestCreates.Web.Tests/CrestWebPresetTests.cs`

- [ ] **Step 1: Verify `/health` carries tenant-skip metadata**

Expected mapping:

```csharp
app.MapHealthChecks("/health", options)
   .WithMetadata(new SkipTenantResolutionMetadata());
```

- [ ] **Step 2: Verify the middleware honors endpoint metadata**

Keep or add a test where the endpoint includes `SkipTenantResolutionMetadata`, and the tenant resolver throws if it is called.

Expected outcome:

```csharp
await middleware.InvokeAsync(httpContext);

nextCalled.Should().BeTrue();
```

The tenant resolver must not be invoked.

- [ ] **Step 3: Keep `/health` path fallback as transitional compatibility**

The `/health` path fallback stays transitional compatibility until middleware-order tests show endpoint metadata is always available before tenant middleware runs.

Removal condition:

- A test proves endpoint metadata is always visible first in every supported hosting pattern.
- The fallback can then be removed in a follow-up plan.

- [ ] **Step 4: Do not expand the skip surface in this plan**

Do not add wildcard skip rules for `/metrics`, `/openapi`, or authentication endpoints here.

---

## Task 6: Verify SaaSHelpdesk Against The Configured Database

**Files:**

- Runtime project: `samples/SaaSHelpdesk/SaaSHelpdesk.Web/SaaSHelpdesk.Web.csproj`
- Runtime configuration: `samples/SaaSHelpdesk/SaaSHelpdesk.Web/appsettings*.json`
- Automated fixture: `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/Fixtures/HelpdeskWebApplicationFactory.cs`

- [ ] **Step 1: Use the configured database for the runtime verification**

Use the database settings already defined by the SaaSHelpdesk configuration files and the test fixture. Do not hard-code a container name, image name, username, or password into the plan.

- [ ] **Step 2: Build the sample**

Run:

```powershell
dotnet build samples/SaaSHelpdesk/SaaSHelpdesk.Web/SaaSHelpdesk.Web.csproj -v:minimal
```

Expected:

```text
Build succeeded.
```

- [ ] **Step 3: Run the automated startup verification through the fixture**

Use `samples/SaaSHelpdesk/SaaSHelpdesk.Tests/Fixtures/HelpdeskWebApplicationFactory.cs` to start the app and request `/health` against the running host.

Expected response:

- HTTP status code 200.
- JSON root `status` is `Healthy`.
- `checks` contains `modules`.
- The response includes module counts and phase counts.

- [ ] **Step 4: Inspect startup logs**

Expected:

- `Host database migration and seeding started` appears once.
- No tenant warning is emitted for `/health`.

The automated fixture is the primary verification path. A manual `dotnet run` check remains a second confirmation step only when the fixture cannot exercise the configured database.

- [ ] **Step 5: Stop the sample host after verification**

Do not leave the SaaSHelpdesk host running after the check completes.

---

## Task 7: Run The Full Test Sweep

**Files:**

- `framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj`
- `framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj`
- `framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj`

- [ ] **Step 1: Run the focused tests first**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj -v:minimal --filter "FullyQualifiedName~CrestCreates.Web.Tests.Middlewares.MultiTenancyMiddlewareTests|FullyQualifiedName~CrestCreates.Web.Tests.CrestWebPresetTests" -m:1
dotnet test framework/test/CrestCreates.ModuleDiagnostics.Tests/CrestCreates.ModuleDiagnostics.Tests.csproj -v:minimal -m:1
dotnet test framework/test/CrestCreates.CodeGenerator.Tests/CrestCreates.CodeGenerator.Tests.csproj -v:minimal --filter "FullyQualifiedName~InitializeCrestApplicationAsync" -m:1
```

Expected:

```text
Passed!
```

- [ ] **Step 2: Run the full Web test project**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj -v:minimal -m:1
```

Expected:

```text
Passed!
```

If an unrelated Dynamic API isolation failure reappears, record it separately and keep it out of this startup-health fix.

- [ ] **Step 3: Re-run the isolated known-stable test only if the full run surfaces a mismatch**

Run:

```powershell
dotnet test framework/test/CrestCreates.Web.Tests/CrestCreates.Web.Tests.csproj -v:minimal --filter "FullyQualifiedName=CrestCreates.Web.Tests.DynamicApi.DynamicApiExtensionsTests.ControllerOnlyProvider_ShouldBuildRegistryAndMapEndpoints" -m:1
```

Expected:

```text
Passed!
```

Use this only as a diagnostic check if the full suite needs it.

---

## Task 8: Handoff And Self-Review

**Files:**

- Review all files changed by the implementation.

- [ ] **Step 1: Review the final diff**

Run:

```powershell
git diff -- framework/src/CrestCreates.Web framework/src/CrestCreates.HealthCheck.AspNetCore framework/src/CrestCreates.MultiTenancy framework/src/CrestCreates.MultiTenancy.Abstract framework/test/CrestCreates.Web.Tests framework/test/CrestCreates.ModuleDiagnostics.Tests framework/test/CrestCreates.CodeGenerator.Tests framework/tools/CrestCreates.CodeGenerator samples/SaaSHelpdesk/SaaSHelpdesk.Web samples/SaaSHelpdesk/SaaSHelpdesk.Tests
```

Expected:

- No unrelated formatting churn.
- No runtime reflection fallback added for startup or module initialization.
- No framework module executing application migration or seeding.
- No plan language that treats `InitializeCrestAsync()` as the maintained startup entry.

- [ ] **Step 2: Run the placeholder scan against this plan**

Run:

```powershell
@("T" + "BD", "TO" + "DO", "fill" + " in", "implement" + " later") | ForEach-Object { rg $_ docs/superpowers/plans/2026-06-07-web-startup-health-mainline.md -n }
```

Expected:

```text
No matches found
```

- [ ] **Step 3: Check type and name consistency**

Verify these names match everywhere in the plan:

- `InitializeCrestApplicationAsync`
- `InitializeModulesAsync`
- `AutoModuleRegistration.g.cs`
- `ModuleSourceGenerator.cs`
- `HelpdeskWebApplicationFactory.cs`
- `HostMigrationAndSeedRunner`

Expected:

- The app-local startup method is consistently named `InitializeCrestApplicationAsync`.
- The generator target is consistently named `AutoModuleRegistration.g.cs`.
- The generator source file is consistently named `ModuleSourceGenerator.cs`.

- [ ] **Step 4: Self-review spec coverage**

Check the plan against the paired spec and confirm each requirement lands in one of these tasks:

- Generated app-local initialization.
- `/health` mapping and module diagnostics.
- Tenant skip behavior.
- Single ownership of startup side effects.
- SaaSHelpdesk runtime verification.
- Full test sweep and handoff.

Known risk to record if it remains after implementation:

- The framework-level `InitializeCrestAsync()` remains available as a compatibility surface, but the plan treats it as deprecated and does not keep it as the maintained path.

---

## Acceptance Criteria

This plan is complete when:

1. The generator emits app-local `InitializeCrestApplicationAsync()` alongside generated module initialization.
2. `CrestCreates.Web` continues to map `/health` and register module diagnostics.
3. `HostMigrationAndSeedRunner` stays app-owned and runs once.
4. `/health` skips tenant resolution and remains compatible with the transitional path fallback until a later removal condition is proven.
5. The SaaSHelpdesk fixture verifies the end-to-end startup path against the configured database.
6. The Web, module diagnostics, and code generator tests pass with the new assertions.
7. The plan contains no placeholder markers or database-container placeholders.
