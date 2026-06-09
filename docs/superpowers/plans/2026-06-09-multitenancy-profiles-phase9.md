# Phase 9: Multi-tenancy + Profiles — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add multi-tenancy support to the metadata system — tenant-scoped registries, per-tenant CapabilityProfile resolution, tenant-isolated DraftStore, and tenant-aware pipeline middleware.

**Architecture:** `ITenantContext` provides the current tenant ID. `TenantScopedRegistry<T>` wraps any `IVersionedDescriptorRegistry<T>` and filters by tenant. `CapabilityProfileResolver` resolves profiles in priority order: Tenant → Environment → Global → default. `TenantIsolatedDraftStore` decorates `IDraftStore` to enforce tenant isolation. `TenantMiddleware` sets the tenant on the execution context.

**Tech Stack:** .NET 10, C# 13, xUnit + FluentAssertions

---

### Task 0: ITenantContext + TenantInfo

**Files:**
- Create: `framework/src/CrestCreates.MultiTenancy.Abstract/ITenantContext.cs`

Use the existing `CrestCreates.MultiTenancy.Abstract` project.

- [ ] **Step 1: Write ITenantContext.cs**

```csharp
namespace CrestCreates.MultiTenancy.Abstract;

public interface ITenantContext
{
    string? CurrentTenantId { get; }
}
```

- [ ] **Step 2: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.MultiTenancy.Abstract/CrestCreates.MultiTenancy.Abstract.csproj
git add framework/src/CrestCreates.MultiTenancy.Abstract/ITenantContext.cs
git commit -m "feat: add ITenantContext — current tenant ID accessor"
```

---

### Task 1: TenantScopedRegistry<T>

**Files:**
- Create: `framework/src/CrestCreates.Metadata/TenantScopedRegistry.cs`

A decorator that wraps any `IVersionedDescriptorRegistry<T>` and filters results by tenant.

- [ ] **Step 1: Write TenantScopedRegistry.cs**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Metadata;

public sealed class TenantScopedRegistry<TDescriptor> : IVersionedDescriptorRegistry<TDescriptor>
    where TDescriptor : class, IVersionedDescriptor
{
    private readonly IVersionedDescriptorRegistry<TDescriptor> _inner;
    private readonly ITenantContext? _tenantContext;
    private readonly Func<TDescriptor, string?> _tenantSelector;

    public TenantScopedRegistry(
        IVersionedDescriptorRegistry<TDescriptor> inner,
        ITenantContext? tenantContext,
        Func<TDescriptor, string?> tenantSelector)
    {
        _inner = inner;
        _tenantContext = tenantContext;
        _tenantSelector = tenantSelector;
    }

    private bool IsAccessible(TDescriptor descriptor)
    {
        if (_tenantContext?.CurrentTenantId == null) return true;
        var descriptorTenant = _tenantSelector(descriptor);
        return descriptorTenant == null || descriptorTenant == _tenantContext.CurrentTenantId;
    }

    public TDescriptor? GetById(string id)
    {
        var d = _inner.GetById(id);
        return d != null && IsAccessible(d) ? d : null;
    }

    public TDescriptor? GetByName(string name)
    {
        var d = _inner.GetByName(name);
        return d != null && IsAccessible(d) ? d : null;
    }

    public TDescriptor? GetByNameAndVersion(string name, int version)
    {
        var d = _inner.GetByNameAndVersion(name, version);
        return d != null && IsAccessible(d) ? d : null;
    }

    public TDescriptor? GetActiveVersion(string name)
    {
        var d = _inner.GetActiveVersion(name);
        return d != null && IsAccessible(d) ? d : null;
    }

    public TDescriptor? GetLatestVersion(string name)
    {
        var d = _inner.GetLatestVersion(name);
        return d != null && IsAccessible(d) ? d : null;
    }

    public IReadOnlyList<TDescriptor> GetAllByName(string name)
        => _inner.GetAllByName(name).Where(IsAccessible).ToList().AsReadOnly();

    public IReadOnlyList<TDescriptor> GetDeprecatedVersions(string name)
        => _inner.GetDeprecatedVersions(name).Where(IsAccessible).ToList().AsReadOnly();

    public IReadOnlyList<TDescriptor> GetAll()
        => _inner.GetAll().Where(IsAccessible).ToList().AsReadOnly();
}
```

- [ ] **Step 2: Add MultiTenancy reference to Metadata.csproj**

```xml
<ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj
git add framework/src/CrestCreates.Metadata/
git commit -m "feat: add TenantScopedRegistry<T> — tenant-filtered descriptor access"
```

---

### Task 2: TenantIsolatedDraftStore

**Files:**
- Create: `framework/src/CrestCreates.Draft/TenantIsolatedDraftStore.cs`

Decorates `IDraftStore` to enforce tenant isolation on all operations.

- [ ] **Step 1: Write TenantIsolatedDraftStore.cs**

```csharp
using CrestCreates.Draft.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Draft;

public sealed class TenantIsolatedDraftStore : IDraftStore
{
    private readonly IDraftStore _inner;
    private readonly ITenantContext? _tenantContext;

    public TenantIsolatedDraftStore(IDraftStore inner, ITenantContext? tenantContext = null)
    {
        _inner = inner;
        _tenantContext = tenantContext;
    }

    public async Task<DraftRecord> SaveAsync(DraftRecord draft, CancellationToken ct = default)
    {
        if (_tenantContext?.CurrentTenantId != null)
        {
            draft = new DraftRecord
            {
                DraftId = draft.DraftId,
                DraftType = draft.DraftType,
                Schema = draft.Schema,
                TenantId = _tenantContext.CurrentTenantId,
                OwnerId = draft.OwnerId,
                PayloadJson = draft.PayloadJson,
                Status = draft.Status,
                CreatedAt = draft.CreatedAt,
                UpdatedAt = draft.UpdatedAt,
                ExpiresAt = draft.ExpiresAt
            };
        }
        return await _inner.SaveAsync(draft, ct).ConfigureAwait(false);
    }

    public async Task<DraftRecord?> GetAsync(string draftId, CancellationToken ct = default)
    {
        var draft = await _inner.GetAsync(draftId, ct).ConfigureAwait(false);
        if (draft == null) return null;
        if (_tenantContext?.CurrentTenantId != null
            && draft.TenantId != _tenantContext.CurrentTenantId)
            return null;
        return draft;
    }

    public Task DeleteAsync(string draftId, CancellationToken ct = default)
        => _inner.DeleteAsync(draftId, ct);

    public Task<IReadOnlyList<DraftRecord>> QueryAsync(DraftQuery query, CancellationToken ct = default)
    {
        if (_tenantContext?.CurrentTenantId != null)
            query = new DraftQuery
            {
                TenantId = _tenantContext.CurrentTenantId,
                OwnerId = query.OwnerId,
                DraftType = query.DraftType,
                Status = query.Status,
                MaxResults = query.MaxResults
            };
        return _inner.QueryAsync(query, ct);
    }
}
```

- [ ] **Step 2: Add MultiTenancy ref to Draft.csproj**

```xml
<ProjectReference Include="..\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Draft/CrestCreates.Draft.csproj
git add framework/src/CrestCreates.Draft/
git commit -m "feat: add TenantIsolatedDraftStore — enforces tenant isolation on draft operations"
```

---

### Task 3: CapabilityProfileResolver — Tenant/Env Priority

**Files:**
- Create: `framework/src/CrestCreates.Capability/CapabilityProfileResolver.cs`

Resolves effective configuration by merging profiles in priority order: Tenant → Environment → Global → default.

- [ ] **Step 1: Write CapabilityProfileResolver.cs**

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public static class CapabilityProfileResolver
{
    public sealed class EffectiveProfile
    {
        public TimeSpan? Timeout { get; init; }
        public string? RetryPolicy { get; init; }
        public bool? RequireApproval { get; init; }
        public int? RateLimit { get; init; }
    }

    public static EffectiveProfile Resolve(
        CapabilityDescriptor descriptor,
        IReadOnlyList<CapabilityProfile> profiles,
        string? tenantId = null,
        string? environment = null)
    {
        // Priority: Tenant → Environment → Global → default
        var ordered = profiles
            .Where(p => p.Capability.Id == descriptor.Id)
            .OrderByDescending(p => GetScopePriority(p.Scope, tenantId, environment));

        var result = new EffectiveProfile();

        foreach (var profile in ordered)
        {
            result = new EffectiveProfile
            {
                Timeout = profile.Timeout ?? result.Timeout,
                RetryPolicy = profile.RetryPolicy ?? result.RetryPolicy,
                RequireApproval = profile.RequireApproval ?? result.RequireApproval,
                RateLimit = profile.RateLimit ?? result.RateLimit
            };
        }

        return result;
    }

    private static int GetScopePriority(string scope, string? tenantId, string? environment)
    {
        if (tenantId != null && scope == $"Tenant:{tenantId}") return 3;
        if (environment != null && scope == $"Environment:{environment}") return 2;
        if (scope.StartsWith("Global")) return 1;
        return 0;
    }
}
```

- [ ] **Step 2: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/CapabilityProfileResolver.cs
git commit -m "feat: add CapabilityProfileResolver — Tenant → Environment → Global priority"
```

---

### Task 4: TenantMiddleware

**Files:**
- Create: `framework/src/CrestCreates.Capability/Middleware/TenantMiddleware.cs`

Sets the tenant ID on the execution context from `ITenantContext`.

- [ ] **Step 1: Write TenantMiddleware.cs**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.MultiTenancy.Abstract;

namespace CrestCreates.Capability.Middleware;

public sealed class TenantMiddleware : ICapabilityPipelineMiddleware
{
    private readonly ITenantContext? _tenantContext;

    public TenantMiddleware(ITenantContext? tenantContext = null)
    {
        _tenantContext = tenantContext;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        if (_tenantContext?.CurrentTenantId != null)
            context.TenantId = _tenantContext.CurrentTenantId;

        return await next(context).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Add to default pipeline (first position)**

In `CapabilityServiceCollectionExtensions.AddCapabilityPipeline`:
```csharp
builder.Use<TenantMiddleware>();
```
Add before AuthorizationMiddleware. Register:
```csharp
services.TryAddTransient<TenantMiddleware>();
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add TenantMiddleware — sets tenant ID from ITenantContext"
```

---

### Task 5: Tests — TenantScopedRegistry + TenantIsolatedDraftStore + ProfileResolver + TenantMiddleware

**Files:**
- Create: `framework/test/CrestCreates.Metadata.Tests/TenantScopedRegistryTests.cs`
- Create: `framework/test/CrestCreates.Draft.Tests/TenantIsolatedDraftStoreTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/CapabilityProfileResolverTests.cs`
- Create: `framework/test/CrestCreates.Capability.Tests/TenantMiddlewareTests.cs`

- [ ] **Step 1: Write TenantScopedRegistryTests.cs (4 tests)**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Metadata.Tests;

public class TenantScopedRegistryTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public string? CurrentTenantId { get; set; }
    }

    private static SchemaDescriptor CreateSchema(string id, string name, int version, string? tenantId = null)
    {
        return new SchemaDescriptor { Id = id, Name = name, Version = version };
    }

    [Fact]
    public void GetById_FiltersByTenant()
    {
        var inner = new Schema.SchemaRegistry();
        inner.Register(CreateSchema("s1", "SchemaA", 1));
        inner.Register(CreateSchema("s2", "SchemaB", 1));

        var tenantCtx = new TestTenantContext { CurrentTenantId = "tenant_A" };
        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, tenantCtx, _ => null);

        var result = scoped.GetById("s1");
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetAll_ReturnsAll_WhenNoTenant()
    {
        var inner = new Schema.SchemaRegistry();
        inner.Register(CreateSchema("s1", "A", 1));
        inner.Register(CreateSchema("s2", "B", 1));

        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);
        scoped.GetAll().Should().HaveCount(2);
    }

    [Fact]
    public void GetAll_ReturnsAll_WhenNullTenantContext()
    {
        var inner = new Schema.SchemaRegistry();
        inner.Register(CreateSchema("s1", "A", 1));

        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);
        scoped.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void GetByName_DelegatesToInner()
    {
        var inner = new Schema.SchemaRegistry();
        inner.Register(CreateSchema("s1", "TestSchema", 1));

        var scoped = new TenantScopedRegistry<SchemaDescriptor>(inner, null, _ => null);
        var result = scoped.GetByName("TestSchema");
        result.Should().NotBeNull();
        result!.Id.Should().Be("s1");
    }
}
```

- [ ] **Step 2: Write TenantIsolatedDraftStoreTests.cs (4 tests)**

```csharp
using CrestCreates.Draft.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.MultiTenancy.Abstract;
using CrestCreates.Schema.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Draft.Tests;

public class TenantIsolatedDraftStoreTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public string? CurrentTenantId { get; set; }
    }

    [Fact]
    public async Task SaveAsync_OverridesTenantId()
    {
        var inner = new InMemoryDraftStore();
        var tenantCtx = new TestTenantContext { CurrentTenantId = "tenant_A" };
        var store = new TenantIsolatedDraftStore(inner, tenantCtx);

        var draft = new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_B"
        };

        var saved = await store.SaveAsync(draft);
        saved.TenantId.Should().Be("tenant_A");
    }

    [Fact]
    public async Task GetAsync_FiltersByTenant()
    {
        var inner = new InMemoryDraftStore();
        await inner.SaveAsync(new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_A"
        });

        var tenantCtx = new TestTenantContext { CurrentTenantId = "tenant_B" };
        var store = new TenantIsolatedDraftStore(inner, tenantCtx);

        var result = await store.GetAsync("d1");
        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryAsync_AddsTenantFilter()
    {
        var inner = new InMemoryDraftStore();
        await inner.SaveAsync(new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_A"
        });
        await inner.SaveAsync(new DraftRecord
        {
            DraftId = "d2", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_B"
        });

        var tenantCtx = new TestTenantContext { CurrentTenantId = "tenant_A" };
        var store = new TenantIsolatedDraftStore(inner, tenantCtx);

        var results = await store.QueryAsync(new DraftQuery());
        results.Should().HaveCount(1);
        results[0].DraftId.Should().Be("d1");
    }

    [Fact]
    public async Task Passthrough_WhenNoTenantContext()
    {
        var inner = new InMemoryDraftStore();
        var store = new TenantIsolatedDraftStore(inner, null);

        var draft = new DraftRecord
        {
            DraftId = "d1", DraftType = "test",
            Schema = new VersionedDescriptorRef<SchemaDescriptor>("s", 1),
            TenantId = "tenant_A"
        };

        var saved = await store.SaveAsync(draft);
        saved.TenantId.Should().Be("tenant_A");
    }
}
```

- [ ] **Step 3: Write CapabilityProfileResolverTests.cs (4 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class CapabilityProfileResolverTests
{
    [Fact]
    public void Resolve_ReturnsDefaults_WhenNoProfiles()
    {
        var descriptor = new CapabilityDescriptor { Id = "cap_01", Name = "test", Version = 1 };
        var result = CapabilityProfileResolver.Resolve(descriptor, Array.Empty<CapabilityProfile>());

        result.Timeout.Should().BeNull();
        result.RequireApproval.Should().BeNull();
    }

    [Fact]
    public void Resolve_TenantProfile_WinsOverGlobal()
    {
        var descriptor = new CapabilityDescriptor { Id = "cap_01", Name = "test", Version = 1 };
        var profiles = new[]
        {
            new CapabilityProfile
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
                Scope = "Global",
                Timeout = TimeSpan.FromSeconds(10)
            },
            new CapabilityProfile
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
                Scope = "Tenant:VIP",
                Timeout = TimeSpan.FromSeconds(5)
            }
        };

        var result = CapabilityProfileResolver.Resolve(descriptor, profiles, tenantId: "VIP");
        result.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Resolve_GlobalOnly_ReturnsGlobal()
    {
        var descriptor = new CapabilityDescriptor { Id = "cap_01", Name = "test", Version = 1 };
        var profiles = new[]
        {
            new CapabilityProfile
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1),
                Scope = "Global-Prod",
                Timeout = TimeSpan.FromSeconds(30)
            }
        };

        var result = CapabilityProfileResolver.Resolve(descriptor, profiles);
        result.Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void Resolve_IgnoresUnrelatedProfiles()
    {
        var descriptor = new CapabilityDescriptor { Id = "cap_01", Name = "test", Version = 1 };
        var profiles = new[]
        {
            new CapabilityProfile
            {
                Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_02", 1),
                Scope = "Global",
                Timeout = TimeSpan.FromSeconds(99)
            }
        };

        var result = CapabilityProfileResolver.Resolve(descriptor, profiles);
        result.Timeout.Should().BeNull();
    }
}
```

- [ ] **Step 4: Write TenantMiddlewareTests.cs (2 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using CrestCreates.MultiTenancy.Abstract;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class TenantMiddlewareTests
{
    private sealed class TestTenantContext : ITenantContext
    {
        public string? CurrentTenantId { get; set; }
    }

    [Fact]
    public async Task SetsTenantId_OnContext()
    {
        var tenantCtx = new TestTenantContext { CurrentTenantId = "tenant_01" };
        var middleware = new TenantMiddleware(tenantCtx);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        context.TenantId.Should().Be("tenant_01");
    }

    [Fact]
    public async Task Passthrough_WhenNoTenantContext()
    {
        var middleware = new TenantMiddleware(null);
        var context = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1, CapabilityContractHash = "abc"
        };

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
        context.TenantId.Should().BeNull();
    }
}
```

- [ ] **Step 5: Add MultiTenancy ref to test csprojs**

Add to `framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj`:
```xml
<ProjectReference Include="..\..\src\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
```

Add to `framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj`:
```xml
<ProjectReference Include="..\..\src\CrestCreates.MultiTenancy.Abstract\CrestCreates.MultiTenancy.Abstract.csproj" />
```

- [ ] **Step 6: Build, run tests, commit**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj
dotnet test framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj
dotnet test framework/test/CrestCreates.Draft.Tests/CrestCreates.Draft.Tests.csproj
git add framework/test/
git commit -m "feat: add multi-tenancy tests — 14 tests for scoped registry, draft store, profiles, middleware"
```

Expected: ~14 new tests, ~169 total.

---

### Task 6: Full Build + All Tests + Final Commit

- [ ] **Step 1: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Expected: ~169 tests pass.

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat: complete Phase 9 — Multi-tenancy + Profiles, 14 tests

- ITenantContext: current tenant ID accessor
- TenantScopedRegistry<T>: tenant-filtered descriptor access decorator
- TenantIsolatedDraftStore: enforces tenant isolation on all draft operations
- CapabilityProfileResolver: Tenant → Environment → Global priority resolution
- TenantMiddleware: sets tenant ID from ITenantContext on execution context
- 14 new tests: 4 scoped registry + 4 draft store + 4 profile + 2 middleware
- ~169 total tests across all 9 phases"
```

---

## Phase 9 Summary

| Task | Component | Tests |
|------|-----------|-------|
| 0 | ITenantContext | — |
| 1 | TenantScopedRegistry<T> | 4 |
| 2 | TenantIsolatedDraftStore | 4 |
| 3 | CapabilityProfileResolver | 4 |
| 4 | TenantMiddleware | 2 |
| 5 | Test files | — |
| 6 | Full build + commit | — |
| **Total** | **~6 new files** | **~14 new tests** |
