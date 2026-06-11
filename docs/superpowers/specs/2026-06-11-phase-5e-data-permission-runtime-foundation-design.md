# Phase 5e Design: Data Permission Runtime Foundation

**Date**: 2026-06-11
**Status**: Draft — awaiting review
**Predecessor**: Phase 5d — Capability Authorization Bridge
**Approach**: Composition with Runtime Facade (Approach A)

---

## 1. Overview

### 1.1 Goal

Establish an organization-identity-driven data permission runtime foundation. Given `userId + tenantId + permission + resource + action`, the system resolves a `DataPermissionScope` and converts it into an ORM-neutral `DataPermissionFilter` intermediate model for consumption by EF/SqlSugar/Mongo/Dynamic API/Capability handlers.

### 1.2 Principles

1. **Continue the Organization main chain.** No second identity, role, or permission-grant model.
2. **ORM-neutral filter model.** No EF QueryFilter, SqlSugar filter, Mongo FilterDefinition, SQL, or LINQ Expression generation in this phase.
3. **Fail closed.** Missing mappings, missing org assignments → denied access. Never silently allow.
4. **AoT-friendly.** Zero reflection, zero `System.Linq.Expressions`, zero dynamic code generation.
5. **Do not touch the legacy `IDataPermissionFilter`.** It remains as-is. Phase 5e builds the new chain alongside it.
6. **Do not modify AuthorizationMiddleware or PermissionCapabilityAuthorizationService.** Execution permission and data permission are separate concerns.

### 1.3 Architecture

```
DataPermissionScopeRequest
        │
        ▼
IDataPermissionScopeRuleStore ──┐
IDataPermissionScopeProvider ───┤──── IDataPermissionRuntime
IDataPermissionFilterBuilder ───┘          │
                                    ResolveScopeAsync()
                                    BuildFilter()
```

Three independently testable units composed by a thin runtime facade. All types live in `CrestCreates.Organization.Abstractions` or `CrestCreates.Organization`. No new projects.

### 1.4 Design Decisions Summary

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | All types in `Organization.Abstractions` | Follows existing pattern; ORM providers only need one reference |
| 2 | `resource` = entity name, `action` = Read/Create/Update/Delete (Query = Read alias) | CRUD semantics match existing `CrestAppServiceBase` patterns |
| 3 | InMemory scope rule store included | Enables per-resource/action scope configuration; lightweight, no DB |
| 4 | Fail closed when no primary org for OwnOrganization | Secure default — `scope.OrganizationUnitId is null` → `None` |
| 5 | Filter builder is stateless Singleton | Pure computation, no dependencies |
| 6 | Runtime is Scoped | Delegates to Scoped `IDataPermissionScopeProvider` |
| 7 | Rule store is Singleton | `ConcurrentDictionary`, thread-safe |
| 8 | Old `GetScopeAsync(userId, permission, tenantId)` kept as adapter | Preserves backward compatibility |
| 9 | `Custom` scope kind added but unresolved | Reserved; builder returns `IsDenied` for Custom/unknown (fail-closed) |

### 1.5 Explicit Non-Goals

- No EF QueryFilter, SqlSugar filter, Mongo FilterDefinition, SQL, LINQ Expression generation
- No second PermissionChecker / PermissionGrant / RolePermission system
- No `UserOrganizationRoleAssignment` → RBAC wiring
- No modification of `AuthorizationMiddleware` or `PermissionCapabilityAuthorizationService`
- No modification of legacy `IDataPermissionFilter` / `DataPermissionFilter`
- No UI, no API, no database persistence, no cache invalidation
- No `CapabilityExecutionContext` changes for data permission context

---

## 2. Contract Extensions (Organization.Abstractions)

### 2.1 Enhanced `DataPermissionScope`

```csharp
public sealed class DataPermissionScope
{
    public DataPermissionScopeKind Kind { get; init; }
    public string? UserId { get; init; }
    public string? TenantId { get; init; }                    // NEW
    public string? Resource { get; init; }                    // NEW — entity name
    public string? Action { get; init; }                      // NEW — CRUD verb
    public string? Permission { get; init; }                  // NEW — RBAC permission
    public string? OrganizationUnitId { get; init; }
    public IReadOnlyList<string> OrganizationUnitIds { get; init; } = Array.Empty<string>();

    // Computed helpers (NEW)
    public bool IsEmpty => Kind == DataPermissionScopeKind.None;
    public bool IsUnrestricted => Kind == DataPermissionScopeKind.All;
}
```

New properties: `TenantId`, `Resource`, `Action`, `Permission` — populated during resolution for traceability. `IsEmpty` and `IsUnrestricted` are convenience computed properties.

### 2.2 `DataPermissionScopeKind` — Add `Custom`

```csharp
public enum DataPermissionScopeKind
{
    None = 0,
    Self = 1,
    OwnOrganization = 2,
    OwnOrganizationAndDescendants = 3,
    All = 4,
    Custom = 5    // NEW — reserved; builder returns IsDenied (fail-closed)
}
```

`Custom` is defined but no provider resolves it. `DefaultDataPermissionFilterBuilder` returns `IsDenied` for Custom and any unknown enum value.

### 2.3 `DataPermissionAction` Constants

```csharp
public static class DataPermissionAction
{
    public const string None   = nameof(None);
    public const string Read   = nameof(Read);
    public const string Create = nameof(Create);
    public const string Update = nameof(Update);
    public const string Delete = nameof(Delete);
    public const string Query  = nameof(Query);  // alias for Read — CRUD-compatible search/list operations
}
```

Static constants — consumers can use arbitrary strings. `Query` is a `Read` alias for search/list operations; rule stores treat them as distinct keys, so rule authors configure them consistently. No enum, AoT-friendly.

### 2.4 `DataPermissionScopeRequest`

```csharp
public sealed class DataPermissionScopeRequest
{
    public required string UserId { get; init; }
    public string? TenantId { get; init; }
    public string? Permission { get; init; }
    public string? Resource { get; init; }
    public string? Action { get; init; }
}
```

Replaces the old `(userId, permission, tenantId)` parameter list.

### 2.5 Extended `IDataPermissionScopeProvider`

```csharp
public interface IDataPermissionScopeProvider
{
    // NEW — primary resolution path
    Task<DataPermissionScope> GetScopeAsync(
        DataPermissionScopeRequest request,
        CancellationToken cancellationToken = default);

    // OLD — kept as compatibility adapter
    Task<DataPermissionScope> GetScopeAsync(
        string userId, string permission, string? tenantId = null,
        CancellationToken cancellationToken = default);
}
```

Old overload implemented as adapter: creates `DataPermissionScopeRequest`, delegates to new method.

### 2.6 `DataPermissionScopeRule`

```csharp
public sealed class DataPermissionScopeRule
{
    public required string Resource { get; init; }
    public string? Action { get; init; }
    public string? Permission { get; init; }
    public string? TenantId { get; init; }
    public DataPermissionScopeKind ScopeKind { get; init; }
}
```

### 2.7 `IDataPermissionScopeRuleStore`

```csharp
public interface IDataPermissionScopeRuleStore
{
    /// <summary>Returns the configured scope kind, or null if no rule matches (fall back to org-membership).</summary>
    Task<DataPermissionScopeKind?> GetScopeKindAsync(
        string resource,
        string? action,
        string? permission,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Upsert a scope rule. Keyed by (resource, action, permission, tenantId).</summary>
    Task SaveRuleAsync(
        DataPermissionScopeRule rule,
        CancellationToken cancellationToken = default);
}
```

Match priority (most specific first):
1. `("Book", "Read", "books.read", "tenant-A")` — tenant-exact
2. `("Book", "Read", "books.read", null)` — global-exact  
3. `("Book", "Read", "*", "tenant-A")` — tenant-wildcard-permission
4. `("Book", "Read", "*", null)` — global-wildcard-permission
5. `("Book", "*", "*", "tenant-A")` — tenant-wildcard-action
6. `("Book", "*", "*", null)` — global-wildcard-action
7. → `null` (no rule — fall back to org-membership scope)

---

## 3. ORM-neutral Filter Model (Organization.Abstractions)

### 3.1 `DataPermissionFilterOperator`

```csharp
public enum DataPermissionFilterOperator
{
    Equal,    // field == value
    In        // field IN [values]
}
```

### 3.2 `DataPermissionFilterRule`

```csharp
public sealed class DataPermissionFilterRule
{
    public required string FieldName { get; init; }
    public DataPermissionFilterOperator Operator { get; init; }
    public string? Value { get; init; }                          // for Equal
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();  // for In
}
```

### 3.3 `DataPermissionFilter`

```csharp
public sealed class DataPermissionFilter
{
    /// <summary>True when the filter denies all access (e.g. None scope).</summary>
    public bool IsDenied { get; init; }

    /// <summary>True when the filter applies no restrictions (e.g. All scope without tenant scoping).
    /// False when tenant scoping is added even under All scope.</summary>
    public bool IsUnrestricted { get; init; }

    /// <summary>AND-combined list of filter rules. Empty when IsDenied or IsUnrestricted.</summary>
    public IReadOnlyList<DataPermissionFilterRule> Rules { get; init; } = Array.Empty<DataPermissionFilterRule>();
}
```

`IsDenied` and `IsUnrestricted` are explicit bool properties — no sentinel `_always` or `True`/`False` operators needed.
- `None` → `IsDenied = true, IsUnrestricted = false, Rules = []`
- `All` (no tenant) → `IsDenied = false, IsUnrestricted = true, Rules = []`
- `All` (with tenant) → `IsDenied = false, IsUnrestricted = false, Rules = [(TenantId, Equal, val)]`
- All other scopes → `IsDenied = false, IsUnrestricted = false, Rules = [...]`

### 3.4 `DataPermissionFieldMapping`

```csharp
public sealed class DataPermissionFieldMapping
{
    public string? UserIdField { get; init; }                  // for Self scope
    public string? OrganizationUnitIdField { get; init; }      // for OwnOrganization / Descendants
    public string? TenantIdField { get; init; }                // optional tenant scoping

    public bool HasUserIdField => !string.IsNullOrEmpty(UserIdField);
    public bool HasOrganizationUnitIdField => !string.IsNullOrEmpty(OrganizationUnitIdField);
    public bool HasTenantIdField => !string.IsNullOrEmpty(TenantIdField);
}
```

Bridge between ORM-neutral filter and concrete entity. Example mappings:

| Entity | UserIdField | OrganizationUnitIdField | TenantIdField |
|--------|------------|------------------------|---------------|
| `Book` | `"CreatorId"` | `"OrganizationUnitId"` | `"TenantId"` |
| `Order` | `"CreatedByUserId"` | `null` | `"TenantId"` |
| `TenantConfig` | `null` | `null` | `null` |

### 3.5 `IDataPermissionFilterBuilder`

```csharp
public interface IDataPermissionFilterBuilder
{
    DataPermissionFilter Build(DataPermissionScope scope, DataPermissionFieldMapping mapping);
}
```

Synchronous — pure computation, no I/O.

---

## 4. Filter Builder: Fail-Closed Rules

### 4.1 `DefaultDataPermissionFilterBuilder`

Location: `framework/src/CrestCreates.Organization/DefaultDataPermissionFilterBuilder.cs`

### 4.2 Fail-Closed Matrix

| Scope Kind | Condition | IsDenied | IsUnrestricted | Rules Generated |
|------------|-----------|----------|---------------|----------------|
| `None` | — | `true` | `false` | `[]` |
| `All` | No `TenantIdField` or `scope.TenantId is null` | `false` | `true` | `[]` |
| `All` | `HasTenantIdField && scope.TenantId is not null` | `false` | `false` | `(TenantIdField, Equal, scope.TenantId)` |
| `Self` | `!HasUserIdField` | `true` | `false` | `[]` |
| `Self` | `HasUserIdField` | `false` | `false` | `(UserIdField, Equal, scope.UserId)` |
| `OwnOrganization` | `!HasOrganizationUnitIdField` or `scope.OrganizationUnitId is null` | `true` | `false` | `[]` |
| `OwnOrganization` | Both present | `false` | `false` | `(OrgUnitIdField, Equal, scope.OrganizationUnitId)` |
| `OwnOrganizationAndDescendants` | `!HasOrganizationUnitIdField` or `scope.OrganizationUnitIds.Count == 0` | `true` | `false` | `[]` |
| `OwnOrganizationAndDescendants` | All present | `false` | `false` | `(OrgUnitIdField, In, scope.OrganizationUnitIds)` |
| `Custom` / unknown | — | `true` | `false` | `[]` |

### 4.3 TenantId Scoping

**In addition** to scope-specific rules, when `mapping.HasTenantIdField && scope.TenantId is not null`:

| Rule | Operator | Value |
|------|----------|-------|
| `(TenantIdField, Equal, scope.TenantId)` | `Equal` | `scope.TenantId` |

Tenant scoping is additive — tightens the filter, never loosens it. Applied even to `All` scope.

### 4.4 No Expression Trees

The builder produces POCO data. `IsDenied`/`IsUnrestricted` are explicit booleans, not derived from sentinel rules. ORM providers consume `DataPermissionFilter` and generate native query constructs. Zero reflection, fully AoT-friendly.

---

## 5. Runtime Facade

### 5.1 `IDataPermissionRuntime`

```csharp
public interface IDataPermissionRuntime
{
    Task<DataPermissionScope> ResolveScopeAsync(
        DataPermissionScopeRequest request,
        CancellationToken cancellationToken = default);

    DataPermissionFilter BuildFilter(
        DataPermissionScope scope,
        DataPermissionFieldMapping mapping);
}
```

### 5.2 `DefaultDataPermissionRuntime`

```csharp
public sealed class DefaultDataPermissionRuntime : IDataPermissionRuntime
{
    private readonly IDataPermissionScopeProvider _scopeProvider;
    private readonly IDataPermissionFilterBuilder _filterBuilder;

    public DefaultDataPermissionRuntime(
        IDataPermissionScopeProvider scopeProvider,
        IDataPermissionFilterBuilder filterBuilder)
    {
        _scopeProvider = scopeProvider;
        _filterBuilder = filterBuilder;
    }

    public Task<DataPermissionScope> ResolveScopeAsync(DataPermissionScopeRequest request, CancellationToken ct)
        => _scopeProvider.GetScopeAsync(request, ct);

    public DataPermissionFilter BuildFilter(DataPermissionScope scope, DataPermissionFieldMapping mapping)
        => _filterBuilder.Build(scope, mapping);
}
```

Pure delegation — no additional logic.

---

## 6. Scope Provider Implementation

### 6.1 `DefaultDataPermissionScopeProvider` — Upgraded

New dependencies: `IOrganizationHierarchyService`, `IDataPermissionScopeRuleStore`.

**Resolution algorithm:**

1. If `request.Resource` is set, query `IDataPermissionScopeRuleStore` with `request.TenantId`:
   - If a `DataPermissionScopeKind` is returned, resolve by that kind.
   - If `null`, fall through to step 2.
2. Fall back to org-membership-based scope via `IOrganizationIdentityService.GetContextAsync()`:
   - No `PrimaryOrganizationUnitId` → `Self`
   - Has primary org → `OwnOrganization`
3. For `OwnOrganization` / `OwnOrganizationAndDescendants` resolved from rule store, still fetch identity context via `IOrganizationIdentityService`:
   - If user has no primary org → **fail closed** (return `None`)
   - For `OwnOrganizationAndDescendants`, additionally call `IOrganizationHierarchyService.GetDescendantsAsync` and combine with primary into `OrganizationUnitIds`

### 6.2 Old Overload Adapter

```csharp
public Task<DataPermissionScope> GetScopeAsync(
    string userId, string permission, string? tenantId, CancellationToken ct)
    => GetScopeAsync(new DataPermissionScopeRequest
    {
        UserId = userId, Permission = permission, TenantId = tenantId
    }, ct);
```

---

## 7. In-Memory Rule Store

### 7.1 `InMemoryDataPermissionScopeRuleStore`

Key format: `$"{resource}::{action ?? "*"}::{permission ?? "*"}::{tenantId ?? "*"}"`.

```csharp
public sealed class InMemoryDataPermissionScopeRuleStore : IDataPermissionScopeRuleStore
{
    private readonly ConcurrentDictionary<string, DataPermissionScopeKind> _rules = new();

    public Task<DataPermissionScopeKind?> GetScopeKindAsync(
        string resource, string? action, string? permission, string? tenantId, CancellationToken ct)
    {
        // Match priority: tenant-exact > global-exact > tenant-wildcard-perm > global-wildcard-perm
        //                > tenant-wildcard-action > global-wildcard-action
        var keys = new[]
        {
            $"{resource}::{action ?? "*"}::{permission ?? "*"}::{tenantId ?? "*"}",      // tenant exact
            $"{resource}::{action ?? "*"}::{permission ?? "*"}::*",                      // global exact
            $"{resource}::{action ?? "*"}::*::{tenantId ?? "*"}",                         // tenant wildcard perm
            $"{resource}::{action ?? "*"}::*::*",                                          // global wildcard perm
            $"{resource}::*::*::{tenantId ?? "*"}",                                        // tenant wildcard action
            $"{resource}::*::*::*",                                                        // global wildcard action
        };

        foreach (var key in keys)
        {
            if (_rules.TryGetValue(key, out var kind))
                return Task.FromResult<DataPermissionScopeKind?>(kind);
        }

        return Task.FromResult<DataPermissionScopeKind?>(null);
    }

    public Task SaveRuleAsync(DataPermissionScopeRule rule, CancellationToken ct = default)
    {
        var key = $"{rule.Resource}::{rule.Action ?? "*"}::{rule.Permission ?? "*"}::{rule.TenantId ?? "*"}";
        _rules[key] = rule.ScopeKind;
        return Task.CompletedTask;
    }
}
```

No DB persistence, no cache invalidation, no management API. Rules added programmatically at startup.

---

## 8. DI Registration

### 8.1 Extended `AddOrganizationKernel()`

```csharp
public static IServiceCollection AddOrganizationKernel(this IServiceCollection services)
{
    // Phase 5c
    services.TryAddSingleton<IOrganizationStore, InMemoryOrganizationStore>();
    services.TryAddScoped<IOrganizationHierarchyService, DefaultOrganizationHierarchyService>();
    services.TryAddScoped<IOrganizationIdentityService, DefaultOrganizationIdentityService>();
    services.TryAddSingleton<IOrganizationContextAccessor, NullOrganizationContextAccessor>();

    // Phase 5d
    services.TryAddScoped<IDataPermissionScopeProvider, DefaultDataPermissionScopeProvider>();

    // Phase 5e — NEW
    services.TryAddSingleton<IDataPermissionScopeRuleStore, InMemoryDataPermissionScopeRuleStore>();
    services.TryAddSingleton<IDataPermissionFilterBuilder, DefaultDataPermissionFilterBuilder>();
    services.TryAddScoped<IDataPermissionRuntime, DefaultDataPermissionRuntime>();

    return services;
}
```

### 8.2 Lifetime Table

| Service | Lifetime | Rationale |
|---------|----------|-----------|
| `IOrganizationStore` | Singleton | In-memory, thread-safe |
| `IOrganizationHierarchyService` | Scoped | Stateful traversal |
| `IOrganizationIdentityService` | Scoped | Stateful queries |
| `IOrganizationContextAccessor` | Singleton | Stateless null accessor |
| `IDataPermissionScopeProvider` | Scoped | Depends on scoped identity/hierarchy services |
| `IDataPermissionScopeRuleStore` | Singleton | `ConcurrentDictionary`, thread-safe |
| `IDataPermissionFilterBuilder` | Singleton | Stateless pure computation |
| `IDataPermissionRuntime` | Scoped | Delegates to scoped `IDataPermissionScopeProvider` |

All use `TryAdd*` — never overrides consumer custom registrations.

---

## 9. File Manifest

### 9.1 Abstractions — Modified

```
CrestCreates.Organization.Abstractions/
├── DataPermissionScope.cs              # MODIFIED: +TenantId, Resource, Action, Permission, IsEmpty, IsUnrestricted
├── DataPermissionScopeKind.cs          # MODIFIED: +Custom value
├── IDataPermissionScopeProvider.cs     # MODIFIED: +GetScopeAsync(Request) overload
├── DataPermissionAction.cs             # NEW: static constants
├── DataPermissionScopeRequest.cs       # NEW: input model
├── DataPermissionScopeRule.cs          # NEW: rule model for store
├── IDataPermissionScopeRuleStore.cs    # NEW: interface (tenant-aware + SaveRuleAsync)
├── DataPermissionFilterOperator.cs     # NEW: enum (Equal, In)
├── DataPermissionFilterRule.cs         # NEW: rule model
├── DataPermissionFilter.cs             # NEW: filter model (explicit IsDenied/IsUnrestricted)
├── DataPermissionFieldMapping.cs       # NEW: field mapping model
├── IDataPermissionFilterBuilder.cs     # NEW: interface
├── IDataPermissionRuntime.cs           # NEW: interface
```

### 9.2 Implementation — Modified / New

```
CrestCreates.Organization/
├── DefaultDataPermissionScopeProvider.cs    # MODIFIED: full resolution with hierarchy + rule store
├── DefaultDataPermissionFilterBuilder.cs    # NEW: fail-closed builder
├── DefaultDataPermissionRuntime.cs          # NEW: facade implementation
├── InMemoryDataPermissionScopeRuleStore.cs  # NEW: rule store
├── OrganizationServiceCollectionExtensions.cs  # MODIFIED: +3 new registrations
```

### 9.3 Tests — Modified / New

```
CrestCreates.Organization.Tests/
├── DataPermissionScopeProviderTests.cs      # MODIFIED: 14 tests (was 2)
├── DataPermissionFilterBuilderTests.cs      # NEW: 13 tests
├── DataPermissionRuntimeTests.cs            # NEW: 3 tests
├── InMemoryDataPermissionScopeRuleStoreTests.cs  # NEW: 7 tests
```

---

## 10. Test Plan

### 10.1 Scope Provider Tests (D1–D14)

| # | Test | Key Assertion |
|---|------|---------------|
| D1 | No org → Self | `Kind = Self`, `UserId = "user-1"` |
| D2 | Primary org → OwnOrganization | `Kind = OwnOrganization`, `OrganizationUnitId = "dept-1"` |
| D3 | Rule → OwnOrganizationAndDescendants with hierarchy | `Kind = OwnOrganizationAndDescendants`, `OrganizationUnitIds` includes primary + descendants |
| D4 | Rule → All | `Kind = All`, `IsUnrestricted = true`, no org IDs |
| D5 | Rule → None | `Kind = None`, `IsEmpty = true` |
| D6 | Rule → OwnOrganization, no primary org → fail closed | `Kind = None` |
| D7 | Rule → OwnOrganizationAndDescendants, no primary org → fail closed | `Kind = None` |
| D8 | No rule, has org → fallback OwnOrganization | `Kind = OwnOrganization` (org-membership fallback) |
| D9 | No rule, no org → fallback Self | `Kind = Self` (fallback) |
| D10 | Rule overrides org membership | Rule says `All`, user has org → `Kind = All` (rule wins) |
| D11 | Tenant isolation in scope resolution | Org membership is tenant-aware; different tenant → no org |
| D12 | Old overload delegates to new | Same result from both overloads |
| D13 | Tenant-specific rule overrides global rule | Global rule `Self`, tenant rule `All` → query with matching tenant returns `All` |
| D14 | Other tenant rule does not apply | Tenant "t-B" queries with only "t-A" rules → falls back to org-membership

### 10.2 Filter Builder Tests (F1–F13)

| # | Test | Key Assertion |
|---|------|---------------|
| F1 | None → IsDenied | `IsDenied = true`, `IsUnrestricted = false`, `Rules` empty |
| F2 | All (no tenant) → IsUnrestricted | `IsUnrestricted = true`, `IsDenied = false`, `Rules` empty |
| F3 | Self + UserIdField → Equal rule | `(UserIdField, Equal, scope.UserId)`, `IsDenied = false`, `IsUnrestricted = false` |
| F4 | Self without UserIdField → IsDenied | `IsDenied = true` (fail closed) |
| F5 | OwnOrganization → Equal rule | `(OrgUnitIdField, Equal, scope.OrganizationUnitId)` |
| F6 | OwnOrganization without OrgField → IsDenied | `IsDenied = true` (fail closed) |
| F7 | OwnOrganizationAndDescendants → In rule | `(OrgUnitIdField, In, [primary, ...descendants])` |
| F8 | OwnOrganizationAndDescendants without OrgField → IsDenied | `IsDenied = true` (fail closed) |
| F9 | TenantIdField + non-null TenantId → 2 rules | Scope rule + `(TenantIdField, Equal, scope.TenantId)` |
| F10 | TenantIdField + null TenantId → 1 rule | Tenant rule skipped |
| F11 | No TenantIdField → 1 rule | Tenant rule skipped even if `scope.TenantId` non-null |
| F12 | All scope + TenantIdField → tenant-scoped (not unrestricted) | `IsUnrestricted = false`, 1 rule `(TenantIdField, Equal, val)` |
| F13 | Custom scope → IsDenied | `IsDenied = true` (unknown → fail closed) |

### 10.3 Runtime Tests (R1–R3)

| # | Test | Key Assertion |
|---|------|---------------|
| R1 | ResolveScopeAsync delegates correctly | Same result as calling `ScopeProvider` directly |
| R2 | BuildFilter delegates correctly | Same result as calling `FilterBuilder` directly |
| R3 | End-to-end: resolve then build | `ResolveScopeAsync → scope → BuildFilter → expected filter with IsDenied false` |

### 10.4 Rule Store Tests (S1–S7)

| # | Test | Key Assertion |
|---|------|---------------|
| S1 | Exact match | `("Book", "Read", "perm", "t-A")` → configured kind |
| S2 | Wildcard permission fallback | `("Book", "Read", "any", "t-A")` matches `("Book", "Read", "*", "t-A")` |
| S3 | Wildcard action+permission fallback | `("Book", "Write", "any", "t-A")` matches `("Book", "*", "*", "t-A")` |
| S4 | No rule → null | No rules configured → `null` (fallback signal) |
| S5 | Most specific rule wins | `("Book", "Read", "*", "t-A")` overrides `("Book", "*", "*", "t-A")` |
| S6 | Tenant rule overrides global rule | Global `("Book", "Read", "*") → Self`, tenant `("Book", "Read", "*", "t-A") → All` → query for "t-A" returns `All` |
| S7 | Other tenant rule does not apply | Tenant "t-B" queries `("Book", "Read", "p")` with only "t-A" rules → `null` |

### 10.5 Regression

All existing test suites must pass unchanged:
- `CrestCreates.Organization.Tests` — 42 existing + ~37 new = ~79 tests
- `CrestCreates.Capability.Tests` — 117 tests, zero changes
- `CrestCreates.Authorization.Abstractions` — zero changes
- `CrestCreates.Infrastructure` — no changes to legacy `DataPermissionFilter`

---

## 11. What is NOT Changed

- `AuthorizationMiddleware` — no data permission logic added
- `PermissionCapabilityAuthorizationService` — no changes
- `CapabilityExecutionContext` — no data permission fields (deferred)
- Legacy `IDataPermissionFilter` / `DataPermissionFilter` — untouched
- `CrestAppServiceBase` — remains on legacy filter chain
- `ICurrentUser.DataScopeValue` / `ICurrentUser.OrganizationId` — untouched
- `DataPermission` domain entity / `DataScope` enum — untouched
- `IMayHaveOrganization` / `IHasCreator` / `IMustHaveTenant` — untouched

---

## 12. References

- Phase 5c Design: `docs/superpowers/specs/2026-06-11-phase-5c-organization-identity-kernel-design.md`
- Phase 5d Design: `docs/superpowers/specs/2026-06-11-phase-5d-capability-authorization-bridge-design.md`
- Organization Architecture Summary: `docs/Feature/Organization/arch-design.md`
- Organization Abstractions: `framework/src/CrestCreates.Organization.Abstractions/`
- Organization Implementation: `framework/src/CrestCreates.Organization/`
- Organization Tests: `framework/test/CrestCreates.Organization.Tests/`
- Legacy DataPermissionFilter: `framework/src/CrestCreates.Infrastructure/DataFilter/DataPermissionFilter.cs`
- memory.md: Platform status record
