# Phase 5c — Organization Identity Kernel Design

**Date**: 2026-06-11
**Status**: Design approved, awaiting implementation plan
**Predecessor**: Phase 5b — Durable Runtime Store Contracts

## 1. Overview

### 1.1 Goal

Establish the minimum Organization Identity Kernel to serve as foundation for:
- HumanTask assignee resolution (future phase)
- Capability Authorization (future phase)
- DataPermissionFilter by org-unit hierarchy (future phase)

This phase delivers **only** models, store interfaces, InMemory implementation, hierarchy queries, identity queries, and a data-permission scope stub. No HTTP API, no database, no full RBAC.

### 1.2 What the Kernel Answers

- Which organization units does a user belong to?
- What is the parent-child hierarchy of organization units?
- Is a user in a given org unit or any of its descendants?
- Does a user hold a given position or role?
- What is the current user's organization context?

### 1.3 What the Kernel Does NOT Answer

- HTTP API for organization management
- Frontend org-tree maintenance
- Permission menu configuration
- Data-permission SQL generation
- Workflow automatic assignee routing
- HumanTask Claim/Delegate/Escalation/SLA
- Multi-tenant cross-database org sync
- Database persistence (EF Core, SqlSugar, Dapper, MongoDB, Redis)
- Cache invalidation
- Complete RBAC admin backend
- LINQ/SQL implementation of DataPermissionFilter

### 1.4 Design Principles

1. **Store = data access only**. No business validation.
2. **Service = query logic**. Active filtering, hierarchy traversal.
3. **InMemory only**. No database implementation in this phase.
4. **NativeAOT-friendly**. No runtime reflection, no dynamic proxies.
5. **No ORM, no expression scripting engine**.
6. **Does not modify Workflow / HumanTask / Capability projects**.
7. **Does not depend on ASP.NET Core HttpContext**.

---

## 2. Architecture

### 2.1 Project Structure

Three new projects following the existing HumanTask/Workflow conventions:

| Project | Path | Dependencies |
|---------|------|-------------|
| `CrestCreates.Organization.Abstractions` | `framework/src/CrestCreates.Organization.Abstractions/` | **Zero project references**. Pure models + interfaces. |
| `CrestCreates.Organization` | `framework/src/CrestCreates.Organization/` | References `Organization.Abstractions` + `Metadata.Abstractions` (for `RuntimeStoreException` in tests). |
| `CrestCreates.Organization.Tests` | `framework/test/CrestCreates.Organization.Tests/` | References `Organization` + `TestBase` + xUnit/FluentAssertions/Moq |

### 2.2 .csproj Conventions

Follow the 14-line minimal `.csproj` pattern from `CrestCreates.HumanTask.Abstractions`:
- `<TargetFramework>net10.0</TargetFramework>`
- `<ImplicitUsings>enable</ImplicitUsings>`
- `<Nullable>enable</Nullable>`
- `<RootNamespace>CrestCreates.Organization.Abstractions</RootNamespace>`

Tests follow `HumanTask.Tests` conventions (`ImplicitUsings>enable`, `IsTestProject` NOT needed, no `coverlet.collector`).

### 2.3 .slnx Additions

```xml
<!-- In <Folder Name="/src/core/">, alphabetically near sibling projects -->
<Project Path="framework/src/CrestCreates.Organization.Abstractions/CrestCreates.Organization.Abstractions.csproj" />
<Project Path="framework/src/CrestCreates.Organization/CrestCreates.Organization.csproj" />

<!-- In <Folder Name="/src/test/"> -->
<Project Path="framework/test/CrestCreates.Organization.Tests/CrestCreates.Organization.Tests.csproj" />
```

### 2.4 Naming Note: Existing `Organization` (Data Scope) vs. New `OrganizationUnit` (HR Org Chart)

The codebase already contains `CrestCreates.Domain.Permission.Organization` — an entity for **data partitioning** under a tenant (used by `DataPermissionFilter` for `WHERE OrganizationId IN (...)`). This is a fundamentally different concept from Phase 5c's `OrganizationUnit` which models **HR organizational hierarchy** (departments, teams, divisions).

**Decision**: Keep the Phase 5c spec naming as-is (`OrganizationUnit`, `IOrganizationStore`, `IOrganizationHierarchyService`). No compiler-level conflict — they live in different namespaces (`CrestCreates.Organization.Abstractions` vs. `CrestCreates.Domain.Permission`). No changes to the existing `Organization` entity or `DataPermissionFilter`.

---

## 3. Core Models

All models live in `CrestCreates.Organization.Abstractions`. All are `sealed class` with `{ get; init; }` properties (immutable record style, NativeAOT-friendly).

### 3.1 OrganizationUnit

```csharp
public sealed class OrganizationUnit
{
    public string Id { get; init; } = default!;
    public string? TenantId { get; init; }
    public string Name { get; init; } = default!;
    public string? Code { get; init; }
    public string? ParentId { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

- `ParentId` is self-referencing — builds the tree hierarchy.
- No `ConcurrencyStamp` (InMemory store uses Last-Write-Wins upsert; database providers will add their own).

### 3.2 Position

```csharp
public sealed class Position
{
    public string Id { get; init; } = default!;
    public string? TenantId { get; init; }
    public string Name { get; init; } = default!;
    public string? Code { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

- Flat lookup table. No hierarchy, no parent reference.

### 3.3 UserOrganizationMembership

```csharp
public sealed class UserOrganizationMembership
{
    public string Id { get; init; } = default!;
    public string? TenantId { get; init; }
    public string UserId { get; init; } = default!;
    public string OrganizationUnitId { get; init; } = default!;
    public string? PositionId { get; init; }
    public bool IsPrimary { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

- `IsPrimary` marks the user's primary org unit (used by `OrganizationContext.PrimaryOrganizationUnitId`).
- `PositionId` is optional — users can belong to an org unit without a position.

### 3.4 UserOrganizationRoleAssignment

```csharp
public sealed class UserOrganizationRoleAssignment
{
    public string Id { get; init; } = default!;
    public string? TenantId { get; init; }
    public string UserId { get; init; } = default!;
    public string RoleId { get; init; } = default!;
    public string? OrganizationUnitId { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

- `RoleId` is a string — no `Role` entity in this phase. Future phases may add a `Role` model.
- `OrganizationUnitId` scopes the role to a specific org unit (e.g., "Manager of Department X").
- **This is organization-scoped role context only.** It does NOT participate in the framework's authentication/authorization RBAC chain (`IPermissionChecker`, claims, token-based roles). The existing `ICurrentUser.IsInRole()` and `IPermissionChecker` remain the sole authorization truth sources.

---

## 4. Context Model

### 4.1 OrganizationContext

```csharp
public sealed class OrganizationContext
{
    public string? TenantId { get; init; }
    public string UserId { get; init; } = default!;
    public string? PrimaryOrganizationUnitId { get; init; }
    public IReadOnlyList<string> OrganizationUnitIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RoleIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PositionIds { get; init; } = Array.Empty<string>();
}
```

All ID lists are deduplicated. `PrimaryOrganizationUnitId` is selected from the first `IsPrimary == true` membership (stable-sorted by `CreatedAt`).

### 4.2 IOrganizationContextAccessor

```csharp
public interface IOrganizationContextAccessor
{
    OrganizationContext? Current { get; }
}
```

Default implementation: `NullOrganizationContextAccessor` (returns `null`). Located in `Organization` project (not Abstractions). No HTTP context dependency. Future phases will wire a real accessor that resolves `OrganizationContext` from `ICurrentUser` or claims.

---

## 5. Store

### 5.1 IOrganizationStore (11 methods)

```csharp
public interface IOrganizationStore
{
    Task SaveOrganizationUnitAsync(OrganizationUnit organizationUnit, CancellationToken ct = default);
    Task<OrganizationUnit?> GetOrganizationUnitByIdAsync(string id, string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync(string? tenantId = null, CancellationToken ct = default);

    Task SavePositionAsync(Position position, CancellationToken ct = default);
    Task<Position?> GetPositionByIdAsync(string id, string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<Position>> GetPositionsAsync(string? tenantId = null, CancellationToken ct = default);

    Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken ct = default);
    Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByUserAsync(string userId, string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByOrganizationUnitAsync(string orgUnitId, string? tenantId = null, CancellationToken ct = default);

    Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken ct = default);
    Task<IReadOnlyList<UserOrganizationRoleAssignment>> GetRoleAssignmentsByUserAsync(string userId, string? tenantId = null, CancellationToken ct = default);
}
```

- `Save` methods are upsert semantics.
- Store returns raw data (no `IsActive` filtering). Service layer filters active records.
- All methods accept `CancellationToken`.

### 5.2 InMemoryOrganizationStore

Implementation in `CrestCreates.Organization` project.

| Concern | Implementation |
|--------|----------------|
| **Storage** | One `ConcurrentDictionary<string, T>` per entity type. Key = `$"{tenantId ?? ""}:{id}"` (composite key). Same ID in different tenants are distinct entries. |
| **Upsert** | `_dict[id] = value` — Last-Write-Wins. No CAS because models lack `ConcurrencyStamp`. |
| **Read** | `TryGetValue` → return deep copy (`Clone()`). Never return dictionary reference. |
| **Query** | `.Values.Where(...).Select(x => x.Clone()).ToList().AsReadOnly()` |
| **Clone()** | Manual `Clone()` method on each entity — field-by-field copy. No reflection, no JSON serialization. Defined directly on model classes in Abstractions (following HumanTask/Workflow pattern). |
| **Filtering** | Store returns raw data. Service filters `IsActive`. |

`Clone()` follows the existing codebase pattern (`HumanTaskInstance.Clone()`, `WorkflowInstance.Clone()`):

```csharp
// Example: OrganizationUnit.Clone()
public OrganizationUnit Clone() => new()
{
    Id = Id, TenantId = TenantId, Name = Name, Code = Code,
    ParentId = ParentId, SortOrder = SortOrder,
    IsActive = IsActive, CreatedAt = CreatedAt
};
```

---

## 6. Hierarchy Service

### 6.1 IOrganizationHierarchyService

```csharp
public interface IOrganizationHierarchyService
{
    Task<IReadOnlyList<OrganizationUnit>> GetAncestorsAsync(string orgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationUnit>> GetDescendantsAsync(string orgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<bool> IsDescendantOfAsync(string orgUnitId, string ancestorOrgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<bool> IsUserInOrganizationAsync(string userId, string orgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<bool> IsUserInDescendantOrganizationAsync(string userId, string ancestorOrgUnitId, string? tenantId = null, CancellationToken ct = default);
}
```

### 6.2 DefaultOrganizationHierarchyService

Dependency: `IOrganizationStore`.

| Method | Algorithm | Cycle Detection |
|--------|----------|-----------------|
| `GetAncestorsAsync` | Load org units scoped to `tenantId`, trace `ParentId` upward until null | Yes — visited set. Throw `OrganizationHierarchyException` if ID re-encountered. |
| `GetDescendantsAsync` | Load org units scoped to `tenantId`, BFS/DFS scan | Yes — same visited-set detection. |
| `IsDescendantOfAsync` | Trace ParentId upward looking for ancestor ID, scoped to `tenantId` | Yes. |
| `IsUserInOrganizationAsync` | Check user's active memberships contain target org unit ID | N/A |
| `IsUserInDescendantOrganizationAsync` | `GetDescendantsAsync` + membership check | Inherited |

- No caching, no graph database, no recursion depth optimization.
- Simple BFS/DFS sufficient for Phase 5c.
- All hierarchy traversal is scoped to `tenantId` — `_store.GetOrganizationUnitsAsync(tenantId, ct)` is called once, then the unit map is built from scoped results.
- Filters `IsActive` on user memberships (not on org units — inactive org units still participate in hierarchy resolution).

---

## 7. Identity Service

### 7.1 IOrganizationIdentityService

```csharp
public interface IOrganizationIdentityService
{
    Task<OrganizationContext> GetContextAsync(string userId, string? tenantId = null, CancellationToken ct = default);
    Task<bool> IsInRoleAsync(string userId, string roleId, string? tenantId = null, CancellationToken ct = default);
    Task<bool> HasPositionAsync(string userId, string positionId, string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetUserOrganizationUnitIdsAsync(string userId, string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetUserRoleIdsAsync(string userId, string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetUserPositionIdsAsync(string userId, string? tenantId = null, CancellationToken ct = default);
}
```

### 7.2 DefaultOrganizationIdentityService

Dependency: `IOrganizationStore`.

- Filters `IsActive` on memberships and role assignments.
- `PrimaryOrganizationUnitId`: selects first `IsPrimary == true` membership, stable-sorted by `CreatedAt`.
- All ID lists are deduplicated.
- No permission resolution, no menu checks, no SQL generation.

---

## 8. Data Permission Scope (Stub)

### 8.1 Models

```csharp
public enum DataPermissionScopeKind
{
    None,
    Self,
    OwnOrganization,
    OwnOrganizationAndDescendants,
    All
}

public sealed class DataPermissionScope
{
    public DataPermissionScopeKind Kind { get; init; }
    public string? UserId { get; init; }
    public string? OrganizationUnitId { get; init; }
    public IReadOnlyList<string> OrganizationUnitIds { get; init; } = Array.Empty<string>();
}

public interface IDataPermissionScopeProvider
{
    Task<DataPermissionScope> GetScopeAsync(string userId, string permission, string? tenantId = null, CancellationToken ct = default);
}
```

### 8.2 DefaultDataPermissionScopeProvider

Minimal implementation:

- No organization membership → return `Self`.
- Has `PrimaryOrganizationUnitId` → return `OwnOrganization`.
- No configuration system, no LINQ expression generation, no SQL, no ORM integration.
- Foundation interface for future `DataPermissionFilter` by org-unit hierarchy.

---

## 9. Exceptions

```csharp
public class OrganizationException : Exception
{
    public OrganizationException(string message) : base(message) { }
    public OrganizationException(string message, Exception innerException) : base(message, innerException) { }
}

public class OrganizationHierarchyException : OrganizationException
{
    public OrganizationHierarchyException(string message) : base(message) { }
    public OrganizationHierarchyException(string message, Exception innerException) : base(message, innerException) { }
}
```

- Extends `Exception` (not `CrestException` or `RuntimeStoreException`) per spec.
- `OrganizationHierarchyException` used for: cycle detection, hierarchy resolution failures, circular parent references.

---

## 10. DI Registration

```csharp
public static class OrganizationServiceCollectionExtensions
{
    public static IServiceCollection AddOrganizationKernel(this IServiceCollection services)
    {
        services.TryAddSingleton<IOrganizationStore, InMemoryOrganizationStore>();
        services.TryAddScoped<IOrganizationHierarchyService, DefaultOrganizationHierarchyService>();
        services.TryAddScoped<IOrganizationIdentityService, DefaultOrganizationIdentityService>();
        services.TryAddScoped<IDataPermissionScopeProvider, DefaultDataPermissionScopeProvider>();
        services.TryAddSingleton<IOrganizationContextAccessor, NullOrganizationContextAccessor>();
        return services;
    }
}
```

Uses `TryAdd*` to avoid overwriting consumer custom registrations. No `[CrestModule]` class in this phase (module registration is not required for InMemory-only kernel — consumers call `AddOrganizationKernel()` directly).

---

## 11. Explicit Non-Goals (Things NOT Included)

### Not in this phase

| Category | Excluded Items |
|----------|---------------|
| HTTP | AppService, Controller, Minimal API, UI |
| Persistence | EF Core, SqlSugar, Dapper, MongoDB, Redis, file persistence, database migration |
| Transactions | UnitOfWork, Outbox, distributed transactions, distributed locks |
| RBAC | Permission tree management, menu permissions, full RBAC admin backend |
| Data Filter | LINQ/SQL implementation, expression scripting engine |
| HumanTask | Claim, Delegate, Escalation, SLA, Timeout, Reminder, Assignee resolution |
| Workflow | Branch, Transition, Retry, Compensation, SubWorkflow |
| Caching | Cache invalidation, distributed cache |
| Localization | i18n JSON resources |
| Cross-cutting | Modifying `HumanTaskCompletedEvent`, adding Workflow fields, making Organization depend on ASP.NET Core HttpContext |

### Dependencies NOT introduced

- Organization does NOT depend on Workflow
- Organization does NOT depend on HumanTask
- Organization does NOT depend on Capability
- Organization does NOT depend on ASP.NET Core (no `FrameworkReference`, no `IHttpContextAccessor`)
- No `IServiceProvider` injected into any Runtime Context

---

## 12. Tests

### 12.1 New Tests (15 minimum)

All in `framework/test/CrestCreates.Organization.Tests/`. Follow `HumanTask.Tests` conventions.

| # | Test | Assertion |
|---|------|-----------|
| 1 | `InMemoryOrganizationStore_SaveAndGetOrganizationUnit` | Upsert then get-by-id returns correct unit |
| 2 | `InMemoryOrganizationStore_SaveAndGetMembershipByUser` | Save membership, query by user returns it |
| 3 | `GetAncestors_ReturnsParentChain` | root→dept→team; query team ancestors → [dept, root] |
| 4 | `GetDescendants_ReturnsChildren` | root→dept1→team1 + root→dept2; query root descendants → [dept1, team1, dept2] |
| 5 | `IsDescendantOf_Works` | Positive and negative assertions |
| 6 | `DetectsCycle_ThrowsHierarchyException` | Circular parent → `OrganizationHierarchyException` |
| 7 | `IsUserInOrganization_Works` | User in dept: dept=true, root=false |
| 8 | `IsUserInDescendantOrganization_Works` | User in team, ancestor=root → true |
| 9 | `GetContext_ReturnsOrganizationsRolesPositions` | Assert context IDs are deduplicated and correct |
| 10 | `IsInRole_Works` | Positive and negative role check |
| 11 | `HasPosition_Works` | Positive and negative position check |
| 12 | `DataPermissionScopeProvider_ReturnsSelf_WhenNoOrganization` | No membership → Self |
| 13 | `DataPermissionScopeProvider_ReturnsOwnOrganization_WhenPrimaryExists` | Has primary → OwnOrganization |
| 14 | `GetAncestors_IsolatesByTenant` | Same org unit ID in two tenants → ancestors only include units from the specified tenant |
| 15 | `GetAncestors_CrossTenantParent_ReturnsNull` | Org unit's parent belongs to different tenant → treated as no parent (no cross-tenant leakage) |

### 12.2 Regression

All existing test suites must pass unchanged:

- `CrestCreates.HumanTask.Tests` (21 tests)
- `CrestCreates.Workflow.Tests` (57 tests)
- `CrestCreates.Capability.Tests`
- `CrestCreates.Metadata.Tests`

---

## 13. File Manifest

### Abstractions (~15 files)

```
CrestCreates.Organization.Abstractions/
├── CrestCreates.Organization.Abstractions.csproj
├── OrganizationUnit.cs
├── Position.cs
├── UserOrganizationMembership.cs
├── UserOrganizationRoleAssignment.cs
├── OrganizationContext.cs
├── IOrganizationContextAccessor.cs
├── IOrganizationStore.cs
├── IOrganizationHierarchyService.cs
├── IOrganizationIdentityService.cs
├── DataPermissionScopeKind.cs
├── DataPermissionScope.cs
├── IDataPermissionScopeProvider.cs
├── OrganizationException.cs
└── OrganizationHierarchyException.cs
```

### Implementation (~7 files)

```
CrestCreates.Organization/
├── CrestCreates.Organization.csproj
├── InMemoryOrganizationStore.cs
├── DefaultOrganizationHierarchyService.cs
├── DefaultOrganizationIdentityService.cs
├── DefaultDataPermissionScopeProvider.cs
├── NullOrganizationContextAccessor.cs
└── OrganizationServiceCollectionExtensions.cs
```

### Tests (~4 files)

```
CrestCreates.Organization.Tests/
├── CrestCreates.Organization.Tests.csproj
├── InMemoryOrganizationStoreTests.cs
├── OrganizationHierarchyServiceTests.cs
├── OrganizationIdentityServiceTests.cs
└── DataPermissionScopeProviderTests.cs
```

---

## 14. Design Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | No `ConcurrencyStamp` on models | Phase 5c is InMemory-only. Database providers can add their own concurrency control when implementing `IOrganizationStore`. |
| 2 | Last-Write-Wins upsert in InMemory store | Without `ConcurrencyStamp`, CAS is not possible. Sufficient for InMemory kernel. |
| 3 | `Clone()` in Abstractions on model classes | Follows the existing `HumanTaskInstance.Clone()` / `WorkflowInstance.Clone()` pattern. Keeps the store implementation simple (no extension method gymnastics). |
| 4 | `Exception` base class, not `CrestException` or `RuntimeStoreException` | Per spec. No HTTP layer in Phase 5c; no middleware integration needed. |
| 5 | No `[CrestModule]` class | Module registration not required for InMemory-only kernel. Consumers call `AddOrganizationKernel()` directly. If a module class is needed later, it can be added without breaking changes. |
| 6 | Store returns raw data (no `IsActive` filter) | Single responsibility: Store stores. Service filters. |
| 7 | Primary org unit: stable sort by `CreatedAt` | If multiple `IsPrimary` memberships exist, picks the first by creation time. No exception thrown — defers strict validation to future phases. |
| 8 | No `IHumanTaskAssigneeResolver` | Would require Organization → HumanTask dependency or HumanTask modification. Out of scope for Phase 5c. |
| 9 | `NullOrganizationContextAccessor` as singleton default | Placeholder until real user resolution is wired. No HttpContext coupling. |
| 10 | Store composite key: `(tenantId, id)` | `ConcurrentDictionary` key = `$"{tenantId ?? ""}:{id}"`. Same ID in different tenants are distinct entries. Hierarchy traversal scoped to `tenantId` via `_store.GetOrganizationUnitsAsync(tenantId, ct)`. Prevents cross-tenant hierarchy mixing and same-ID collisions. |
| 11 | `UserOrganizationRoleAssignment` — organization-scoped only | Explicitly renamed from `UserRoleAssignment` to signal this is organization-context role data, NOT a second authorization truth source. The framework's `ICurrentUser.IsInRole()` / `IPermissionChecker` / claims-based roles remain the sole RBAC chain. |

---

## 15. References

- Phase 5b Design: `docs/superpowers/specs/2026-06-11-phase-5b-durable-runtime-store-contracts-design.md`
- Phase 5b Plan: `docs/superpowers/plans/2026-06-11-phase-5b-durable-runtime-store-contracts.md`
- Metadata Architecture Summary: `docs/Feature/UnifiedMetadataModel/2026-06-09-unified-metadata-model-architecture-summary.md`
- InMemory store pattern reference: `framework/src/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs`
- DI registration pattern reference: `framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs`
- Test conventions: `framework/test/CrestCreates.HumanTask.Tests/`
- Existing data-scope Organization: `framework/src/CrestCreates.Domain/Permission/Organization.cs` (unrelated to Phase 5c)
