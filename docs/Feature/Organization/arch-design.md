# Organization Identity Kernel — Architecture Summary

> **Date:** 2026-06-12 | **Status:** Complete | **Phases 5c/5d/5e: Organization Identity Kernel + Authorization Bridge + Data Permission Runtime**

---

## 1. Design Goals

Establish the minimum Organization Identity Kernel to serve as foundation for:
- HumanTask assignee resolution (future phase)
- Capability Authorization (Phase 5d — complete)
- Data Permission Runtime (Phase 5e — complete)

The kernel answers 6 questions:

| Question | Service |
|----------|---------|
| Which org units does a user belong to? | `IOrganizationIdentityService` |
| What is the parent-child hierarchy? | `IOrganizationHierarchyService` |
| Is a user in a given org unit or its descendants? | `IOrganizationHierarchyService` |
| Does a user hold a given position/role? | `IOrganizationIdentityService` |
| What is the current user's organization context? | `IOrganizationContextAccessor` |
| What data scope should apply for a given resource? | `IDataPermissionRuntime` |

---

## 2. Project Structure

Three projects following the existing HumanTask/Workflow conventions:

```
framework/src/CrestCreates.Organization.Abstractions/   # 23 files — pure models + interfaces
framework/src/CrestCreates.Organization/                  # 10 files — InMemory store + services + DI
framework/test/CrestCreates.Organization.Tests/           # 8 files — 79 tests
```

**Dependencies**: Organization.Abstractions depends on nothing. Organization references only Abstractions + `Microsoft.Extensions.DependencyInjection.Abstractions`. No dependency on Workflow, HumanTask, Capability, or ASP.NET Core.

---

## 3. Core Models (Abstractions)

### 3.1 OrganizationUnit

```csharp
public sealed class OrganizationUnit
{
    public string Id { get; init; }
    public string? TenantId { get; init; }
    public string Name { get; init; }
    public string? Code { get; init; }
    public string? ParentId { get; init; }    // self-referencing tree
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; }
}
```

### 3.2 Position

```
Id | TenantId | Name | Code | IsActive | CreatedAt
```

Flat lookup table. No hierarchy.

### 3.3 UserOrganizationMembership

```
Id | TenantId | UserId | OrganizationUnitId | PositionId? | IsPrimary | IsActive | CreatedAt
```

Bridges user ↔ org unit. Carries optional `PositionId`. `IsPrimary` marks the user's primary org.

### 3.4 UserOrganizationRoleAssignment

```
Id | TenantId | UserId | RoleId | OrganizationUnitId? | IsActive | CreatedAt
```

Organization-scoped role context. Does NOT participate in the framework's RBAC chain (`IPermissionChecker`, claims, tokens). `RoleId` is a string — no `Role` entity in this phase.

---

## 4. Context Model

### OrganizationContext

```csharp
public sealed class OrganizationContext
{
    public string? TenantId { get; init; }
    public string UserId { get; init; }
    public string? PrimaryOrganizationUnitId { get; init; }
    public IReadOnlyList<string> OrganizationUnitIds { get; init; }
    public IReadOnlyList<string> RoleIds { get; init; }
    public IReadOnlyList<string> PositionIds { get; init; }
}
```

All ID lists are deduplicated. `PrimaryOrganizationUnitId` = first `IsPrimary == true` membership sorted by `CreatedAt`.

### IOrganizationContextAccessor

```csharp
public interface IOrganizationContextAccessor
{
    OrganizationContext? Current { get; }
}
```

Default: `NullOrganizationContextAccessor` (returns null). No HTTP context coupling.

---

## 5. Store Layer

### IOrganizationStore (11 methods)

| Entity | Save (upsert) | GetById | Query (by tenant) |
|--------|--------------|---------|-------------------|
| OrganizationUnit | ✓ | ✓ (composite key) | ✓ |
| Position | ✓ | ✓ (composite key) | ✓ |
| UserOrganizationMembership | ✓ | — | By user, by org unit |
| UserOrganizationRoleAssignment | ✓ | — | By user |

### InMemoryOrganizationStore

| Concern | Implementation |
|--------|----------------|
| **Storage** | One `ConcurrentDictionary<string, T>` per entity type |
| **Key** | Composite: `$"{tenantId ?? ""}:{id}"` — same ID in different tenants are distinct entries |
| **Upsert** | Last-Write-Wins. No CAS (models lack `ConcurrencyStamp`). |
| **Read** | `TryGetValue` → return `Clone()` snapshot. Never return dictionary reference. |
| **Query** | `.Values.Where(...).Select(x => x.Clone()).ToList().AsReadOnly()` |
| **Clone()** | Manual field-by-field copy on each model. No reflection, no JSON. |

Store returns raw data. Service filters `IsActive`.

---

## 6. Hierarchy Service

### IOrganizationHierarchyService

| Method | Algorithm | Cycle Detection |
|--------|----------|-----------------|
| `GetAncestorsAsync(orgUnitId, tenantId?)` | Trace ParentId upward | Yes — visited set. Throws `OrganizationHierarchyException`. |
| `GetDescendantsAsync(orgUnitId, tenantId?)` | BFS scan | Yes — visited set. |
| `IsDescendantOfAsync(orgUnitId, ancestorId, tenantId?)` | Trace upward for ancestor | Yes. |
| `IsUserInOrganizationAsync(userId, orgUnitId, tenantId?)` | Active membership check | N/A |
| `IsUserInDescendantOrganizationAsync(userId, ancestorId, tenantId?)` | Descendants + membership check | Inherited |

### Key Design: Tenant-Aware Graph Keys

All hierarchy traversal is scoped to `tenantId`. The unit map uses composite keys (`$"{tenantId}:{id}"`). This prevents:
- `ToDictionary` collisions when same-ID org units exist across tenants
- Cross-tenant hierarchy mixing
- Cross-tenant parent resolution (parent in different tenant → treated as no parent)

---

## 7. Identity Service

### IOrganizationIdentityService

| Method | Behavior |
|--------|----------|
| `GetContextAsync(userId, tenantId?)` | Returns full `OrganizationContext` with deduplicated active-only IDs |
| `IsInRoleAsync(userId, roleId, tenantId?)` | Checks active `UserOrganizationRoleAssignment` |
| `HasPositionAsync(userId, positionId, tenantId?)` | Checks active membership with matching `PositionId` |
| `GetUserOrganizationUnitIdsAsync(userId, tenantId?)` | Distinct active org unit IDs |
| `GetUserRoleIdsAsync(userId, tenantId?)` | Distinct active role IDs |
| `GetUserPositionIdsAsync(userId, tenantId?)` | Distinct active position IDs |

All filtering: active-only (`IsActive == true`). Primary selection: first `IsPrimary` by `CreatedAt`.

---

## 8. Data Permission Runtime (Phase 5e)

### 8.1 Scope Resolution

```
DataPermissionScopeRequest (UserId, TenantId, Resource, Action, Permission)
        │
        ▼
IDataPermissionScopeRuleStore ──┐  (tenant-aware: 6-priority matching)
IDataPermissionScopeProvider ───┤  (rule store > org-membership fallback)
        │
        ▼
DataPermissionScope (Kind, UserId, TenantId, OrgUnitIds, ...)
        │
        ▼
IDataPermissionFilterBuilder ────→ DataPermissionFilter (IsDenied, IsUnrestricted, Rules[])
```

**Scope kinds**: `None | Self | OwnOrganization | OwnOrganizationAndDescendants | All | Custom` (Custom → fail closed).

**Resolution priority**:
1. If `request.Resource` is set, query `IDataPermissionScopeRuleStore` with tenantId. Tenant-specific rules override global rules.
2. Fall back to org-membership via `IOrganizationIdentityService.GetContextAsync()`:
   - No `PrimaryOrganizationUnitId` → `Self`
   - Has primary org → `OwnOrganization`
3. For rule-resolved `OwnOrganization`/`OwnOrganizationAndDescendants` without primary org → **fail closed** (return `None`).
4. `Custom` scope kind → **fail closed** at provider level (return `None`).

### 8.2 Rule Store

`InMemoryDataPermissionScopeRuleStore` — `ConcurrentDictionary`-based, tenant-aware.

Match priority (all tenant rules before all global):
1. tenant-exact → tenant-wildcard-permission → tenant-wildcard-action
2. global-exact → global-wildcard-permission → global-wildcard-action

Rules are configured via `SaveRuleAsync(DataPermissionScopeRule)` on the interface — no implementation-only `AddRule()` leak.

### 8.3 Filter Builder

`DefaultDataPermissionFilterBuilder` — stateless, fail-closed:

| Scope | Missing Mapping | Result |
|-------|----------------|--------|
| `None` | — | `IsDenied = true` |
| `Custom` / unknown | — | `IsDenied = true` |
| `All` (no tenant) | — | `IsUnrestricted = true` |
| `All` (+ tenant) | — | `IsUnrestricted = false`, `Rules = [(TenantId, Equal, val)]` |
| `Self` | `!HasUserIdField` | `IsDenied = true` |
| `OwnOrganization` | `!HasOrganizationUnitIdField` or `OrganizationUnitId is null` | `IsDenied = true` |
| `OwnOrganizationAndDescendants` | `!HasOrganizationUnitIdField` or `OrganizationUnitIds.Count == 0` | `IsDenied = true` |

Tenant scoping is additive — appended to all non-denied filters when `HasTenantIdField && scope.TenantId is not null`.

### 8.4 ORM-neutral Filter Model

`DataPermissionFilter` uses explicit `IsDenied`/`IsUnrestricted` bools (no sentinel convention). Rules use `DataPermissionFilterOperator.Equal`/`In`. `DataPermissionFieldMapping` bridges entity fields (`UserIdField`, `OrganizationUnitIdField`, `TenantIdField`).

### 8.5 Runtime Facade

`IDataPermissionRuntime` composes scope resolution + filter building:
- `ResolveScopeAsync(DataPermissionScopeRequest)` → `DataPermissionScope`
- `BuildFilter(DataPermissionScope, DataPermissionFieldMapping)` → `DataPermissionFilter`

---

## 9. DI Registration

```csharp
services.AddOrganizationKernel();
// Registers:
//   IOrganizationStore              → InMemoryOrganizationStore             (Singleton)
//   IOrganizationHierarchyService   → DefaultOrganizationHierarchyService   (Scoped)
//   IOrganizationIdentityService    → DefaultOrganizationIdentityService    (Scoped)
//   IDataPermissionScopeProvider    → DefaultDataPermissionScopeProvider    (Scoped)
//   IDataPermissionScopeRuleStore   → InMemoryDataPermissionScopeRuleStore  (Singleton)
//   IDataPermissionFilterBuilder    → DefaultDataPermissionFilterBuilder    (Singleton)
//   IDataPermissionRuntime          → DefaultDataPermissionRuntime          (Scoped)
//   IOrganizationContextAccessor    → NullOrganizationContextAccessor       (Singleton)
```

All use `TryAdd*` — won't override consumer custom registrations.

---

## 10. Exceptions

```
Exception
└── OrganizationException
    └── OrganizationHierarchyException    (cycle detection, hierarchy resolution failure)
```

Plain `Exception` base (no `CrestException` — no HTTP layer in this phase).

---

## 11. Tests (79 tests, 0 failures)

| Test File | Tests | Coverage |
|-----------|-------|----------|
| `InMemoryOrganizationStoreTests` | 11 | Upsert, get-by-id, get-by-tenant, membership query, role assignment, position CRUD |
| `OrganizationHierarchyServiceTests` | 16 | Ancestors, descendants, IsDescendantOf, cycle detection, cross-tenant isolation |
| `OrganizationIdentityServiceTests` | 13 | GetContext, dedup, primary selection, inactive exclusion, IsInRole, HasPosition |
| `DataPermissionScopeProviderTests` | 15 | Self/OwnOrganization/OwnOrgAndDescendants/All/None/Custom resolutions, rule overrides, tenant isolation, fail-closed, old overload adapter |
| `DataPermissionFilterBuilderTests` | 13 | None/All/Self/OwnOrg/Descendants fail-closed rules, tenant scoping, Custom→Denied, missing mapping→Denied |
| `InMemoryDataPermissionScopeRuleStoreTests` | 8 | Exact/wildcard matching, tenant-priority ordering, tenant-wildcard-overrides-global-exact |
| `DataPermissionRuntimeTests` | 3 | ResolveScopeAsync delegation, BuildFilter delegation, end-to-end resolve-then-build |

---

## 12. Design Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | No `ConcurrencyStamp` | InMemory-only. Database providers add their own. |
| 2 | LW upsert | Without ConcurrencyStamp, CAS not possible. Sufficient for InMemory. |
| 3 | `Clone()` in Abstractions on models | Follows HumanTask/Workflow pattern. |
| 4 | Plain `Exception` base | No HTTP layer, no middleware integration needed. |
| 5 | No `[CrestModule]` | Consumers call `AddOrganizationKernel()` directly. |
| 6 | Store raw data, Service filters active | Single responsibility. |
| 7 | Primary org: stable sort by `CreatedAt` | Defers strict validation. |
| 8 | `UserOrganizationRoleAssignment` naming | Signals org-scoped role, NOT a second auth truth source. |
| 9 | Composite store keys `(tenantId, id)` | Same ID in different tenants are distinct. |
| 10 | Tenant-aware hierarchy graph keys | Prevents `ToDictionary` collisions and cross-tenant mixing. |
| 11 | All entities use composite keys | Consistent; Membership/RoleAssignment included after review. |

---

## 13. Explicit Non-Goals

No AppService, Controller, Minimal API, UI, EF Core, SqlSugar, Dapper, MongoDB, Redis, file persistence, database migration, UnitOfWork, Outbox, distributed transactions, cache invalidation, i18n resources, RBAC admin backend, Permission tree, DataPermissionFilter LINQ/SQL, HumanTask Claim/Delegate/Escalation/SLA, Workflow Branch/Transition/Retry/Compensation.

No dependency on ASP.NET Core HttpContext, Workflow, HumanTask, or Capability projects.

---

## 14. Capability Authorization Bridge (Phase 5d)

The Capability Authorization Bridge connects Capability Runtime to the existing `IPermissionChecker` RBAC main chain.

### 14.1 How It Works

```
CapabilityDescriptor.Permissions      (string[])
        ↓
CapabilityExecutionContext.RequiredPermissions
        ↓
AuthorizationMiddleware
        ↓
PermissionCapabilityAuthorizationService
        ↓
IPermissionChecker.IsGrantedAsync()   (existing RBAC chain)
```

- `CapabilityDescriptor.Permissions` declares which permissions a capability requires.
- `CapabilityPipeline.ExecuteAsync` copies those into `context.RequiredPermissions` **after** `configureContext` (bypass-proof — caller cannot clear).
- `AuthorizationMiddleware` calls `ICapabilityAuthorizationService.AuthorizeAsync()` with the required permissions.
- `PermissionCapabilityAuthorizationService` delegates to the existing `IPermissionChecker`:
  - Empty permissions → allow (no checker needed).
  - Non-empty → `IsGrantedAsync(string[])` with `AllGranted` semantics.
- `ICapabilityPipeline` and `ICapabilityDispatcher` are now Scoped (were Singleton), fixing captive dependencies on scoped `ITenantContext`/`ICurrentUser`.

### 14.2 Explicit Boundary: Organization Role Does NOT Participate

`UserOrganizationRoleAssignment` (Phase 5c) stores organization-scoped role context (e.g., "Department Manager"). It does NOT flow into `IPermissionChecker` or the Capability Authorization Bridge. The framework's RBAC truth source remains `IPermissionChecker` and claims. Organization roles are identity facts for workflow routing and future data-permission filtering — not for API access control.

### 14.3 Key Files

| File | Role |
|------|------|
| `CrestCreates.Capability/PermissionCapabilityAuthorizationService.cs` | Default auth implementation |
| `CrestCreates.Capability.Abstractions/ICapabilityAuthorizationService.cs` | Interface (accepts `requiredPermissions`) |
| `CrestCreates.Capability.Abstractions/CapabilityExecutionContext.cs` | Carries `RequiredPermissions` |
| `CrestCreates.Capability/Middleware/AuthorizationMiddleware.cs` | Pipeline middleware (requires `ICapabilityAuthorizationService`) |
| `CrestCreates.Capability/CapabilityPipeline.cs` | Populates `RequiredPermissions` from descriptor |
| `CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs` | Registers default auth service as Scoped |

### 14.4 Tests

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/
# 117 tests, 0 failures (includes 13 auth bridge tests)
```

Key test scenarios: empty permissions allow (even without `IPermissionChecker`), all-granted passes, any-denied returns UNAUTHORIZED, configureContext cannot clear permissions, missing `IPermissionChecker` causes failure (not silent skip).

---

## 15. Data Permission Runtime Foundation (Phase 5e)

The Data Permission Runtime Foundation establishes an organization-identity-driven data permission system that resolves `DataPermissionScope` from org identity + rule store, and converts it to an ORM-neutral `DataPermissionFilter`.

### 15.1 How It Works

```
DataPermissionScopeRequest (UserId, TenantId, Resource, Action, Permission)
        │
        ▼
IDataPermissionScopeRuleStore.GetScopeKindAsync(resource, action, permission, tenantId)
        │
        ├─ Rule found → ResolveByKindAsync(kind, request)
        │     ├─ Self/All → return scope without org context
        │     ├─ None/Custom → return Denied scope
        │     └─ OwnOrganization/OwnOrganizationAndDescendants
        │           ├─ No primary org → fail closed (None)
        │           └─ Has primary org → fetch descendants, build scope
        │
        └─ No rule → fall back to org-membership
              ├─ No primary org → Self
              └─ Has primary org → OwnOrganization
                      │
                      ▼
              DataPermissionScope
                      │
                      ▼
              IDataPermissionFilterBuilder.Build(scope, mapping)
                      │
                      ▼
              DataPermissionFilter (IsDenied, IsUnrestricted, Rules[])
```

- Rule store is tenant-aware: tenant-specific rules override global rules.
- Filter builder is fail-closed: missing field mappings, Custom scope, unknown enum → deny.
- `All` + `TenantIdField` → tenant-scoped (not unrestricted).
- Old `IDataPermissionFilter` in Infrastructure is untouched — Phase 5e builds the new chain alongside it.
- No changes to `AuthorizationMiddleware` or `PermissionCapabilityAuthorizationService`.

### 15.2 Key Files

| File | Role |
|------|------|
| `CrestCreates.Organization.Abstractions/DataPermissionFilter.cs` | Filter result model (explicit IsDenied/IsUnrestricted) |
| `CrestCreates.Organization.Abstractions/DataPermissionFilterRule.cs` | Single rule (FieldName, Operator, Value/Values) |
| `CrestCreates.Organization.Abstractions/DataPermissionFilterOperator.cs` | Enum: Equal, In |
| `CrestCreates.Organization.Abstractions/DataPermissionFieldMapping.cs` | Entity field bridge (UserIdField, OrgUnitIdField, TenantIdField) |
| `CrestCreates.Organization.Abstractions/IDataPermissionFilterBuilder.cs` | Build filter from scope + mapping |
| `CrestCreates.Organization.Abstractions/IDataPermissionRuntime.cs` | Composes scope resolution + filter building |
| `CrestCreates.Organization.Abstractions/DataPermissionScopeRequest.cs` | Input model for scope resolution |
| `CrestCreates.Organization.Abstractions/IDataPermissionScopeRuleStore.cs` | Tenant-aware rule store interface |
| `CrestCreates.Organization.Abstractions/DataPermissionScopeRule.cs` | Rule model (Resource, Action, Permission, TenantId, ScopeKind) |
| `CrestCreates.Organization.Abstractions/DataPermissionAction.cs` | Static constants: Read/Create/Update/Delete/Query |
| `CrestCreates.Organization/DefaultDataPermissionFilterBuilder.cs` | Fail-closed builder implementation |
| `CrestCreates.Organization/DefaultDataPermissionRuntime.cs` | Runtime facade |
| `CrestCreates.Organization/InMemoryDataPermissionScopeRuleStore.cs` | Tenant-aware rule store (6-priority match) |

### 15.3 Tests

```bash
dotnet test framework/test/CrestCreates.Organization.Tests/
# 79 tests, 0 failures (includes 37 data permission tests)
```

Key test scenarios: scope resolution (rule store > org-membership), fail-closed (Custom→None, missing mapping→Denied, no primary org→None), tenant isolation (tenant-rule-overrides-global, cross-tenant rule does not apply), filter builder (All+TenantId→tenant-scoped, explicit IsDenied/IsUnrestricted), runtime delegation.

---

## 16. References

- Design spec (Phase 5c): `docs/superpowers/specs/2026-06-11-phase-5c-organization-identity-kernel-design.md`
- Implementation plan (Phase 5c): `docs/superpowers/plans/2026-06-11-phase-5c-organization-identity-kernel.md`
- Design spec (Phase 5d): `docs/superpowers/specs/2026-06-11-phase-5d-capability-authorization-bridge-design.md`
- Implementation plan (Phase 5d): `docs/superpowers/plans/2026-06-11-phase-5d-capability-authorization-bridge.md`
- Design spec (Phase 5e): `docs/superpowers/specs/2026-06-11-phase-5e-data-permission-runtime-foundation-design.md`
- Implementation plan (Phase 5e): `docs/superpowers/plans/2026-06-11-phase-5e-data-permission-runtime-foundation.md`
- InMemory store reference: `framework/src/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs`
- DI registration reference: `framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs`
