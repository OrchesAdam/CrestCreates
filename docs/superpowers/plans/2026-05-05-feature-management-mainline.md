# Feature Management Mainline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete Feature Management as a single generated/AoT-friendly mainline for definition validation, scoped values, cache invalidation, permissions, audit logging, tenant initialization, and integration tests.

**Architecture:** Keep the existing independent `FeatureValue` storage and `FeatureDefinition -> FeatureStore -> FeatureValueResolver -> FeatureProvider/Checker -> FeatureManager -> FeatureAppService` chain. Do not move feature flags into `SettingValue`, do not add user-level feature overrides, and do not strengthen runtime Dynamic API scanner/executor paths. Feature writes go through `FeatureManager`; generated Dynamic API calls go through app service contracts; feature changes enrich the existing AuditLogging context rather than creating a side-channel audit table.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions, Moq, EF Core, PostgreSQL integration tests, CrestCreates generated Dynamic API, CrestCreates cache and audit logging.

---

## Scope Guardrails

- Do not implement percentage rollout, rule engines, organization/user targeting, or remote config.
- Do not store Feature values in `SettingValue`.
- Do not add behavior to `DynamicApiScanner` or `DynamicApiEndpointExecutor`.
- Do not use `TenantName` as a key. Use `CurrentTenant.Id` or explicit `tenantId`.
- Do not let `FeatureChecker` replace permission checks.
- Do not write `FeatureRepository` directly from app services or tenant seeders.

## File Structure

Create or modify these files only as needed:

| File | Responsibility |
|------|----------------|
| `framework/src/CrestCreates.Domain.Shared/Features/FeatureManagementErrorCodes.cs` | Stable error code constants used by Feature Management. |
| `framework/src/CrestCreates.Domain/Features/FeatureDefinitionManager.cs` | Validate duplicate definitions and expose definitions. |
| `framework/src/CrestCreates.Application/Features/FeatureManagementExceptionFactory.cs` | Convert feature errors to `CrestBusinessException`. |
| `framework/src/CrestCreates.Application/Features/FeatureManagementPermissions.cs` | Permission constants for generated Dynamic API and app-service checks. |
| `framework/src/CrestCreates.Application/Features/FeatureAuditEntry.cs` | Structured audit payload for one feature change. |
| `framework/src/CrestCreates.Application/Features/IFeatureAuditRecorder.cs` | Abstraction for recording feature change audit data. |
| `framework/src/CrestCreates.Application/Features/FeatureAuditRecorder.cs` | Adds feature change details to `AuditContext.Current.ExtraProperties`. |
| `framework/src/CrestCreates.Application/Features/FeatureValueAppServiceMapper.cs` | Maps `ResolvedFeatureValue` and scoped entries to `FeatureValueDto`. |
| `framework/src/CrestCreates.Application/Features/FeatureManager.cs` | Validate writes, normalize values, enforce scope, update existing rows, invalidate cache, record audit. |
| `framework/src/CrestCreates.Application/Features/FeatureAppService.cs` | Enforce host/tenant boundaries, permissions, and return resolved values for read APIs. |
| `framework/src/CrestCreates.Application/Features/FeatureDefinitionAppService.cs` | Return groups and definitions through generated Dynamic API contracts. |
| `framework/src/CrestCreates.Application/Features/FeatureManagementServiceCollectionExtensions.cs` | Register mapper and audit recorder. |
| `framework/src/CrestCreates.Application/Tenants/TenantFeatureDefaultsSeeder.cs` | Keep tenant initialization feature defaults idempotent and manager-based. |
| `framework/src/CrestCreates.OrmProviders.EFCore/DbContexts/CrestCreatesDbContext.cs` | Ensure `FeatureValues` unique index remains `Name + Scope + ProviderKey + TenantId`. |
| `framework/test/CrestCreates.Application.Tests/Features/FeatureDefinitionManagerTests.cs` | Definition validation tests. |
| `framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs` | Manager, resolver, validation, cache and audit unit tests. |
| `framework/test/CrestCreates.Application.Tests/Features/FeatureAppServiceSecurityTests.cs` | Host/tenant permission and cross-tenant tests. |
| `framework/test/CrestCreates.Application.Tests/Tenants/TenantSeederScopeTests.cs` | Tenant feature defaults seeder idempotency/scope tests. |
| `framework/test/CrestCreates.IntegrationTests/FeatureManagementIntegrationTests.cs` | Generated Dynamic API and real integration behavior. |

---

### Task 1: Feature Error Codes And Business Exceptions

**Files:**
- Create: `framework/src/CrestCreates.Domain.Shared/Features/FeatureManagementErrorCodes.cs`
- Create: `framework/src/CrestCreates.Application/Features/FeatureManagementExceptionFactory.cs`
- Test: `framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs`

- [ ] **Step 1: Write failing tests for business exceptions**

Add these tests to `FeatureManagementTests`:

```csharp
[Fact]
public async Task UnknownFeature_ShouldThrowBusinessException()
{
    var action = async () => await _featureManager.SetGlobalAsync("Unknown.Feature", "true");

    var exception = await action.Should().ThrowAsync<CrestCreates.Domain.Shared.Exceptions.CrestBusinessException>();
    exception.Which.ErrorCode.Should().Be(FeatureManagementErrorCodes.UndefinedFeature);
    exception.Which.Message.Should().Contain("Unknown.Feature");
}

[Fact]
public async Task InvalidFeatureValue_ShouldThrowBusinessException()
{
    var action = async () => await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "abc");

    var exception = await action.Should().ThrowAsync<CrestCreates.Domain.Shared.Exceptions.CrestBusinessException>();
    exception.Which.ErrorCode.Should().Be(FeatureManagementErrorCodes.InvalidValue);
    exception.Which.Message.Should().Contain("Identity.UserCreationEnabled");
}
```

Add this `using` near the top:

```csharp
using CrestCreates.Domain.Shared.Features;
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureManagementTests.UnknownFeature_ShouldThrowBusinessException|FullyQualifiedName~FeatureManagementTests.InvalidFeatureValue_ShouldThrowBusinessException"
```

Expected: fail because `FeatureManagementErrorCodes` and `FeatureManagementExceptionFactory` do not exist, or because existing code throws `InvalidOperationException` / `ArgumentException`.

- [ ] **Step 3: Add error code constants**

Create `framework/src/CrestCreates.Domain.Shared/Features/FeatureManagementErrorCodes.cs`:

```csharp
namespace CrestCreates.Domain.Shared.Features;

public static class FeatureManagementErrorCodes
{
    public const string UndefinedFeature = "Crest.FeatureManagement.UndefinedFeature";
    public const string InvalidValue = "Crest.FeatureManagement.InvalidValue";
    public const string UnsupportedScope = "Crest.FeatureManagement.UnsupportedScope";
    public const string CrossTenantAccessDenied = "Crest.FeatureManagement.CrossTenantAccessDenied";
    public const string MissingTenantContext = "Crest.FeatureManagement.MissingTenantContext";
}
```

- [ ] **Step 4: Add exception factory**

Create `framework/src/CrestCreates.Application/Features/FeatureManagementExceptionFactory.cs`:

```csharp
using CrestCreates.Domain.Shared.Exceptions;
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Features;

internal static class FeatureManagementExceptionFactory
{
    public static CrestBusinessException UndefinedFeature(string name)
    {
        return new CrestBusinessException(
            FeatureManagementErrorCodes.UndefinedFeature,
            $"未定义的功能特性: {name}");
    }

    public static CrestBusinessException InvalidValue(string name, FeatureValueType valueType, string? value)
    {
        return new CrestBusinessException(
            FeatureManagementErrorCodes.InvalidValue,
            $"功能特性 '{name}' 的值 '{value}' 不是有效的 {valueType} 值");
    }

    public static CrestBusinessException UnsupportedScope(string name, FeatureScope scope)
    {
        return new CrestBusinessException(
            FeatureManagementErrorCodes.UnsupportedScope,
            $"功能特性 '{name}' 不支持作用域 {scope}");
    }

    public static CrestBusinessException MissingTenantContext()
    {
        return new CrestBusinessException(
            FeatureManagementErrorCodes.MissingTenantContext,
            "当前租户上下文不存在");
    }
}
```

- [ ] **Step 5: Update `FeatureManager` to use business exceptions**

In `FeatureManager`, replace `EnsureDefinitionExists`, `EnsureScopeAllowed`, and null-value validation with:

```csharp
private FeatureDefinition EnsureDefinitionExists(string name)
{
    return _featureDefinitionManager.GetOrNull(name)
           ?? throw FeatureManagementExceptionFactory.UndefinedFeature(name);
}

private static void EnsureScopeAllowed(FeatureDefinition definition, FeatureScope scope)
{
    if (!definition.SupportsScope(scope))
    {
        throw FeatureManagementExceptionFactory.UnsupportedScope(definition.Name, scope);
    }
}
```

In `SetAsync`, replace the null-value branch with:

```csharp
if (value == null)
{
    throw FeatureManagementExceptionFactory.InvalidValue(name, FeatureValueType.String, value);
}
```

Wrap `FeatureValueTypeConverter.Validate(...)`:

```csharp
try
{
    _featureValueTypeConverter.Validate(value, definition.ValueType, definition.Name);
}
catch (ArgumentException)
{
    throw FeatureManagementExceptionFactory.InvalidValue(definition.Name, definition.ValueType, value);
}
```

- [ ] **Step 6: Run tests and verify pass**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureManagementTests.UnknownFeature_ShouldThrowBusinessException|FullyQualifiedName~FeatureManagementTests.InvalidFeatureValue_ShouldThrowBusinessException"
```

Expected: pass.

- [ ] **Step 7: Commit**

```powershell
git add framework/src/CrestCreates.Domain.Shared/Features/FeatureManagementErrorCodes.cs framework/src/CrestCreates.Application/Features/FeatureManagementExceptionFactory.cs framework/src/CrestCreates.Application/Features/FeatureManager.cs framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs
git commit -m "feat: add feature management business errors"
```

---

### Task 2: Definition Validation And Duplicate Detection

**Files:**
- Modify: `framework/src/CrestCreates.Domain/Features/FeatureDefinitionManager.cs`
- Test: `framework/test/CrestCreates.Application.Tests/Features/FeatureDefinitionManagerTests.cs`

- [ ] **Step 1: Write failing duplicate definition test**

Add this nested provider and test to `FeatureDefinitionManagerTests`:

```csharp
[Fact]
public void FeatureDefinitionManager_WithDuplicateName_ShouldFail()
{
    var action = () => new FeatureDefinitionManager(
        new IFeatureDefinitionProvider[]
        {
            new CoreFeatureDefinitionProvider(),
            new DuplicateIdentityFeatureProvider()
        });

    action.Should().Throw<InvalidOperationException>()
        .WithMessage("*Identity.UserCreationEnabled*");
}

private sealed class DuplicateIdentityFeatureProvider : IFeatureDefinitionProvider
{
    public void Define(FeatureDefinitionContext context)
    {
        context.GetOrAddGroup("Duplicate", "Duplicate")
            .AddDefinition(
                "Identity.UserCreationEnabled",
                "Duplicate",
                "Duplicate definition",
                "true",
                FeatureValueType.Bool,
                FeatureScope.Global | FeatureScope.Tenant);
    }
}
```

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureDefinitionManagerTests.FeatureDefinitionManager_WithDuplicateName_ShouldFail"
```

Expected: fail if duplicates are silently overwritten or the message does not include the duplicate name.

- [ ] **Step 3: Implement duplicate validation**

In `FeatureDefinitionManager`, build `_definitions` from grouped definitions with explicit duplicate detection:

```csharp
var definitions = _groups
    .SelectMany(group => group.Definitions)
    .ToArray();

var duplicate = definitions
    .GroupBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
    .FirstOrDefault(group => group.Count() > 1);

if (duplicate is not null)
{
    throw new InvalidOperationException($"重复的功能特性定义: {duplicate.Key}");
}

_definitions = new ReadOnlyDictionary<string, FeatureDefinition>(
    definitions.ToDictionary(
        definition => definition.Name,
        definition => definition,
        StringComparer.OrdinalIgnoreCase));
```

Keep existing `GetGroups`, `GetAll`, and `GetOrNull` public behavior unchanged.

- [ ] **Step 4: Run definition tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureDefinitionManagerTests"
```

Expected: all `FeatureDefinitionManagerTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add framework/src/CrestCreates.Domain/Features/FeatureDefinitionManager.cs framework/test/CrestCreates.Application.Tests/Features/FeatureDefinitionManagerTests.cs
git commit -m "feat: validate duplicate feature definitions"
```

---

### Task 3: Manager Write Path, Scope Rules, And Cache Isolation

**Files:**
- Modify: `framework/src/CrestCreates.Application/Features/FeatureManager.cs`
- Test: `framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs`
- Test: `framework/test/CrestCreates.Application.Tests/Features/FeatureCacheKeyContributorTests.cs`

- [ ] **Step 1: Add unsupported-scope test**

Add a test-only provider in `FeatureManagementTests` constructor setup or create a local manager factory. The simplest isolated test is:

```csharp
[Fact]
public async Task SetTenantAsync_WithGlobalOnlyFeature_ShouldThrowBusinessException()
{
    var definitionManager = new FeatureDefinitionManager(new IFeatureDefinitionProvider[]
    {
        new GlobalOnlyFeatureProvider()
    });

    var manager = new FeatureManager(
        definitionManager,
        _featureRepositoryMock.Object,
        _featureStore,
        new FeatureValueTypeConverter(),
        new FeatureCacheInvalidator(
            new CrestCacheService(
                new CrestMemoryCache(new CacheOptions { Prefix = $"FeatureScope:{Guid.NewGuid():N}:" }),
                new CrestCacheKeyGenerator()),
            new FeatureCacheKeyContributor()));

    var action = async () => await manager.SetTenantAsync("Global.Only", "tenant-1", "true");

    var exception = await action.Should().ThrowAsync<CrestCreates.Domain.Shared.Exceptions.CrestBusinessException>();
    exception.Which.ErrorCode.Should().Be(FeatureManagementErrorCodes.UnsupportedScope);
}

private sealed class GlobalOnlyFeatureProvider : IFeatureDefinitionProvider
{
    public void Define(FeatureDefinitionContext context)
    {
        context.GetOrAddGroup("Global", "Global")
            .AddDefinition(
                "Global.Only",
                "Global Only",
                "Only global scope is supported",
                "false",
                FeatureValueType.Bool,
                FeatureScope.Global);
    }
}
```

- [ ] **Step 2: Add duplicate-row prevention test**

Add:

```csharp
[Fact]
public async Task SetTenantAsync_CalledTwice_ShouldUpdateExistingRow()
{
    await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");
    await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "false");

    _features.Where(feature =>
            feature.Name == "Identity.UserCreationEnabled" &&
            feature.Scope == FeatureScope.Tenant &&
            feature.TenantId == "tenant-1")
        .Should()
        .ContainSingle()
        .Which.Value.Should().Be("false");
}
```

- [ ] **Step 3: Add cache isolation test**

Add:

```csharp
[Fact]
public async Task SetTenantFeature_ShouldInvalidateOnlyThatTenantCache()
{
    await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "false");
    await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");

    var tenant1Before = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-1");
    var tenant2Before = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-2");

    tenant1Before.Value.Should().Be("true");
    tenant2Before.Value.Should().Be("false");

    await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "false");

    var tenant1After = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-1");
    var tenant2After = await _featureValueResolver.ResolveAsync("Identity.UserCreationEnabled", "tenant-2");

    tenant1After.Value.Should().Be("false");
    tenant2After.Value.Should().Be("false");
}
```

- [ ] **Step 4: Run tests and verify failure/pass status**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureManagementTests.SetTenantAsync_WithGlobalOnlyFeature_ShouldThrowBusinessException|FullyQualifiedName~FeatureManagementTests.SetTenantAsync_CalledTwice_ShouldUpdateExistingRow|FullyQualifiedName~FeatureManagementTests.SetTenantFeature_ShouldInvalidateOnlyThatTenantCache"
```

Expected: unsupported-scope may fail before Task 1/2 changes are complete; duplicate/cache tests should pass after manager remains update-first and invalidates tenant cache.

- [ ] **Step 5: Ensure manager always normalizes tenant ids**

In `FeatureManager.SetTenantAsync`, keep this shape:

```csharp
public Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default)
{
    var normalizedTenantId = Require(tenantId, nameof(tenantId));
    return SetAsync(name, FeatureScope.Tenant, normalizedTenantId, value, normalizedTenantId, cancellationToken);
}
```

In `RemoveTenantAsync`, keep this shape:

```csharp
public Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default)
{
    var normalizedTenantId = Require(tenantId, nameof(tenantId));
    return RemoveAsync(name, FeatureScope.Tenant, normalizedTenantId, normalizedTenantId, cancellationToken);
}
```

- [ ] **Step 6: Run all Feature application tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~Features"
```

Expected: all Feature application tests pass.

- [ ] **Step 7: Commit**

```powershell
git add framework/src/CrestCreates.Application/Features/FeatureManager.cs framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs framework/test/CrestCreates.Application.Tests/Features/FeatureCacheKeyContributorTests.cs
git commit -m "feat: harden feature manager scope and cache behavior"
```

---

### Task 4: AppService Resolved Values And Generated API Contracts

**Files:**
- Create: `framework/src/CrestCreates.Application/Features/FeatureValueAppServiceMapper.cs`
- Modify: `framework/src/CrestCreates.Application/Features/FeatureAppService.cs`
- Modify: `framework/src/CrestCreates.Application/Features/FeatureManagementServiceCollectionExtensions.cs`
- Test: `framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs`

- [ ] **Step 1: Write failing app-service mapping test**

Add:

```csharp
[Fact]
public async Task GetCurrentTenantValuesAsync_ShouldReturnResolvedDefinitions()
{
    await _featureManager.SetGlobalAsync("Identity.UserCreationEnabled", "false");
    await _featureManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");

    var currentTenant = new Mock<ICurrentTenant>();
    currentTenant.SetupGet(x => x.Id).Returns("tenant-1");

    var appService = new FeatureAppService(
        _featureManager,
        new FeatureProvider(
            _featureDefinitionManager,
            _featureValueResolver,
            new FeatureValueTypeConverter(),
            currentTenant.Object),
        _featureValueResolver,
        currentTenant.Object,
        new FeatureValueAppServiceMapper());

    var values = await appService.GetCurrentTenantValuesAsync();

    values.Should().Contain(value =>
        value.Name == "Identity.UserCreationEnabled" &&
        value.Value == "true" &&
        value.Scope == FeatureScope.Tenant &&
        value.TenantId == "tenant-1");

    values.Should().Contain(value => value.Name == "Storage.MaxFileCount");
}
```

Expected behavior: `GetCurrentTenantValuesAsync` returns resolved values for all definitions, not only persisted overrides.

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureManagementTests.GetCurrentTenantValuesAsync_ShouldReturnResolvedDefinitions"
```

Expected: fail until mapper exists and `FeatureAppService` returns resolver results.

- [ ] **Step 3: Add mapper**

Create `framework/src/CrestCreates.Application/Features/FeatureValueAppServiceMapper.cs`:

```csharp
using CrestCreates.Application.Contracts.DTOs.Features;
using CrestCreates.Domain.Features;

namespace CrestCreates.Application.Features;

public class FeatureValueAppServiceMapper
{
    public FeatureValueDto Map(ResolvedFeatureValue value)
    {
        return new FeatureValueDto
        {
            Name = value.Name,
            Value = value.Value,
            Scope = value.Scope,
            ProviderKey = value.ProviderKey,
            TenantId = value.TenantId
        };
    }

    public FeatureValueDto Map(FeatureValueEntry value)
    {
        return new FeatureValueDto
        {
            Name = value.Name,
            Value = value.Value,
            Scope = value.Scope,
            ProviderKey = value.ProviderKey,
            TenantId = value.TenantId
        };
    }
}
```

- [ ] **Step 4: Update `FeatureAppService` constructor**

Add field:

```csharp
private readonly FeatureValueAppServiceMapper _mapper;
```

Change constructor:

```csharp
public FeatureAppService(
    IFeatureManager featureManager,
    IFeatureProvider featureProvider,
    IFeatureValueResolver featureValueResolver,
    ICurrentTenant currentTenant,
    FeatureValueAppServiceMapper mapper)
{
    _featureManager = featureManager;
    _featureProvider = featureProvider;
    _featureValueResolver = featureValueResolver;
    _currentTenant = currentTenant;
    _mapper = mapper;
}
```

- [ ] **Step 5: Update current-context read methods**

Change `GetCurrentTenantValuesAsync`:

```csharp
public async Task<List<FeatureValueDto>> GetCurrentTenantValuesAsync()
{
    var tenantId = _currentTenant.Id;
    var resolved = await _featureValueResolver.ResolveAllAsync(
        tenantId: string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim());

    return resolved.Select(_mapper.Map).ToList();
}
```

Change `GetCurrentTenantValueAsync`:

```csharp
public async Task<FeatureValueDto?> GetCurrentTenantValueAsync(string name)
{
    var tenantId = string.IsNullOrWhiteSpace(_currentTenant.Id) ? null : _currentTenant.Id.Trim();
    var resolved = await _featureValueResolver.ResolveAsync(name, tenantId);
    return _mapper.Map(resolved);
}
```

Keep `GetGlobalValuesAsync` and `GetTenantValuesAsync` as scoped override views unless integration tests require resolved views.

- [ ] **Step 6: Replace manual mapping in private helpers**

Change `GetScopedValuesAsync` return mapping to:

```csharp
return values.Select(_mapper.Map).ToList();
```

Change `GetScopedValueAsync` return mapping to:

```csharp
return value == null ? null : _mapper.Map(value);
```

- [ ] **Step 7: Register mapper**

In `FeatureManagementServiceCollectionExtensions.AddFeatureManagement`, add:

```csharp
services.TryAddScoped<FeatureValueAppServiceMapper>();
```

Place it near `FeatureValueTypeConverter`.

- [ ] **Step 8: Run tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureManagementTests.GetCurrentTenantValuesAsync_ShouldReturnResolvedDefinitions"
```

Expected: pass.

- [ ] **Step 9: Commit**

```powershell
git add framework/src/CrestCreates.Application/Features/FeatureValueAppServiceMapper.cs framework/src/CrestCreates.Application/Features/FeatureAppService.cs framework/src/CrestCreates.Application/Features/FeatureManagementServiceCollectionExtensions.cs framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs
git commit -m "feat: return resolved feature values from app service"
```

---

### Task 5: Host/Tenant Permission Boundaries

**Files:**
- Create: `framework/src/CrestCreates.Application/Features/FeatureManagementPermissions.cs`
- Modify: `framework/src/CrestCreates.Application/Features/FeatureAppService.cs`
- Test: `framework/test/CrestCreates.Application.Tests/Features/FeatureAppServiceSecurityTests.cs`

- [ ] **Step 1: Write failing security tests**

Create `framework/test/CrestCreates.Application.Tests/Features/FeatureAppServiceSecurityTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using CrestCreates.Application.Features;
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Features;
using CrestCreates.Domain.Shared.Features;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.Application.Tests.Features;

public class FeatureAppServiceSecurityTests
{
    [Fact]
    public async Task TenantUser_ShouldNotManageOtherTenantFeature()
    {
        var fixture = FeatureAppServiceFixture.Create(currentTenantId: "tenant-a", grantPermissions: true);

        var action = async () => await fixture.AppService.SetTenantAsync(
            "Identity.UserCreationEnabled",
            "tenant-b",
            "true");

        await action.Should().ThrowAsync<CrestPermissionException>();
    }

    [Fact]
    public async Task HostWithoutPermission_ShouldNotSetGlobalFeature()
    {
        var fixture = FeatureAppServiceFixture.Create(currentTenantId: null, grantPermissions: false);

        var action = async () => await fixture.AppService.SetGlobalAsync(
            "Identity.UserCreationEnabled",
            "true");

        await action.Should().ThrowAsync<CrestPermissionException>();
    }

    private sealed class FeatureAppServiceFixture
    {
        public FeatureAppService AppService { get; private init; } = null!;

        public static FeatureAppServiceFixture Create(string? currentTenantId, bool grantPermissions)
        {
            var repository = new InMemoryFeatureRepository();
            var definitionManager = new FeatureDefinitionManager(new[] { new CoreFeatureDefinitionProvider() });
            var cacheService = new CrestCreates.Caching.CrestCacheService(
                new CrestCreates.Caching.Abstractions.CrestMemoryCache(
                    new CrestCreates.Caching.Abstractions.CacheOptions { Prefix = $"FeatureSecurity:{Guid.NewGuid():N}:" }),
                new CrestCreates.Caching.CrestCacheKeyGenerator());
            var cacheKeyContributor = new CrestCreates.Caching.FeatureCacheKeyContributor();
            var store = new FeatureStore(repository, cacheService, cacheKeyContributor);
            var resolver = new FeatureValueResolver(definitionManager, store);
            var currentTenant = new Mock<ICurrentTenant>();
            currentTenant.SetupGet(x => x.Id).Returns(currentTenantId ?? string.Empty);

            var permissionChecker = new Mock<IPermissionChecker>();
            permissionChecker
                .Setup(x => x.IsGrantedAsync(It.IsAny<string>()))
                .ReturnsAsync(grantPermissions);

            var appService = new FeatureAppService(
                new FeatureManager(
                    definitionManager,
                    repository,
                    store,
                    new FeatureValueTypeConverter(),
                    new CrestCreates.Caching.FeatureCacheInvalidator(cacheService, cacheKeyContributor)),
                new FeatureProvider(definitionManager, resolver, new FeatureValueTypeConverter(), currentTenant.Object),
                resolver,
                currentTenant.Object,
                new FeatureValueAppServiceMapper(),
                permissionChecker.Object);

            return new FeatureAppServiceFixture { AppService = appService };
        }
    }
}
```

Add this private nested repository to `FeatureAppServiceSecurityTests` so the test is self-contained:

```csharp
private sealed class InMemoryFeatureRepository : IFeatureRepository
{
    private readonly List<FeatureValue> _features = new();

    public Task<FeatureValue?> FindAsync(
        string name,
        FeatureScope scope,
        string providerKey,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_features.FirstOrDefault(feature =>
            feature.Name == name &&
            feature.Scope == scope &&
            feature.ProviderKey == providerKey &&
            feature.TenantId == tenantId));
    }

    public Task<List<FeatureValue>> GetListByScopeAsync(
        FeatureScope scope,
        string? providerKey = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_features
            .Where(feature => feature.Scope == scope)
            .Where(feature => providerKey is null || feature.ProviderKey == providerKey)
            .Where(feature => tenantId is null || feature.TenantId == tenantId)
            .OrderBy(feature => feature.Name)
            .ToList());
    }

    public Task<FeatureValue> InsertAsync(FeatureValue entity, CancellationToken cancellationToken = default)
    {
        _features.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<FeatureValue> UpdateAsync(FeatureValue entity, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(entity);
    }

    public Task DeleteAsync(FeatureValue entity, CancellationToken cancellationToken = default)
    {
        _features.Remove(entity);
        return Task.CompletedTask;
    }

    // Implement the remaining ICrestRepositoryBase members by throwing NotSupportedException,
    // because these tests only exercise FeatureManager's Find/Insert/Update/Delete path.
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureAppServiceSecurityTests"
```

Expected: fail until permissions are added to `FeatureAppService`.

- [ ] **Step 3: Add permission constants**

Create `framework/src/CrestCreates.Application/Features/FeatureManagementPermissions.cs`:

```csharp
namespace CrestCreates.Application.Features;

public static class FeatureManagementPermissions
{
    public const string Read = "FeatureManagement.Read";
    public const string ManageGlobal = "FeatureManagement.ManageGlobal";
    public const string ManageTenant = "FeatureManagement.ManageTenant";
}
```

- [ ] **Step 4: Inject `IPermissionChecker`**

In `FeatureAppService`, add:

```csharp
using CrestCreates.Authorization.Abstractions;
using CrestCreates.Domain.Exceptions;
```

Add field:

```csharp
private readonly IPermissionChecker _permissionChecker;
```

Extend constructor:

```csharp
public FeatureAppService(
    IFeatureManager featureManager,
    IFeatureProvider featureProvider,
    IFeatureValueResolver featureValueResolver,
    ICurrentTenant currentTenant,
    FeatureValueAppServiceMapper mapper,
    IPermissionChecker permissionChecker)
{
    _featureManager = featureManager;
    _featureProvider = featureProvider;
    _featureValueResolver = featureValueResolver;
    _currentTenant = currentTenant;
    _mapper = mapper;
    _permissionChecker = permissionChecker;
}
```

- [ ] **Step 5: Add permission helpers**

Add private methods:

```csharp
private async Task EnsureGrantedAsync(string permission)
{
    if (!await _permissionChecker.IsGrantedAsync(permission))
    {
        throw new CrestPermissionException(permission);
    }
}

private void EnsureHostContext(string permission)
{
    if (!string.IsNullOrWhiteSpace(_currentTenant.Id))
    {
        throw new CrestPermissionException(permission);
    }
}

private void EnsureCurrentTenantOrHost(string targetTenantId, string permission)
{
    var currentTenantId = string.IsNullOrWhiteSpace(_currentTenant.Id) ? null : _currentTenant.Id.Trim();
    if (currentTenantId is not null &&
        !string.Equals(currentTenantId, targetTenantId.Trim(), StringComparison.OrdinalIgnoreCase))
    {
        throw new CrestPermissionException(permission);
    }
}
```

- [ ] **Step 6: Guard write APIs**

Change write methods:

```csharp
public async Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default)
{
    EnsureHostContext(FeatureManagementPermissions.ManageGlobal);
    await EnsureGrantedAsync(FeatureManagementPermissions.ManageGlobal);
    await _featureManager.SetGlobalAsync(name, value, cancellationToken);
}

public async Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default)
{
    EnsureHostContext(FeatureManagementPermissions.ManageTenant);
    await EnsureGrantedAsync(FeatureManagementPermissions.ManageTenant);
    await _featureManager.SetTenantAsync(name, tenantId, value, cancellationToken);
}

public async Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default)
{
    EnsureHostContext(FeatureManagementPermissions.ManageGlobal);
    await EnsureGrantedAsync(FeatureManagementPermissions.ManageGlobal);
    await _featureManager.RemoveGlobalAsync(name, cancellationToken);
}

public async Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default)
{
    EnsureHostContext(FeatureManagementPermissions.ManageTenant);
    await EnsureGrantedAsync(FeatureManagementPermissions.ManageTenant);
    await _featureManager.RemoveTenantAsync(name, tenantId, cancellationToken);
}
```

Guard explicit tenant reads:

```csharp
public async Task<List<FeatureValueDto>> GetTenantValuesAsync(string tenantId)
{
    EnsureHostContext(FeatureManagementPermissions.Read);
    await EnsureGrantedAsync(FeatureManagementPermissions.Read);
    return await GetScopedValuesAsync(FeatureScope.Tenant, tenantId, tenantId);
}

public async Task<bool> IsTenantEnabledAsync(string tenantId, string featureName, CancellationToken cancellationToken = default)
{
    EnsureHostContext(FeatureManagementPermissions.Read);
    await EnsureGrantedAsync(FeatureManagementPermissions.Read);
    var resolved = await _featureValueResolver.ResolveAsync(featureName, tenantId, cancellationToken);
    return bool.TryParse(resolved.Value, out var result) && result;
}
```

- [ ] **Step 7: Run security tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureAppServiceSecurityTests"
```

Expected: pass.

- [ ] **Step 8: Run all Feature tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~Features"
```

Expected: pass. Update every test constructor call for `FeatureAppService` in `framework/test` to include a mocked `IPermissionChecker`:

```csharp
var permissionChecker = new Mock<IPermissionChecker>();
permissionChecker.Setup(x => x.IsGrantedAsync(It.IsAny<string>())).ReturnsAsync(true);
```

- [ ] **Step 9: Commit**

```powershell
git add framework/src/CrestCreates.Application/Features/FeatureManagementPermissions.cs framework/src/CrestCreates.Application/Features/FeatureAppService.cs framework/test/CrestCreates.Application.Tests/Features/FeatureAppServiceSecurityTests.cs framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs
git commit -m "feat: enforce feature management permissions"
```

---

### Task 6: Feature Audit Recording

**Files:**
- Create: `framework/src/CrestCreates.Application/Features/FeatureAuditEntry.cs`
- Create: `framework/src/CrestCreates.Application/Features/IFeatureAuditRecorder.cs`
- Create: `framework/src/CrestCreates.Application/Features/FeatureAuditRecorder.cs`
- Modify: `framework/src/CrestCreates.Application/Features/FeatureManager.cs`
- Modify: `framework/src/CrestCreates.Application/Features/FeatureManagementServiceCollectionExtensions.cs`
- Test: `framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs`

- [ ] **Step 1: Write failing audit test**

Add to `FeatureManagementTests`:

```csharp
[Fact]
public async Task FeatureChange_ShouldWriteAuditEntry()
{
    CrestCreates.AuditLogging.Context.AuditContext.SetCurrentForTesting(new CrestCreates.AuditLogging.Context.AuditContext
    {
        StartTime = DateTime.UtcNow,
        ExecutionTime = DateTime.UtcNow,
        UserId = "user-1",
        TenantId = "tenant-1",
        TraceId = "trace-1"
    });

    try
    {
        var auditedManager = new FeatureManager(
            _featureDefinitionManager,
            _featureRepositoryMock.Object,
            _featureStore,
            new FeatureValueTypeConverter(),
            new FeatureCacheInvalidator(
                new CrestCacheService(
                    new CrestMemoryCache(new CacheOptions { Prefix = $"FeatureAudit:{Guid.NewGuid():N}:" }),
                    new CrestCacheKeyGenerator()),
                new FeatureCacheKeyContributor()),
            new FeatureAuditRecorder());

        await auditedManager.SetTenantAsync("Identity.UserCreationEnabled", "tenant-1", "true");

        var auditContext = CrestCreates.AuditLogging.Context.AuditContext.Current;
        auditContext.Should().NotBeNull();
        auditContext!.ExtraProperties.Should().ContainKey("FeatureChanges");
        auditContext.ExtraProperties["FeatureChanges"].ToString().Should().Contain("Identity.UserCreationEnabled");
    }
    finally
    {
        CrestCreates.AuditLogging.Context.AuditContext.ClearCurrentForTesting();
    }
}
```

This test requires explicit test-only helpers on `AuditContext`. Add the public helper methods in the next step and do not use them from production code.

- [ ] **Step 2: Add test helpers to `AuditContext`**

Modify `framework/src/CrestCreates.AuditLogging/Context/AuditContext.cs` and add methods below `ClearCurrent`:

```csharp
public static void SetCurrentForTesting(AuditContext context) => _current.Value = context;

public static void ClearCurrentForTesting() => _current.Value = null;
```

These are explicit test helpers. Do not use them in production code.

- [ ] **Step 3: Create audit entry**

Create `framework/src/CrestCreates.Application/Features/FeatureAuditEntry.cs`:

```csharp
using CrestCreates.Domain.Shared.Features;

namespace CrestCreates.Application.Features;

public class FeatureAuditEntry
{
    public string FeatureName { get; init; } = string.Empty;
    public FeatureScope Scope { get; init; }
    public string? TenantId { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string Operation { get; init; } = string.Empty;
}
```

- [ ] **Step 4: Create recorder abstraction and implementation**

Create `framework/src/CrestCreates.Application/Features/IFeatureAuditRecorder.cs`:

```csharp
using System.Threading;
using System.Threading.Tasks;

namespace CrestCreates.Application.Features;

public interface IFeatureAuditRecorder
{
    Task RecordAsync(FeatureAuditEntry entry, CancellationToken cancellationToken = default);
}
```

Create `framework/src/CrestCreates.Application/Features/FeatureAuditRecorder.cs`:

```csharp
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrestCreates.AuditLogging.Context;

namespace CrestCreates.Application.Features;

public class FeatureAuditRecorder : IFeatureAuditRecorder
{
    private const string ExtraPropertyKey = "FeatureChanges";

    public Task RecordAsync(FeatureAuditEntry entry, CancellationToken cancellationToken = default)
    {
        var context = AuditContext.Current;
        if (context is null)
        {
            return Task.CompletedTask;
        }

        if (!context.ExtraProperties.TryGetValue(ExtraPropertyKey, out var value) ||
            value is not List<FeatureAuditEntry> entries)
        {
            entries = new List<FeatureAuditEntry>();
            context.ExtraProperties[ExtraPropertyKey] = entries;
        }

        entries.Add(entry);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: Inject recorder into `FeatureManager`**

Add field:

```csharp
private readonly IFeatureAuditRecorder _featureAuditRecorder;
```

Change constructor:

```csharp
public FeatureManager(
    IFeatureDefinitionManager featureDefinitionManager,
    IFeatureRepository featureRepository,
    IFeatureStore featureStore,
    FeatureValueTypeConverter featureValueTypeConverter,
    FeatureCacheInvalidator featureCacheInvalidator,
    IFeatureAuditRecorder featureAuditRecorder)
{
    _featureDefinitionManager = featureDefinitionManager;
    _featureRepository = featureRepository;
    _featureStore = featureStore;
    _featureValueTypeConverter = featureValueTypeConverter;
    _featureCacheInvalidator = featureCacheInvalidator;
    _featureAuditRecorder = featureAuditRecorder;
}
```

Update all tests and registrations to pass `new FeatureAuditRecorder()` or a mock.

- [ ] **Step 6: Record set/remove operations**

In `SetAsync`, before mutating existing value:

```csharp
var oldValue = existing?.Value;
var normalizedValue = NormalizeValue(value, definition.ValueType);
```

Use `normalizedValue` for insert/update. After cache invalidation:

```csharp
await _featureAuditRecorder.RecordAsync(
    new FeatureAuditEntry
    {
        FeatureName = definition.Name,
        Scope = scope,
        TenantId = normalizedTenantId,
        OldValue = oldValue,
        NewValue = normalizedValue,
        Operation = "Set"
    },
    cancellationToken);
```

In `RemoveAsync`, capture `oldValue` before delete and after cache invalidation:

```csharp
await _featureAuditRecorder.RecordAsync(
    new FeatureAuditEntry
    {
        FeatureName = definition.Name,
        Scope = scope,
        TenantId = normalizedTenantId,
        OldValue = existing.Value,
        NewValue = null,
        Operation = "Remove"
    },
    cancellationToken);
```

- [ ] **Step 7: Register audit recorder**

In `FeatureManagementServiceCollectionExtensions.AddFeatureManagement`, add:

```csharp
services.TryAddScoped<IFeatureAuditRecorder, FeatureAuditRecorder>();
```

- [ ] **Step 8: Run focused audit test**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~FeatureManagementTests.FeatureChange_ShouldWriteAuditEntry"
```

Expected: pass.

- [ ] **Step 9: Run Feature tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~Features"
```

Expected: pass after updating constructor calls.

- [ ] **Step 10: Commit**

```powershell
git add framework/src/CrestCreates.AuditLogging/Context/AuditContext.cs framework/src/CrestCreates.Application/Features/FeatureAuditEntry.cs framework/src/CrestCreates.Application/Features/IFeatureAuditRecorder.cs framework/src/CrestCreates.Application/Features/FeatureAuditRecorder.cs framework/src/CrestCreates.Application/Features/FeatureManager.cs framework/src/CrestCreates.Application/Features/FeatureManagementServiceCollectionExtensions.cs framework/test/CrestCreates.Application.Tests/Features/FeatureManagementTests.cs
git commit -m "feat: audit feature value changes"
```

---

### Task 7: Tenant Feature Defaults Seeder Idempotency

**Files:**
- Modify: `framework/src/CrestCreates.Application/Tenants/TenantFeatureDefaultsSeeder.cs`
- Test: `framework/test/CrestCreates.Application.Tests/Tenants/TenantSeederScopeTests.cs`

- [ ] **Step 1: Write failing idempotency test**

Add to `TenantSeederScopeTests`:

```csharp
[Fact]
public async Task FeatureDefaultsSeeder_ShouldBeIdempotent()
{
    var definitionManager = new Mock<IFeatureDefinitionManager>();
    definitionManager.Setup(x => x.GetAll()).Returns(new[]
    {
        new FeatureDefinition(
            "Identity.UserCreationEnabled",
            "User creation",
            "Allows creating users",
            "true",
            FeatureValueType.Bool,
            FeatureScope.Global | FeatureScope.Tenant)
    });

    var featureManager = new Mock<IFeatureManager>();
    featureManager
        .Setup(x => x.GetScopedValueOrNullAsync(
            "Identity.UserCreationEnabled",
            FeatureScope.Tenant,
            "tenant-1",
            "tenant-1",
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new FeatureValueEntry
        {
            Name = "Identity.UserCreationEnabled",
            Value = "true",
            Scope = FeatureScope.Tenant,
            ProviderKey = "tenant-1",
            TenantId = "tenant-1"
        });

    var scopedProvider = new Mock<IServiceProvider>();
    scopedProvider.Setup(x => x.GetService(typeof(IFeatureManager))).Returns(featureManager.Object);

    var scope = new Mock<IServiceScope>();
    scope.SetupGet(x => x.ServiceProvider).Returns(scopedProvider.Object);

    var scopeFactory = new Mock<IServiceScopeFactory>();
    scopeFactory.Setup(x => x.CreateScope()).Returns(scope.Object);

    var rootProvider = new Mock<IServiceProvider>();
    rootProvider.Setup(x => x.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);

    var seeder = new TenantFeatureDefaultsSeeder(
        definitionManager.Object,
        rootProvider.Object,
        Mock.Of<ILogger<TenantFeatureDefaultsSeeder>>());

    var result = await seeder.SeedAsync(new TenantInitializationContext
    {
        TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        TenantName = "tenant-1",
        CorrelationId = "test",
        ConnectionString = null
    });

    result.Success.Should().BeTrue();
    featureManager.Verify(x => x.SetTenantAsync(
        It.IsAny<string>(),
        It.IsAny<string>(),
        It.IsAny<string?>(),
        It.IsAny<CancellationToken>()), Times.Never);
}
```

Use `context.TenantId.ToString()` consistently as provider key and tenant id. Do not use `TenantName`.

- [ ] **Step 2: Run test and verify failure**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~TenantSeederScopeTests.FeatureDefaultsSeeder_ShouldBeIdempotent"
```

Expected: fail if seeder always writes or uses tenant name.

- [ ] **Step 3: Implement idempotent seeding**

In `TenantFeatureDefaultsSeeder.SeedAsync`, use this loop shape:

```csharp
var tenantId = context.TenantId.ToString();
var definitions = _definitionManager.GetAll()
    .Where(definition => definition.SupportsScope(FeatureScope.Tenant))
    .ToArray();

foreach (var definition in definitions)
{
    var existing = await featureManager.GetScopedValueOrNullAsync(
        definition.Name,
        FeatureScope.Tenant,
        tenantId,
        tenantId,
        cancellationToken);

    if (existing is not null)
    {
        continue;
    }

    // Default behavior is lazy fallback. Current definitions do not declare explicit tenant
    // defaults, so no write is needed.
}
```

Return `TenantFeatureDefaultsResult.Succeeded()` when no writes are needed.

- [ ] **Step 4: Run tenant seeder tests**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~TenantSeederScopeTests"
```

Expected: pass.

- [ ] **Step 5: Commit**

```powershell
git add framework/src/CrestCreates.Application/Tenants/TenantFeatureDefaultsSeeder.cs framework/test/CrestCreates.Application.Tests/Tenants/TenantSeederScopeTests.cs
git commit -m "feat: keep tenant feature defaults idempotent"
```

---

### Task 8: Generated Dynamic API And Integration Tests

**Files:**
- Modify: `framework/test/CrestCreates.IntegrationTests/FeatureManagementIntegrationTests.cs`
- Create: `framework/src/CrestCreates.Application.Contracts/Interfaces/IFeatureAppService.cs`
- Create: `framework/src/CrestCreates.Application.Contracts/Interfaces/IFeatureDefinitionAppService.cs`
- Modify: `samples/LibraryManagement/LibraryManagement.Web/Program.cs`

- [ ] **Step 1: Add generated-path integration test**

In `FeatureManagementIntegrationTests`, add:

```csharp
[Fact]
public async Task FeatureDynamicApi_ShouldWorkOnGeneratedPath()
{
    using var factory = await WebApplicationFactory.CreateAsync();
    var hostClient = await factory.CreateAuthenticatedHostClientAsync();

    var definitionsResponse = await hostClient.GetAsync("/api/feature-definition/groups");
    definitionsResponse.StatusCode.Should().Be(HttpStatusCode.OK, await definitionsResponse.Content.ReadAsStringAsync());

    var setResponse = await hostClient.PostAsJsonAsync(
        "/api/feature/set-global",
        new { name = "Identity.UserCreationEnabled", value = "false" });
    setResponse.StatusCode.Should().Be(HttpStatusCode.OK, await setResponse.Content.ReadAsStringAsync());

    var enabledResponse = await hostClient.GetAsync("/api/feature/is-enabled?featureName=Identity.UserCreationEnabled");
    enabledResponse.StatusCode.Should().Be(HttpStatusCode.OK, await enabledResponse.Content.ReadAsStringAsync());

    var envelope = await ReadJsonAsync<DynamicApiResponse<bool>>(enabledResponse);
    envelope.Data.Should().BeFalse();
}
```

Adjust URLs only after inspecting generated route conventions in `GeneratedDynamicApiEndpoints.g.cs`. Do not add runtime scanner fallback to make this pass.

- [ ] **Step 2: Add cross-tenant integration test**

Add:

```csharp
[Fact]
public async Task TenantUser_ShouldNotManageOtherTenantFeature()
{
    using var factory = await WebApplicationFactory.CreateAsync();
    var tenantA = await factory.CreateTenantAsync("tenant-feature-a");
    var tenantB = await factory.CreateTenantAsync("tenant-feature-b");
    var tenantClient = await factory.CreateAuthenticatedTenantClientAsync(tenantA.Name, "admin", "Admin123!");

    var response = await tenantClient.PostAsJsonAsync(
        "/api/feature/set-tenant",
        new { name = "Identity.UserCreationEnabled", tenantId = tenantB.Id.ToString(), value = "false" });

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
}
```

Before writing the test, copy the authenticated-client helper names from `IntegrationTests.cs` / `TenantManagementFullChainIntegrationTests.cs` into this test file. Keep the same assertion: tenant A receives `HttpStatusCode.Forbidden` when trying to update tenant B.

- [ ] **Step 3: Add audit integration test**

Add:

```csharp
[Fact]
public async Task FeatureChange_ShouldWriteAuditLog()
{
    using var factory = await WebApplicationFactory.CreateAsync();
    var hostClient = await factory.CreateAuthenticatedHostClientAsync();

    var response = await hostClient.PostAsJsonAsync(
        "/api/feature/set-global",
        new { name = "Identity.UserCreationEnabled", value = "true" });
    response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

    var auditResponse = await hostClient.GetAsync("/api/audit-log?keyword=Identity.UserCreationEnabled");
    auditResponse.StatusCode.Should().Be(HttpStatusCode.OK, await auditResponse.Content.ReadAsStringAsync());

    var body = await auditResponse.Content.ReadAsStringAsync();
    body.Should().Contain("FeatureChanges");
    body.Should().Contain("Identity.UserCreationEnabled");
}
```

Use the same audit-log query route and query parameter names already used in `AuditLogIntegrationTests`. The assertion must still check for `FeatureChanges` and `Identity.UserCreationEnabled` in the returned audit payload.

- [ ] **Step 4: Run focused integration tests**

Run with Docker PostgreSQL running:

```powershell
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~FeatureManagementIntegrationTests.FeatureDynamicApi_ShouldWorkOnGeneratedPath|FullyQualifiedName~FeatureManagementIntegrationTests.TenantUser_ShouldNotManageOtherTenantFeature|FullyQualifiedName~FeatureManagementIntegrationTests.FeatureChange_ShouldWriteAuditLog"
```

Expected: fail until route contracts, permissions, and audit are aligned.

- [ ] **Step 5: Ensure app service contracts are in `Application.Contracts`**

Move the public contracts out of implementation files and into:

```text
framework/src/CrestCreates.Application.Contracts/Interfaces/IFeatureAppService.cs
framework/src/CrestCreates.Application.Contracts/Interfaces/IFeatureDefinitionAppService.cs
```

Use this interface shape:

```csharp
using CrestCreates.Application.Contracts.DTOs.Features;

namespace CrestCreates.Application.Contracts.Interfaces;

public interface IFeatureAppService
{
    Task<List<FeatureValueDto>> GetGlobalValuesAsync();
    Task<List<FeatureValueDto>> GetTenantValuesAsync(string tenantId);
    Task<List<FeatureValueDto>> GetCurrentTenantValuesAsync();
    Task<FeatureValueDto?> GetGlobalValueAsync(string name);
    Task<FeatureValueDto?> GetTenantValueAsync(string name, string tenantId);
    Task<FeatureValueDto?> GetCurrentTenantValueAsync(string name);
    Task SetGlobalAsync(string name, string? value, CancellationToken cancellationToken = default);
    Task SetTenantAsync(string name, string tenantId, string? value, CancellationToken cancellationToken = default);
    Task RemoveGlobalAsync(string name, CancellationToken cancellationToken = default);
    Task RemoveTenantAsync(string name, string tenantId, CancellationToken cancellationToken = default);
    Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default);
    Task<bool> IsTenantEnabledAsync(string tenantId, string featureName, CancellationToken cancellationToken = default);
}
```

Then update `FeatureAppService` to implement this contracts namespace and remove the nested interface from the implementation file. This keeps generated Dynamic API discovery consistent with other application services.

- [ ] **Step 6: Run integration tests again**

Run:

```powershell
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~FeatureManagementIntegrationTests"
```

Expected: all Feature Management integration tests pass.

- [ ] **Step 7: Run broader validation**

Run:

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~Features|FullyQualifiedName~TenantSeederScopeTests"
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~FeatureManagementIntegrationTests"
```

Expected: pass.

- [ ] **Step 8: Commit**

```powershell
git add framework/test/CrestCreates.IntegrationTests/FeatureManagementIntegrationTests.cs framework/src/CrestCreates.Application.Contracts/Interfaces/IFeatureAppService.cs framework/src/CrestCreates.Application.Contracts/Interfaces/IFeatureDefinitionAppService.cs framework/src/CrestCreates.Application/Features/FeatureAppService.cs framework/src/CrestCreates.Application/Features/FeatureDefinitionAppService.cs samples/LibraryManagement/LibraryManagement.Web/Program.cs
git commit -m "test: verify feature management generated api mainline"
```

---

## Final Verification

- [ ] **Step 1: Run application Feature tests**

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~Features"
```

Expected: pass.

- [ ] **Step 2: Run tenant seeder tests**

```powershell
dotnet test framework\test\CrestCreates.Application.Tests\CrestCreates.Application.Tests.csproj --filter "FullyQualifiedName~TenantSeederScopeTests"
```

Expected: pass.

- [ ] **Step 3: Run integration Feature tests with Docker PostgreSQL**

```powershell
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~FeatureManagementIntegrationTests"
```

Expected: pass.

- [ ] **Step 4: Run generated Dynamic API regression tests**

```powershell
dotnet test framework\test\CrestCreates.IntegrationTests\CrestCreates.IntegrationTests.csproj --filter "FullyQualifiedName~DynamicApi_MainChain_CanUpdateQueryDeleteAndShowInSwagger|FullyQualifiedName~FeatureManagementIntegrationTests.FeatureDynamicApi_ShouldWorkOnGeneratedPath"
```

Expected: pass and no `AmbiguousMatchException`.

---

## Self-Review Checklist For Implementer

- [ ] Every Feature write goes through `IFeatureManager`.
- [ ] `FeatureAppService` does not write repositories directly.
- [ ] `TenantFeatureDefaultsSeeder` uses `context.TenantId.ToString()`, not `TenantName`.
- [ ] `FeatureChecker` remains read-only and does not grant permissions.
- [ ] Runtime Dynamic API scanner/executor files are untouched.
- [ ] Feature audit data is recorded through the existing `AuditContext` / AuditLogging mainline.
- [ ] Cache invalidation includes scope, provider key, tenant id, and feature name.
- [ ] All new tests fail before implementation and pass after implementation.
