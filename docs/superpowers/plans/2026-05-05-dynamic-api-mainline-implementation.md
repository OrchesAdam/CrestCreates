# Dynamic API Mainline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove Dynamic API runtime fallback and make compile-time generated providers/endpoints the only official path.

**Architecture:** Dynamic API startup continues to resolve `DynamicApiRegistry` through `DynamicApiGeneratedRegistryStore` and maps endpoints through generated providers. This change deletes the leftover runtime fallback option/API, removes fallback guidance from diagnostics, and updates tests so they only prove generated-mainline behavior.

**Tech Stack:** .NET 10 preview, C#, xUnit, FluentAssertions, ASP.NET Core endpoint routing, Roslyn source generator tests.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `framework/src/CrestCreates.DynamicApi/DynamicApiOptions.cs` | Modify | Keep only generated-mainline options: service assemblies and route prefix |
| `framework/src/CrestCreates.DynamicApi/DynamicApiGeneratedRegistryStore.cs` | Modify | Fail fast when generated providers are missing, with diagnostics that do not mention fallback |
| `framework/test/CrestCreates.Web.Tests/DynamicApi/DynamicApiExtensionsTests.cs` | Modify | Remove fallback-positive test and add tests proving fallback API is gone and missing-provider diagnostics stay generated-only |
| `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs` | Verify only | Existing tests already prove registry and endpoint generation |
| `framework/test/CrestCreates.IntegrationTests/IntegrationTests.cs` | Verify only | Existing Dynamic API CRUD/Swagger tests are the generated-mainline acceptance layer |

Do not modify runtime scanner/executor code unless an actual active implementation is discovered. Prior scans did not find `DynamicApiScanner` or `DynamicApiEndpointExecutor`.

---

### Task 1: Add Failing Web Tests For Runtime Fallback Removal

**Files:**
- Modify: `framework/test/CrestCreates.Web.Tests/DynamicApi/DynamicApiExtensionsTests.cs`

- [ ] **Step 1: Replace the fallback-positive test with removal tests**

Edit `framework/test/CrestCreates.Web.Tests/DynamicApi/DynamicApiExtensionsTests.cs`.

Remove the whole test method:

```csharp
[Fact]
public void AddCrestDynamicApi_WithRuntimeFallbackOptIn_CanResolveEmptyRegistryForLegacyDiagnostics()
{
    var services = new ServiceCollection();

    services.AddCrestDynamicApi(options =>
    {
        options.UseRuntimeReflectionFallback();
        options.AddApplicationServiceAssembly(typeof(string).Assembly);
    });

    using var serviceProvider = services.BuildServiceProvider();
    var registry = serviceProvider.GetRequiredService<DynamicApiRegistry>();

    registry.Services.Should().BeEmpty();
}
```

Add these two tests in the same class, after `AddCrestDynamicApi_WithoutGeneratedProvider_ThrowsWhenResolvingRegistry`:

```csharp
[Fact]
public void DynamicApiOptions_ShouldNotExposeRuntimeFallbackMembers()
{
    typeof(DynamicApiOptions).GetProperty("EnableRuntimeReflectionFallback").Should().BeNull();
    typeof(DynamicApiOptions).GetMethod("UseRuntimeReflectionFallback").Should().BeNull();
}

[Fact]
public void AddCrestDynamicApi_WithoutGeneratedProvider_ErrorShouldNotMentionRuntimeFallback()
{
    var services = new ServiceCollection();

    services.AddCrestDynamicApi(options => options.AddApplicationServiceAssembly(typeof(string).Assembly));

    using var serviceProvider = services.BuildServiceProvider();

    var action = () => serviceProvider.GetRequiredService<DynamicApiRegistry>();

    action.Should().Throw<InvalidOperationException>()
        .WithMessage("*编译期生成的 provider*")
        .And.Message.Should().NotContain("RuntimeReflectionFallback")
        .And.NotContain("UseRuntimeReflectionFallback")
        .And.NotContain("fallback");
}
```

- [ ] **Step 2: Run the focused Web tests and verify they fail**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --filter "FullyQualifiedName~DynamicApiExtensionsTests"
```

Expected result:

```text
Failed DynamicApiOptions_ShouldNotExposeRuntimeFallbackMembers
```

The failure should show that `EnableRuntimeReflectionFallback` and/or `UseRuntimeReflectionFallback` still exist. The diagnostic-message test should also fail until the missing-provider message stops mentioning fallback.

- [ ] **Step 3: Commit the failing tests**

```powershell
git add framework\test\CrestCreates.Web.Tests\DynamicApi\DynamicApiExtensionsTests.cs
git commit -m "test: require dynamic api generated mainline only"
```

---

### Task 2: Delete Runtime Fallback From DynamicApiOptions

**Files:**
- Modify: `framework/src/CrestCreates.DynamicApi/DynamicApiOptions.cs`

- [ ] **Step 1: Remove fallback members**

Replace the entire content of `framework/src/CrestCreates.DynamicApi/DynamicApiOptions.cs` with:

```csharp
using System.Reflection;

namespace CrestCreates.DynamicApi;

public sealed class DynamicApiOptions
{
    private readonly HashSet<Assembly> _serviceAssemblies = new();

    public IReadOnlyCollection<Assembly> ServiceAssemblies => _serviceAssemblies;

    public string DefaultRoutePrefix { get; set; } = "api";

    public void AddApplicationServiceAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        _serviceAssemblies.Add(assembly);
    }

    public void AddApplicationServiceAssembly<TMarker>()
    {
        AddApplicationServiceAssembly(typeof(TMarker).Assembly);
    }
}
```

This removes:

```csharp
using System.ComponentModel;
public bool EnableRuntimeReflectionFallback { get; private set; }
public void UseRuntimeReflectionFallback()
```

- [ ] **Step 2: Run the focused Web tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --filter "FullyQualifiedName~DynamicApiExtensionsTests"
```

Expected result after this task:

```text
Failed AddCrestDynamicApi_WithoutGeneratedProvider_ErrorShouldNotMentionRuntimeFallback
```

The reflection test should pass. The diagnostic-message test should still fail because `DynamicApiGeneratedRegistryStore` still references `UseRuntimeReflectionFallback`.

- [ ] **Step 3: Commit DynamicApiOptions cleanup**

```powershell
git add framework\src\CrestCreates.DynamicApi\DynamicApiOptions.cs
git commit -m "fix: remove dynamic api runtime fallback option"
```

---

### Task 3: Remove Fallback From Missing-Provider Diagnostics

**Files:**
- Modify: `framework/src/CrestCreates.DynamicApi/DynamicApiGeneratedRegistryStore.cs`

- [ ] **Step 1: Update missing-provider error message**

In `CreateMissingGeneratedProviderException`, replace the current return statement:

```csharp
return new InvalidOperationException(
    $"Dynamic API 未找到编译期生成的 provider，当前主链只支持生成链。ServiceAssemblies: {assemblies}。如需临时诊断，可显式启用 {nameof(DynamicApiOptions.UseRuntimeReflectionFallback)}。");
```

with:

```csharp
return new InvalidOperationException(
    $"Dynamic API 未找到编译期生成的 provider，当前主链只支持生成链。ServiceAssemblies: {assemblies}。请检查 CrestCreates.CodeGenerator 是否作为 analyzer 引用、应用服务程序集是否被当前项目引用、服务是否符合生成规则，以及 GeneratedDynamicApiRegistry.g.cs / GeneratedDynamicApiEndpoints.g.cs 是否生成并参与编译。");
```

- [ ] **Step 2: Run focused Web tests and verify they pass**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --filter "FullyQualifiedName~DynamicApiExtensionsTests"
```

Expected result:

```text
Passed! - Failed: 0
```

- [ ] **Step 3: Search source and tests for removed fallback API references**

Run:

```powershell
rg -n "UseRuntimeReflectionFallback|EnableRuntimeReflectionFallback|RuntimeReflectionFallback" framework\src framework\test samples
```

Expected result:

```text
no matches
```

If there are matches in source/test/sample code, remove or update them. Do not remove mentions from already-approved specs or plans.

- [ ] **Step 4: Commit diagnostic cleanup**

```powershell
git add framework\src\CrestCreates.DynamicApi\DynamicApiGeneratedRegistryStore.cs framework\test\CrestCreates.Web.Tests\DynamicApi\DynamicApiExtensionsTests.cs
git commit -m "fix: remove dynamic api fallback diagnostics"
```

---

### Task 4: Verify Generated Mainline Tests

**Files:**
- Verify: `framework/test/CrestCreates.CodeGenerator.Tests/DynamicApiGenerator/DynamicApiAotSourceGeneratorTests.cs`
- Verify: `framework/test/CrestCreates.Web.Tests/DynamicApi/GeneratedDynamicApiRuntimeTests.cs`
- Verify: `framework/test/CrestCreates.Web.Tests/DynamicApi/DynamicApiExtensionsTests.cs`

- [ ] **Step 1: Run CodeGenerator Dynamic API tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DynamicApiAotSourceGeneratorTests"
```

Expected result:

```text
Passed! - Failed: 0
```

These tests must continue to prove:

```text
GeneratedDynamicApiRegistry.g.cs is produced
GeneratedDynamicApiEndpoints.g.cs is produced
Generated endpoints call DynamicApiGeneratedRuntime.EnsurePermissionAsync
Generated endpoints call DynamicApiGeneratedRuntime.ValidateAsync
Generated endpoints call DynamicApiGeneratedRuntime.ExecuteAsync
Generated registry includes route, HTTP method, permissions, and ignored-method behavior
```

- [ ] **Step 2: Run Web Dynamic API tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --filter "FullyQualifiedName~GeneratedDynamicApiRuntimeTests|FullyQualifiedName~DynamicApiExtensionsTests"
```

Expected result:

```text
Passed! - Failed: 0
```

These tests must continue to prove:

```text
Body JSON binds to DTOs
Denied permissions throw CrestPermissionException
Validation failures surface
Transactional UoW commits on success
Null GET results return 404 envelope
Missing generated providers fail fast
Fallback API is not exposed
```

- [ ] **Step 3: Commit only if verification required test adjustments**

If Task 4 required no code changes, do not commit.

If a test needed a small update caused by the fallback API deletion, commit only that update:

```powershell
git add framework\test\CrestCreates.CodeGenerator.Tests\DynamicApiGenerator\DynamicApiAotSourceGeneratorTests.cs framework\test\CrestCreates.Web.Tests\DynamicApi\GeneratedDynamicApiRuntimeTests.cs framework\test\CrestCreates.Web.Tests\DynamicApi\DynamicApiExtensionsTests.cs
git commit -m "test: align dynamic api generated mainline coverage"
```

---

### Task 5: Verify Integration Mainline

**Files:**
- Verify: `framework/test/CrestCreates.IntegrationTests/IntegrationTests.cs`
- Verify: `samples/LibraryManagement/LibraryManagement.Web/Program.cs`

- [ ] **Step 1: Run Dynamic API integration tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~DynamicApi_WithAuthorizedAdmin_CanCreateAndGetBook|FullyQualifiedName~DynamicApi_MainChain_CanUpdateQueryDeleteAndShowInSwagger|FullyQualifiedName~PermissionGrant_AfterGrantingBookSearch_UserCanAccessDynamicApi"
```

Expected result:

```text
Passed! - Failed: 0
```

These tests must prove:

```text
/api/book create and get work
/api/book update, query, delete, and deleted get behavior work
/swagger/v1/swagger.json contains /api/book and /api/book/{id}
permission grants affect generated Dynamic API endpoints
```

- [ ] **Step 2: Build sample app**

Run:

```powershell
dotnet build samples\LibraryManagement\LibraryManagement.Web\LibraryManagement.Web.csproj --no-restore
```

Expected result:

```text
Build succeeded.
```

This build catches any sample usage of removed fallback APIs and confirms `AddCrestAspNetCoreDynamicApi(...)` / `MapCrestAspNetCoreDynamicApi()` still compile.

- [ ] **Step 3: Commit only if integration/sample verification required fixes**

If no files changed, do not commit.

If sample or integration files changed, commit only those files:

```powershell
git add framework\test\CrestCreates.IntegrationTests\IntegrationTests.cs samples\LibraryManagement\LibraryManagement.Web\Program.cs
git commit -m "test: verify dynamic api generated integration path"
```

---

### Task 6: Final Source Audit And Full Verification

**Files:**
- Verify: full repository source/test references

- [ ] **Step 1: Confirm removed API has no source/test/sample references**

Run:

```powershell
rg -n "UseRuntimeReflectionFallback|EnableRuntimeReflectionFallback|RuntimeReflectionFallback" framework\src framework\test samples
```

Expected result:

```text
no matches
```

- [ ] **Step 2: Confirm no scanner/executor path was added**

Run:

```powershell
rg -n "DynamicApiScanner|DynamicApiEndpointExecutor|reflection fallback|runtime fallback" framework\src framework\test samples
```

Expected result:

```text
no matches
```

If a match appears only in an intentional test name or approved design/plan document, do not change source behavior to satisfy the search. Source/test/sample code should not contain active fallback paths.

- [ ] **Step 3: Run focused final test suite**

Run:

```powershell
dotnet test framework\test\CrestCreates.Web.Tests\CrestCreates.Web.Tests.csproj --filter "FullyQualifiedName~DynamicApiExtensionsTests|FullyQualifiedName~GeneratedDynamicApiRuntimeTests"
```

Expected result:

```text
Passed! - Failed: 0
```

Run:

```powershell
dotnet test framework\test\CrestCreates.CodeGenerator.Tests\CrestCreates.CodeGenerator.Tests.csproj --filter "FullyQualifiedName~DynamicApiAotSourceGeneratorTests"
```

Expected result:

```text
Passed! - Failed: 0
```

Run:

```powershell
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~DynamicApi_WithAuthorizedAdmin_CanCreateAndGetBook|FullyQualifiedName~DynamicApi_MainChain_CanUpdateQueryDeleteAndShowInSwagger|FullyQualifiedName~PermissionGrant_AfterGrantingBookSearch_UserCanAccessDynamicApi"
```

Expected result:

```text
Passed! - Failed: 0
```

- [ ] **Step 4: Build changed main projects**

Run:

```powershell
dotnet build framework\src\CrestCreates.DynamicApi\CrestCreates.DynamicApi.csproj --no-restore
```

Expected result:

```text
Build succeeded.
```

Run:

```powershell
dotnet build samples\LibraryManagement\LibraryManagement.Web\LibraryManagement.Web.csproj --no-restore
```

Expected result:

```text
Build succeeded.
```

- [ ] **Step 5: Inspect final diff**

Run:

```powershell
git diff --stat HEAD
git diff HEAD -- framework\src\CrestCreates.DynamicApi\DynamicApiOptions.cs framework\src\CrestCreates.DynamicApi\DynamicApiGeneratedRegistryStore.cs framework\test\CrestCreates.Web.Tests\DynamicApi\DynamicApiExtensionsTests.cs
```

Expected final code changes:

```text
DynamicApiOptions.cs removes fallback property/method and System.ComponentModel using
DynamicApiGeneratedRegistryStore.cs removes fallback suggestion from missing-provider error
DynamicApiExtensionsTests.cs removes fallback-positive test and adds generated-mainline removal assertions
```

- [ ] **Step 6: Final commit if needed**

If all changes are already committed by previous tasks, do not create an empty commit.

If there are remaining tracked changes from this feature, commit them:

```powershell
git add framework\src\CrestCreates.DynamicApi\DynamicApiOptions.cs framework\src\CrestCreates.DynamicApi\DynamicApiGeneratedRegistryStore.cs framework\test\CrestCreates.Web.Tests\DynamicApi\DynamicApiExtensionsTests.cs
git commit -m "fix: finalize dynamic api generated mainline"
```

---

## Review Notes For GPT Audit

When reviewing the implementation, check these first:

| Risk | What to inspect |
|---|---|
| Fallback API accidentally retained | `DynamicApiOptions` must not expose `UseRuntimeReflectionFallback` or `EnableRuntimeReflectionFallback` |
| Error message still suggests fallback | `DynamicApiGeneratedRegistryStore.CreateMissingGeneratedProviderException` must not mention fallback |
| Tests still bless legacy path | `DynamicApiExtensionsTests` must not contain a positive runtime fallback test |
| Runtime scanning reintroduced | Search source/test/sample for `DynamicApiScanner`, `DynamicApiEndpointExecutor`, and fallback phrases |
| Generated path weakened | CodeGenerator, Web runtime, and Integration Dynamic API tests must pass |

Do not ask the implementation worker to add compatibility shims. The approved design intentionally makes this a breaking cleanup.
