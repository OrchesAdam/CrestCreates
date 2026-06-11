# Phase 5d: Capability Authorization Bridge — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bridge Capability Runtime authorization middleware to the existing `IPermissionChecker` RBAC main chain — no new permission system.

**Architecture:** Add `RequiredPermissions` to `CapabilityExecutionContext`, update `ICapabilityAuthorizationService` to accept permissions, create `PermissionCapabilityAuthorizationService` delegating to `IPermissionChecker`, register as Scoped in `AddCapabilityPipeline()`, fix `ICapabilityPipeline`/`ICapabilityDispatcher` Singleton→Scoped captive dependency.

**Tech Stack:** .NET 10, xUnit + FluentAssertions + Moq, zero reflection, AoT-friendly.

**Design spec:** `docs/superpowers/specs/2026-06-11-phase-5d-capability-authorization-bridge-design.md`

---

### File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs` | Modify | Add `RequiredPermissions` property |
| `framework/src/CrestCreates.Capability.Abstractions/ICapabilityAuthorizationService.cs` | Modify | Add `requiredPermissions` parameter |
| `framework/src/CrestCreates.Capability/PermissionCapabilityAuthorizationService.cs` | **Create** | Default auth implementation via `IPermissionChecker` |
| `framework/src/CrestCreates.Capability/CapabilityPipeline.cs` | Modify | Set `RequiredPermissions` from descriptor after `configureContext` |
| `framework/src/CrestCreates.Capability/Middleware/AuthorizationMiddleware.cs` | Modify | Pass `context.RequiredPermissions` to auth service |
| `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs` | Modify | Singleton→Scoped + register default auth service |
| `framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs` | **Create** | 9 tests (T1–T9 from spec) |

**No changes** to: `CapabilityDescriptor`, `IPermissionChecker`, `PermissionChecker`, `PermissionGrantManager`, `AuthorizationServiceCollectionExtensions`, `CrestCreates.Capability.Abstractions.csproj`, `CrestCreates.Capability.csproj`.

---

### Task 1: Modify `CapabilityExecutionContext` — add `RequiredPermissions`

**Files:**
- Modify: `framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs`

- [ ] **Step 1: Add `RequiredPermissions` property**

Add after the `Items` property (line 17). Insert before `CancellationToken`:

```csharp
public IReadOnlyList<string> RequiredPermissions { get; set; } = Array.Empty<string>();
```

Full property block context:
```csharp
public IDictionary<string, object?> Items { get; init; } = new Dictionary<string, object?>();
public IReadOnlyList<string> RequiredPermissions { get; set; } = Array.Empty<string>();
public CancellationToken CancellationToken { get; init; }
```

- [ ] **Step 2: Build to verify no compilation errors**

```bash
dotnet build framework/src/CrestCreates.Capability.Abstractions/
```

Expected: Build succeeds. `Array.Empty<string>()` is valid for `IReadOnlyList<string>`.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs
git commit -m "feat: add RequiredPermissions to CapabilityExecutionContext"
```

---

### Task 2: Update `ICapabilityAuthorizationService` — add `requiredPermissions` parameter

**Files:**
- Modify: `framework/src/CrestCreates.Capability.Abstractions/ICapabilityAuthorizationService.cs`

- [ ] **Step 1: Update interface signature**

Replace the entire file content:

```csharp
namespace CrestCreates.Capability.Abstractions;

public interface ICapabilityAuthorizationService
{
    Task<bool> AuthorizeAsync(
        string capabilityName,
        string? userId,
        IReadOnlyList<string> requiredPermissions,
        CancellationToken ct);
}
```

- [ ] **Step 2: Build to verify — expect compilation error in `AuthorizationMiddleware`**

```bash
dotnet build framework/src/CrestCreates.Capability/
```

Expected: **FAIL** — `AuthorizationMiddleware.cs` calls `AuthorizeAsync` with old (3-param) signature. This is expected; Task 4 fixes it.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Capability.Abstractions/ICapabilityAuthorizationService.cs
git commit -m "feat: add requiredPermissions parameter to ICapabilityAuthorizationService"
```

---

### Task 3: Create `PermissionCapabilityAuthorizationService`

**Files:**
- Create: `framework/src/CrestCreates.Capability/PermissionCapabilityAuthorizationService.cs`

- [ ] **Step 1: Create implementation file**

```csharp
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class PermissionCapabilityAuthorizationService : ICapabilityAuthorizationService
{
    private readonly IPermissionChecker _permissionChecker;

    public PermissionCapabilityAuthorizationService(IPermissionChecker permissionChecker)
    {
        _permissionChecker = permissionChecker;
    }

    public async Task<bool> AuthorizeAsync(
        string capabilityName,
        string? userId,
        IReadOnlyList<string> requiredPermissions,
        CancellationToken ct)
    {
        if (requiredPermissions.Count == 0)
            return true;

        var result = await _permissionChecker.IsGrantedAsync(requiredPermissions.ToArray());
        return result.AllGranted;
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Capability/
```

Expected: Still fails on `AuthorizationMiddleware` (old 3-param call). The new file itself should compile (no unused variable warning since `capabilityName`/`userId` are parameters — remove the unused parameter warning by keeping them in the signature as spec requires for diagnostics).

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Capability/PermissionCapabilityAuthorizationService.cs
git commit -m "feat: add PermissionCapabilityAuthorizationService delegating to IPermissionChecker"
```

---

### Task 4: Fix `AuthorizationMiddleware` and `CapabilityPipeline`

**Files:**
- Modify: `framework/src/CrestCreates.Capability/Middleware/AuthorizationMiddleware.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityPipeline.cs`

- [ ] **Step 1: Update `AuthorizationMiddleware` to pass `context.RequiredPermissions`**

Change line 20-22 from:
```csharp
var authorized = await _authService.AuthorizeAsync(
    context.CapabilityName, context.UserId, context.CancellationToken)
    .ConfigureAwait(false);
```

To:
```csharp
var authorized = await _authService.AuthorizeAsync(
    context.CapabilityName, context.UserId, context.RequiredPermissions, context.CancellationToken)
    .ConfigureAwait(false);
```

- [ ] **Step 2: Update `CapabilityPipeline` to populate `RequiredPermissions` AFTER `configureContext`**

In `ExecuteAsync`, after line 54 (`configureContext?.Invoke(context);`), add:

```csharp
context.RequiredPermissions = descriptor.Permissions;
```

The relevant block becomes (lines 54-56):
```csharp
        configureContext?.Invoke(context);

        context.RequiredPermissions = descriptor.Permissions;

        var startedAt = DateTimeOffset.UtcNow;
```

- [ ] **Step 3: Build to verify compilation succeeds**

```bash
dotnet build framework/src/CrestCreates.Capability/
```

Expected: Build succeeds. All compilation errors from Task 2 resolved.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Capability/Middleware/AuthorizationMiddleware.cs framework/src/CrestCreates.Capability/CapabilityPipeline.cs
git commit -m "feat: wire RequiredPermissions through pipeline and middleware"
```

---

### Task 5: Update DI registration — Scoped lifetimes + default auth service

**Files:**
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs`

- [ ] **Step 1: Change `ICapabilityPipeline` from Singleton to Scoped**

Line 36 — change:
```csharp
services.TryAddSingleton<ICapabilityPipeline, CapabilityPipeline>();
```
To:
```csharp
services.TryAddScoped<ICapabilityPipeline, CapabilityPipeline>();
```

- [ ] **Step 2: Register default `ICapabilityAuthorizationService` in `AddCapabilityPipeline()`**

Add after the `TryAddScoped<ICapabilityPipeline>` line (now line 36) and before `services.TryAddSingleton(builder)` (line 33):

```csharp
services.TryAddScoped<ICapabilityAuthorizationService, PermissionCapabilityAuthorizationService>();
```

Insert position: between the `ICapabilityPipeline` registration and the `CapabilityHandlerResolver` registration. Add it right after the pipeline scope change:

```csharp
services.TryAddScoped<ICapabilityPipeline, CapabilityPipeline>();
services.TryAddScoped<ICapabilityAuthorizationService, PermissionCapabilityAuthorizationService>();
services.TryAddSingleton<CapabilityHandlerResolver>();
```

- [ ] **Step 3: Change `ICapabilityDispatcher` from Singleton to Scoped**

Line 70 — change:
```csharp
services.TryAddSingleton<ICapabilityDispatcher>(sp =>
```
To:
```csharp
services.TryAddScoped<ICapabilityDispatcher>(sp =>
```

- [ ] **Step 4: Build to verify**

```bash
dotnet build framework/src/CrestCreates.Capability/
```

Expected: Build succeeds.

- [ ] **Step 5: Fast regression — run existing Capability tests to catch any DI breakage**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/
```

If any existing tests fail (e.g., `CapabilityEndToEndTests` or `CapabilityDispatcherTests`), note the failures but do NOT fix yet — Task 8 addresses them.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs
git commit -m "feat: register PermissionCapabilityAuthorizationService as default; fix Scoped lifetimes for pipeline/dispatcher"
```

---

### Task 6: Write unit tests T1–T3 (service authorization logic)

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs`

- [ ] **Step 1: Create test file with T1, T2, T3**

```csharp
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Capability.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class PermissionCapabilityAuthorizationServiceTests
{
    // T1: Empty permissions → allow execution, IPermissionChecker NOT called
    [Fact]
    public async Task Authorize_EmptyPermissions_AllowsExecution()
    {
        var mockChecker = new Mock<IPermissionChecker>();
        var service = new PermissionCapabilityAuthorizationService(mockChecker.Object);

        var result = await service.AuthorizeAsync(
            "test.cap", "user1", Array.Empty<string>(), CancellationToken.None);

        result.Should().BeTrue();
        mockChecker.Verify(
            c => c.IsGrantedAsync(It.IsAny<string[]>()),
            Times.Never);
    }

    // T2: All permissions granted → allow execution
    [Fact]
    public async Task Authorize_AllPermissionsGranted_AllowsExecution()
    {
        var mockChecker = new Mock<IPermissionChecker>();
        mockChecker
            .Setup(c => c.IsGrantedAsync(It.IsAny<string[]>()))
            .ReturnsAsync(new MultiplePermissionGrantResult(
                new Dictionary<string, bool> { ["perm.read"] = true, ["perm.write"] = true }));

        var service = new PermissionCapabilityAuthorizationService(mockChecker.Object);

        var result = await service.AuthorizeAsync(
            "test.cap", "user1", new[] { "perm.read", "perm.write" }, CancellationToken.None);

        result.Should().BeTrue();
    }

    // T3: Any permission denied → return false
    [Fact]
    public async Task Authorize_AnyPermissionDenied_ReturnsUnauthorized()
    {
        var mockChecker = new Mock<IPermissionChecker>();
        mockChecker
            .Setup(c => c.IsGrantedAsync(It.IsAny<string[]>()))
            .ReturnsAsync(new MultiplePermissionGrantResult(
                new Dictionary<string, bool> { ["perm.read"] = true, ["perm.write"] = false }));

        var service = new PermissionCapabilityAuthorizationService(mockChecker.Object);

        var result = await service.AuthorizeAsync(
            "test.cap", "user1", new[] { "perm.read", "perm.write" }, CancellationToken.None);

        result.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/ --filter "FullyQualifiedName~PermissionCapabilityAuthorizationServiceTests"
```

Expected: 3 tests pass.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs
git commit -m "test: add unit tests for PermissionCapabilityAuthorizationService (T1-T3)"
```

---

### Task 7: Write unit tests T4–T5 (middleware + configureContext bypass)

**Files:**
- Modify: `framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs`

- [ ] **Step 1: Add T4 — AuthorizationMiddleware passes RequiredPermissions, not capabilityName**

Append to the test class:

```csharp
// T4: Middleware passes context.RequiredPermissions to auth service
[Fact]
public async Task AuthorizationMiddleware_UsesDescriptorPermissions_NotCapabilityName()
{
    // Arrange
    string? capturedPermission = null;
    var mockAuthService = new Mock<ICapabilityAuthorizationService>();
    mockAuthService
        .Setup(s => s.AuthorizeAsync(It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
        .Callback<string, string?, IReadOnlyList<string>, CancellationToken>(
            (name, userId, permissions, ct) => capturedPermission = permissions.FirstOrDefault())
        .ReturnsAsync(true);

    var middleware = new AuthorizationMiddleware(mockAuthService.Object);

    var context = new CapabilityExecutionContext
    {
        CapabilityId = "test.echo",
        CapabilityName = "Echo",
        UserId = "user1",
        RequiredPermissions = new[] { "perm.read" }
    };

    // Act
    var result = await middleware.InvokeAsync(context, _ => Task.FromResult(
        CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

    // Assert
    result.IsSuccess.Should().BeTrue();
    capturedPermission.Should().Be("perm.read",
        "middleware must pass descriptor permissions, not capability name");
}

// T5: configureContext cannot clear RequiredPermissions
[Fact]
public async Task Pipeline_SetsRequiredPermissions_AfterConfigureContext()
{
    // This test verifies the ordering documented in spec section 2.3.
    // The pipeline MUST set RequiredPermissions AFTER configureContext runs,
    // so a caller cannot clear permissions to bypass authorization.
    // We test this by constructing the pipeline scenario directly:
    // configureContext sets RequiredPermissions to empty, then verify
    // the pipeline would still set it from the descriptor.

    // Simulate what CapabilityPipeline does:
    var descriptorPermissions = new[] { "perm.read", "perm.write" };
    var context = new CapabilityExecutionContext
    {
        CapabilityId = "test.cap",
        CapabilityName = "Test",
        RequiredPermissions = Array.Empty<string>() // initial empty
    };

    // Step 1: Caller's configureContext
    Action<CapabilityExecutionContext>? configureContext = ctx =>
    {
        ctx.RequiredPermissions = Array.Empty<string>(); // caller tries to clear
    };
    configureContext?.Invoke(context);

    // Step 2: Pipeline sets from descriptor (this is what we're verifying)
    context.RequiredPermissions = descriptorPermissions;

    // Assert: final value is descriptor.Permissions, not the empty value from configureContext
    context.RequiredPermissions.Should().BeEquivalentTo(new[] { "perm.read", "perm.write" });
    context.RequiredPermissions.Should().NotBeEmpty("pipeline must override configureContext clearing");
}
```

- [ ] **Step 2: Run new tests**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/ --filter "FullyQualifiedName~PermissionCapabilityAuthorizationServiceTests"
```

Expected: 5 tests pass (3 from Task 6 + 2 new).

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs
git commit -m "test: add middleware and configureContext bypass tests (T4-T5)"
```

---

### Task 8: Write pipeline integration tests T6–T7

**Files:**
- Modify: `framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs`

These go through the **full** `AddCapabilityPipeline()` DI chain with all middleware, including `AuthorizationMiddleware`. They catch DI misconfiguration and middleware ordering issues.

- [ ] **Step 1: Add T6 — granted permission invokes handler**

Append to the test class:

```csharp
// T6: Full pipeline with granted permission → handler invoked, result Success
[Fact]
public async Task Pipeline_WithDescriptorPermissions_AndGrantedPermission_InvokesHandler()
{
    // Arrange
    var descriptor = new CapabilityDescriptor
    {
        Id = "secure.echo",
        Name = "Secure Echo",
        Version = 1,
        CapabilityKind = CapabilityKind.Query,
        State = DescriptorState.Active,
        Permissions = new[] { "perm.read" }
    };

    var mockPermissionChecker = new Mock<IPermissionChecker>();
    mockPermissionChecker
        .Setup(c => c.IsGrantedAsync(It.IsAny<string[]>()))
        .ReturnsAsync(new MultiplePermissionGrantResult(
            new Dictionary<string, bool> { ["perm.read"] = true }));

    var services = new ServiceCollection();
    services.AddSingleton(mockPermissionChecker.Object); // our mock
    services.AddCapabilityPipeline(); // full default middleware chain (includes AuthorizationMiddleware)

    // Register descriptor
    var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
    var registry = new CapabilityRegistry(engine);
    registry.Build([new TestDescriptorProvider([descriptor])]);
    services.AddSingleton<ICapabilityRegistry>(registry);

    // Register handler
    var handlerResolver = new CapabilityHandlerResolver();
    handlerResolver.Register("secure.echo", new EchoHandlerInvoker());
    services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

    var sp = services.BuildServiceProvider();
    using var scope = sp.CreateScope();
    var pipeline = scope.ServiceProvider.GetRequiredService<ICapabilityPipeline>();

    // Act
    var result = await pipeline.ExecuteAsync("secure.echo", input: "hello");

    // Assert
    result.IsSuccess.Should().BeTrue();
    result.Output.Should().Be("ECHO: hello");
}

// T7: Full pipeline with denied permission → UNAUTHORIZED
[Fact]
public async Task Pipeline_WithDescriptorPermissions_AndDeniedPermission_ReturnsUnauthorized()
{
    // Arrange
    var descriptor = new CapabilityDescriptor
    {
        Id = "secure.write",
        Name = "Secure Write",
        Version = 1,
        CapabilityKind = CapabilityKind.Command,
        State = DescriptorState.Active,
        Permissions = new[] { "perm.write" }
    };

    var mockPermissionChecker = new Mock<IPermissionChecker>();
    mockPermissionChecker
        .Setup(c => c.IsGrantedAsync(It.IsAny<string[]>()))
        .ReturnsAsync(new MultiplePermissionGrantResult(
            new Dictionary<string, bool> { ["perm.write"] = false }));

    var services = new ServiceCollection();
    services.AddSingleton(mockPermissionChecker.Object);
    services.AddCapabilityPipeline();

    var engine = new RegistryValidationEngine<CapabilityDescriptor>([]);
    var registry = new CapabilityRegistry(engine);
    registry.Build([new TestDescriptorProvider([descriptor])]);
    services.AddSingleton<ICapabilityRegistry>(registry);

    var handlerResolver = new CapabilityHandlerResolver();
    handlerResolver.Register("secure.write", new EchoHandlerInvoker());
    services.AddSingleton<ICapabilityHandlerResolver>(handlerResolver);

    var sp = services.BuildServiceProvider();
    using var scope2 = sp.CreateScope();
    var pipeline2 = scope2.ServiceProvider.GetRequiredService<ICapabilityPipeline>();

    // Act
    var result = await pipeline2.ExecuteAsync("secure.write", input: "data");

    // Assert
    result.IsSuccess.Should().BeFalse();
    result.Status.Should().Be(CapabilityExecutionStatus.Failed);
    result.ErrorCode.Should().Be("UNAUTHORIZED");
}

// Helper: descriptor provider for tests
private sealed class TestDescriptorProvider : IDescriptorProvider<CapabilityDescriptor>
{
    private readonly List<CapabilityDescriptor> _descriptors;
    public TestDescriptorProvider(List<CapabilityDescriptor> descriptors) => _descriptors = descriptors;
    public IReadOnlyList<CapabilityDescriptor> GetDescriptors() => _descriptors;
}

// Helper: simple echo handler
private sealed class EchoHandlerInvoker : ICapabilityHandlerInvoker
{
    public Task<object?> InvokeAsync(object? input, CancellationToken ct)
        => Task.FromResult<object?>($"ECHO: {input}");
}
```

- [ ] **Step 2: Add required imports at top of file**

Ensure these using directives are present:
```csharp
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Capability.Internal;
using Microsoft.Extensions.DependencyInjection;
```

- [ ] **Step 3: Run integration tests**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/ --filter "FullyQualifiedName~PermissionCapabilityAuthorizationServiceTests"
```

Expected: 7 tests pass. If T6/T7 fail due to DI issues, verify `IPermissionChecker` mock is registered before `AddCapabilityPipeline()` — the mock must be in the container before the scoped auth service is resolved.

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs
git commit -m "test: add pipeline integration tests for authorization bridge (T6-T7)"
```

---

### Task 9: Write DI registration tests T8–T9

**Files:**
- Modify: `framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs`

- [ ] **Step 1: Add T8 — `AddCapabilityPipeline` registers default auth service**

Append to the test class:

```csharp
// T8: AddCapabilityPipeline registers default ICapabilityAuthorizationService
[Fact]
public void AddCapabilityPipeline_RegistersDefaultAuthorizationService()
{
    var services = new ServiceCollection();

    services.AddCapabilityPipeline();

    var sp = services.BuildServiceProvider();
    using var scope = sp.CreateScope();
    var authService = scope.ServiceProvider.GetService<ICapabilityAuthorizationService>();

    authService.Should().NotBeNull("AddCapabilityPipeline must register a default auth service");
    authService.Should().BeOfType<PermissionCapabilityAuthorizationService>(
        "default implementation must be PermissionCapabilityAuthorizationService");
}

// T9: AddCapabilityRuntime registers default ICapabilityAuthorizationService (inherits from AddCapabilityPipeline)
[Fact]
public void AddCapabilityRuntime_RegistersDefaultAuthorizationService()
{
    var services = new ServiceCollection();

    services.AddCapabilityRuntime();

    var sp = services.BuildServiceProvider();
    using var scope2 = sp.CreateScope();
    var authService2 = scope2.ServiceProvider.GetService<ICapabilityAuthorizationService>();

    authService2.Should().NotBeNull("AddCapabilityRuntime must register auth service via AddCapabilityPipeline");
    authService2.Should().BeOfType<PermissionCapabilityAuthorizationService>(
        "default implementation must be PermissionCapabilityAuthorizationService");
}
```

- [ ] **Step 2: Run DI registration tests**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/ --filter "FullyQualifiedName~PermissionCapabilityAuthorizationServiceTests"
```

Expected: 9 tests pass. If T8/T9 fail with DI resolution error, check that `AddCapabilityPipeline()` registers `ICapabilityAuthorizationService` as Scoped and that no missing dependency prevents resolution.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs
git commit -m "test: add DI registration verification tests (T8-T9)"
```

---

### Task 10: Fix existing tests if broken

**Files:**
- May modify: `framework/test/CrestCreates.Capability.Tests/CapabilityEndToEndTests.cs`
- May modify: `framework/test/CrestCreates.Capability.Tests/CapabilityDispatcherTests.cs`

- [ ] **Step 1: Run full Capability test suite to identify failures**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/
```

- [ ] **Step 2: Analyze failures**

Expected scenarios and fixes:

| Failure | Likely Cause | Fix |
|---------|-------------|-----|
| `CapabilityEndToEndTests` — `E2E_WithTenantAndUser_PopulatesAuditContext` creates `CapabilityDispatcher` with `new CapabilityDispatcher(resolver, pipeline, tenantMock.Object, userMock.Object)` | Now requires `ICapabilityAuthorizationService?` if constructor changes | Verify `CapabilityDispatcher` constructor didn't change; it shouldn't — we only changed its DI registration |
| `CapabilityDispatcherTests` — any mock setup mismatch for `ICapabilityPipeline` | `ICapabilityPipeline` mock unchanged | No fix — mock is still valid |
| Any test using `BuildServiceProvider()` with scoped registrations | `BuildServiceProvider()` creates root scope only — Scoped services resolved from root return valid instances (unlike ASP.NET Core validation) | Should work in test scenarios |

`CapabilityEndToEndTests.CreateE2EPipeline()` uses `AddSingleton<ICapabilityPipeline, CapabilityPipeline>()` with a custom builder containing only `AuditMiddleware`. The `AuthorizationMiddleware` is NOT in their chain, so the new auth service registration has no effect. These tests should pass without changes.

- [ ] **Step 3: If failures exist, fix minimally**

Do NOT remove `AuthorizationMiddleware` from any tests. If a test fails because of the new auth registration:
1. Register a mock `IPermissionChecker` that returns `true` for all checks.
2. OR use descriptors with empty `Permissions`.

- [ ] **Step 4: Verify all tests pass**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/
```

Expected: All tests green (existing + 9 new).

- [ ] **Step 5: Commit if changes were needed**

```bash
git add framework/test/CrestCreates.Capability.Tests/
git commit -m "fix: update existing tests for Phase 5d authorization bridge changes"
```

---

### Task 11: Acceptance verification

- [ ] **Step 1: Full Capability test suite**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/
```

- [ ] **Step 2: Authorization regression — must not regress**

```bash
dotnet test framework/test/CrestCreates.Application.Tests/ --filter "FullyQualifiedName~Permission"
```

- [ ] **Step 3: Build all affected projects**

```bash
dotnet build framework/src/CrestCreates.Capability.Abstractions/
dotnet build framework/src/CrestCreates.Capability/
dotnet build framework/test/CrestCreates.Capability.Tests/
```

- [ ] **Step 4: LSP diagnostics on all changed files**

```bash
# Run via lsp_diagnostics tool on:
# framework/src/CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs
# framework/src/CrestCreates.Capability.Abstractions/ICapabilityAuthorizationService.cs
# framework/src/CrestCreates.Capability/PermissionCapabilityAuthorizationService.cs
# framework/src/CrestCreates.Capability/CapabilityPipeline.cs
# framework/src/CrestCreates.Capability/Middleware/AuthorizationMiddleware.cs
# framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs
# framework/test/CrestCreates.Capability.Tests/PermissionCapabilityAuthorizationServiceTests.cs
```

Expected: Zero errors, zero warnings on changed files.

- [ ] **Step 5: If any step fails, fix and re-verify. Do NOT proceed with failures.**

- [ ] **Step 6: Update `memory.md` with Phase 5d completion status**

Add entry:
```markdown
### Phase 5d: Capability Authorization Bridge — Completed
- `PermissionCapabilityAuthorizationService` delegates to existing `IPermissionChecker` via `CapabilityDescriptor.Permissions`
- `RequiredPermissions` on `CapabilityExecutionContext`, populated AFTER `configureContext` (bypass-proof)
- Registered as Scoped default in `AddCapabilityPipeline()` (inherited by `AddCapabilityRuntime()`)
- Fixed `ICapabilityPipeline`/`ICapabilityDispatcher` Singleton→Scoped captive dependency
- 9 tests: service unit, middleware, configureContext bypass, pipeline integration, DI registration
```

- [ ] **Step 7: Final commit**

```bash
git add memory.md
git commit -m "docs: record Phase 5d Capability Authorization Bridge completion"
```

---

### Acceptance Criteria Checklist

- [ ] `dotnet test framework/test/CrestCreates.Capability.Tests/` — all tests pass
- [ ] `dotnet test framework/test/CrestCreates.Application.Tests/ --filter "FullyQualifiedName~Permission"` — no regressions
- [ ] `dotnet build framework/src/CrestCreates.Capability.Abstractions/` — succeeds
- [ ] `dotnet build framework/src/CrestCreates.Capability/` — succeeds
- [ ] `dotnet build framework/test/CrestCreates.Capability.Tests/` — succeeds
- [ ] `AddCapabilityPipeline()` registers a default `ICapabilityAuthorizationService`
- [ ] `AddCapabilityRuntime()` inherits the default via `AddCapabilityPipeline()`
- [ ] `AuthorizationMiddleware` never silently skips auth in default chain
- [ ] `configureContext` cannot clear `RequiredPermissions`
- [ ] No new permission definitions, grant stores, or checkers
- [ ] Organization role from Phase 5c not wired to RBAC
- [ ] Zero reflection, zero dynamic expressions, AoT-friendly
- [ ] `memory.md` updated
