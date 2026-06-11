# Organization Identity Kernel — Usage Guide

> This document is for CrestCreates module developers who need to work with organization identity — org units, membership, hierarchy queries, and organization context.
> *Updated for Phase 5c (2026-06-11): Organization Identity Kernel — composite-key storage, tenant-scoped hierarchy, cycle detection, 42 tests*
> *Updated for Phase 5d (2026-06-11): Capability Authorization Bridge — `CapabilityDescriptor.Permissions` → `IPermissionChecker` via `RequiredPermissions`, organization roles remain separate from RBAC*
> *Updated for Phase 5e (2026-06-12): Data Permission Runtime Foundation — scope resolution from org identity + rule store, ORM-neutral filter model*

---

## 1. Quick Start

### 1.1 Register the Kernel

```csharp
using CrestCreates.Organization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrganizationKernel();
```

This registers all 8 services with `TryAdd*` semantics — if you provide custom implementations, they won't be overwritten.

### 1.2 Define an Organization Hierarchy

```csharp
var store = serviceProvider.GetRequiredService<IOrganizationStore>();

// Build a hierarchy: root → dept → team
await store.SaveOrganizationUnitAsync(new OrganizationUnit
{
    Id = "root", Name = "Acme Corp", TenantId = "t1"
});
await store.SaveOrganizationUnitAsync(new OrganizationUnit
{
    Id = "dept", Name = "Engineering", ParentId = "root", TenantId = "t1"
});
await store.SaveOrganizationUnitAsync(new OrganizationUnit
{
    Id = "team", Name = "Platform Team", ParentId = "dept", TenantId = "t1"
});
```

---

## 2. Core Models

### 2.1 OrganizationUnit

Represents a node in the org chart — department, team, division.

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | Unique within tenant (composite key `(tenantId, id)`) |
| `TenantId` | `string?` | Multi-tenant scope. Same ID in different tenants = distinct units. |
| `Name` | `string` | Display name |
| `Code` | `string?` | Optional short code (e.g., "ENG") |
| `ParentId` | `string?` | Self-referencing — builds tree hierarchy |
| `SortOrder` | `int` | Order within siblings |
| `IsActive` | `bool` | Soft-delete flag. Inactive units still participate in hierarchy. |
| `CreatedAt` | `DateTimeOffset` | Creation timestamp |

### 2.2 Position

Flat lookup table for job positions.

```csharp
await store.SavePositionAsync(new Position
{
    Id = "pos-manager", Name = "Manager", Code = "MGR", TenantId = "t1"
});
```

### 2.3 UserOrganizationMembership

Links a user to an org unit with optional position.

```csharp
await store.SaveMembershipAsync(new UserOrganizationMembership
{
    Id = "mem-1",
    UserId = "user-1",
    OrganizationUnitId = "team",
    PositionId = "pos-manager",       // optional
    IsPrimary = true,                  // marks primary org
    IsActive = true,
    TenantId = "t1"
});
```

### 2.4 UserOrganizationRoleAssignment

Organization-scoped role assignment. **Does NOT participate in the framework's RBAC chain** (`IPermissionChecker`, claims-based roles). This is org-context role data only.

```csharp
await store.SaveRoleAssignmentAsync(new UserOrganizationRoleAssignment
{
    Id = "ra-1",
    UserId = "user-1",
    RoleId = "department-admin",
    OrganizationUnitId = "dept",      // role scoped to this org unit
    IsActive = true,
    TenantId = "t1"
});
```

---

## 3. Hierarchy Queries

### 3.1 Get Ancestors

```csharp
var hierarchy = serviceProvider.GetRequiredService<IOrganizationHierarchyService>();

// root → dept → team
// GetAncestorsAsync("team", "t1") → [dept, root]
var ancestors = await hierarchy.GetAncestorsAsync("team", "t1");
```

### 3.2 Get Descendants

```csharp
// root → dept1 → team1
// root → dept2
// GetDescendantsAsync("root", "t1") → [dept1, team1, dept2]
var descendants = await hierarchy.GetDescendantsAsync("root", "t1");
```

### 3.3 Is Descendant Of

```csharp
bool isDescendant = await hierarchy.IsDescendantOfAsync("team", "root", "t1");
// → true
```

### 3.4 Is User In Organization

```csharp
bool isIn = await hierarchy.IsUserInOrganizationAsync("user-1", "dept", "t1");
// → true if user-1 has an active membership to "dept"
```

### 3.5 Is User In Descendant Organization

Checks if the user belongs to the target org OR any of its descendants:

```csharp
// User belongs to "team". Check against ancestor "root".
bool isIn = await hierarchy.IsUserInDescendantOrganizationAsync("user-1", "root", "t1");
// → true (team is a descendant of root)
```

### 3.6 Cycle Detection

All hierarchy traversal methods detect circular parent references and throw `OrganizationHierarchyException`:

```csharp
// A → B → C → A (cycle)
try
{
    await hierarchy.GetAncestorsAsync("a");
}
catch (OrganizationHierarchyException ex)
{
    // "Circular hierarchy detected..."
}
```

---

## 4. Identity Queries

### 4.1 Get Organization Context

Returns a snapshot of the user's full organization identity:

```csharp
var identity = serviceProvider.GetRequiredService<IOrganizationIdentityService>();

var context = await identity.GetContextAsync("user-1", "t1");

// context.UserId                    → "user-1"
// context.PrimaryOrganizationUnitId → first IsPrimary membership
// context.OrganizationUnitIds       → ["dept", "team"] (deduplicated)
// context.RoleIds                   → ["department-admin"] (deduplicated)
// context.PositionIds               → ["pos-manager"] (deduplicated)
```

Only active memberships and role assignments are included. All ID lists are deduplicated.

### 4.2 Check Role

```csharp
bool isAdmin = await identity.IsInRoleAsync("user-1", "department-admin", "t1");
```

Only checks active `UserOrganizationRoleAssignment` records. Does NOT check `IPermissionChecker` or claims.

### 4.3 Check Position

```csharp
bool isManager = await identity.HasPositionAsync("user-1", "pos-manager", "t1");
```

### 4.4 Get User IDs

```csharp
var orgIds   = await identity.GetUserOrganizationUnitIdsAsync("user-1", "t1");
var roleIds  = await identity.GetUserRoleIdsAsync("user-1", "t1");
var posIds   = await identity.GetUserPositionIdsAsync("user-1", "t1");
```

All return distinct, active-only IDs.

---

## 5. Multi-Tenant Isolation

### 5.1 Same ID, Different Tenants

OrganizationUnit and Position use composite store keys `(tenantId, id)`. Same ID in different tenants are completely independent:

```csharp
// Tenant 1: Engineering
await store.SaveOrganizationUnitAsync(new OrganizationUnit
    { Id = "dept", Name = "Engineering", TenantId = "t1", ParentId = "root-t1" });

// Tenant 2: Marketing — same ID, different tenant, different parent
await store.SaveOrganizationUnitAsync(new OrganizationUnit
    { Id = "dept", Name = "Marketing", TenantId = "t2", ParentId = "root-t2" });

// Scoped queries only see their tenant's data
var t1Ancestors = await hierarchy.GetAncestorsAsync("dept", "t1");
// → [root-t1] — only sees t1's tree
```

### 5.2 Cross-Tenant Parent

If an org unit's parent belongs to a different tenant, the hierarchy service treats it as having no parent (no cross-tenant leakage):

```csharp
// dept (t1) has parent root-t2 (t2) → query scoped to t1 won't find parent
var ancestors = await hierarchy.GetAncestorsAsync("dept", "t1");
// → [] (empty — root-t2 is not in t1's scope)
```

---

## 6. Data Permission Runtime

`IDataPermissionRuntime` resolves data access scope from org identity and converts it to an ORM-neutral filter.

### 6.1 Basic Scope Resolution

```csharp
var runtime = serviceProvider.GetRequiredService<IDataPermissionRuntime>();

var request = new DataPermissionScopeRequest
{
    UserId = "user-1",
    TenantId = "t1",
    Resource = "Book",
    Action = DataPermissionAction.Read
};

var scope = await runtime.ResolveScopeAsync(request);

// scope.Kind → DataPermissionScopeKind.OwnOrganization (if user has primary org)
// scope.OrganizationUnitId → "dept-1"
// scope.IsEmpty → false (Self or higher)
// scope.IsUnrestricted → false (not All)
```

### 6.2 Resolution Flow

1. **Rule store lookup** (if `request.Resource` is set):
   - Query `IDataPermissionScopeRuleStore` with tenantId
   - Tenant-specific rules override global rules
   - Match priority: tenant-exact > tenant-wildcard-perm > tenant-wildcard-action > global-*
2. **Org-membership fallback** (no rule matched):
   - No primary org → `Self`
   - Has primary org → `OwnOrganization`
3. **Fail closed**:
   - `Custom` scope kind → `None`
   - `OwnOrganization`/`OwnOrganizationAndDescendants` without primary org → `None`

### 6.3 Configure Scope Rules

```csharp
var ruleStore = serviceProvider.GetRequiredService<IDataPermissionScopeRuleStore>();

// Global: Book.Read → OwnOrganization scope
await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
{
    Resource = "Book", Action = "Read",
    ScopeKind = DataPermissionScopeKind.OwnOrganization
});

// Tenant-specific override: Book.Read → All for tenant "t-A"
await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
{
    Resource = "Book", Action = "Read", TenantId = "t-A",
    ScopeKind = DataPermissionScopeKind.All
});

// Block access: SecretDoc.Read → None
await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
{
    Resource = "SecretDoc", Action = "Read",
    ScopeKind = DataPermissionScopeKind.None
});
```

### 6.4 Build an ORM-neutral Filter

```csharp
// Given a scope from ResolveScopeAsync...
var scope = await runtime.ResolveScopeAsync(request);

// Define field mapping for your entity
var mapping = new DataPermissionFieldMapping
{
    UserIdField = "CreatorId",
    OrganizationUnitIdField = "OrganizationUnitId",
    TenantIdField = "TenantId"
};

// Build the filter
var filter = runtime.BuildFilter(scope, mapping);

if (filter.IsDenied)
{
    // Access denied — return empty/403
}
else if (filter.IsUnrestricted)
{
    // No filtering needed
}
else
{
    // Apply rules to your ORM query:
    // filter.Rules[0] → (OrganizationUnitId, Equal, "dept-1")
    // filter.Rules[1] → (TenantId, Equal, "t1")  // if tenant scoping active
}
```

### 6.5 Fail-Closed Behavior

| Scenario | Result |
|----------|--------|
| Scope = `Custom` | `filter.IsDenied = true` |
| `Self` scope but no `UserIdField` mapping | `filter.IsDenied = true` |
| `OwnOrganization` but no `OrganizationUnitIdField` | `filter.IsDenied = true` |
| `OwnOrganizationAndDescendants` but no `OrganizationUnitIdField` | `filter.IsDenied = true` |
| `All` scope with no `TenantIdField` | `filter.IsUnrestricted = true` |
| `All` scope + `TenantIdField` + `scope.TenantId` | `filter.IsUnrestricted = false`, `Rules = [(TenantId, Equal, val)]` |

---

## 7. Key Types Reference

| Type | Location | Purpose |
|------|----------|---------|
| `OrganizationUnit` | `Abstractions` | Org chart node (tree) |
| `Position` | `Abstractions` | Job position (flat) |
| `UserOrganizationMembership` | `Abstractions` | User ↔ org unit link |
| `UserOrganizationRoleAssignment` | `Abstractions` | Org-scoped role assignment |
| `OrganizationContext` | `Abstractions` | User's identity snapshot |
| `IOrganizationContextAccessor` | `Abstractions` | `Current` context accessor |
| `IOrganizationStore` | `Abstractions` | Upsert + query contract |
| `IOrganizationHierarchyService` | `Abstractions` | Ancestors, descendants, membership checks |
| `IOrganizationIdentityService` | `Abstractions` | Context, role, position queries |
| `IDataPermissionScopeProvider` | `Abstractions` | Data scope resolution |
| `IDataPermissionRuntime` | `Abstractions` | Scope resolution + filter building facade |
| `IDataPermissionFilterBuilder` | `Abstractions` | Build filter from scope + mapping |
| `IDataPermissionScopeRuleStore` | `Abstractions` | Tenant-aware scope rule storage |
| `DataPermissionScopeKind` | `Abstractions` | Scope enum: None/Self/OwnOrg/OwnOrgAndDescendants/All/Custom |
| `DataPermissionScope` | `Abstractions` | Scope value object |
| `DataPermissionScopeRequest` | `Abstractions` | Input for scope resolution |
| `DataPermissionScopeRule` | `Abstractions` | Rule model (Resource, Action, Permission, TenantId, ScopeKind) |
| `DataPermissionAction` | `Abstractions` | Action constants: Read/Create/Update/Delete/Query |
| `DataPermissionFilter` | `Abstractions` | ORM-neutral filter result (IsDenied, IsUnrestricted, Rules) |
| `DataPermissionFilterRule` | `Abstractions` | Single rule: FieldName, Operator (Equal/In), Value/Values |
| `DataPermissionFieldMapping` | `Abstractions` | Entity field bridge |
| `OrganizationException` | `Abstractions` | Base exception |
| `OrganizationHierarchyException` | `Abstractions` | Cycle/hierarchy failure |
| `InMemoryOrganizationStore` | `Organization` | ConcurrentDictionary store |
| `DefaultOrganizationHierarchyService` | `Organization` | BFS/DFS + cycle detection |
| `DefaultOrganizationIdentityService` | `Organization` | Active-only + dedup |
| `DefaultDataPermissionScopeProvider` | `Organization` | Full scope resolution with rule store + hierarchy |
| `DefaultDataPermissionRuntime` | `Organization` | Runtime facade |
| `DefaultDataPermissionFilterBuilder` | `Organization` | Fail-closed filter builder |
| `InMemoryDataPermissionScopeRuleStore` | `Organization` | Tenant-aware rule store (6-priority match) |
| `NullOrganizationContextAccessor` | `Organization` | Null default accessor |
| `AddOrganizationKernel()` | `Organization` | DI registration extension |

---

## 8. Important Boundaries

### Organization Role ≠ RBAC Role

`UserOrganizationRoleAssignment` and `IOrganizationIdentityService.IsInRoleAsync()` operate on **organization-scoped role context only**. They do NOT participate in:

- `IPermissionChecker` (the framework's permission check chain)
- JWT claims-based roles (`ICurrentUser.IsInRole()`)
- Token-based authorization

The framework's sole RBAC truth sources remain `IPermissionChecker` and claims. Organization roles are separate context data intended for workflow routing, assignee resolution, and future data-permission filtering — not for API access control.

### No HTTP Dependency

The Organization Kernel has zero dependency on ASP.NET Core `HttpContext`, `IHttpContextAccessor`, or `Microsoft.AspNetCore.App`. It works in any .NET application context.

### Not a Full RBAC System

This phase deliberately excludes: AppService, Controller, Minimal API, UI, Permission tree management, Menu permissions, complete RBAC admin backend, database persistence, cache invalidation.

### Capability Authorization Bridge (Phase 5d)

The Capability Authorization Bridge connects Capability Runtime to the existing `IPermissionChecker` RBAC chain:

```csharp
// Define a capability descriptor with permissions
var descriptor = new CapabilityDescriptor
{
    Id = "approve.expense",
    Name = "Approve Expense",
    Permissions = new[] { "expense.approve", "expense.read" }
};

// Register capability runtime (includes default auth service)
services.AddCapabilityRuntime();

// Permissions flow: descriptor.Permissions → context.RequiredPermissions
// → AuthorizationMiddleware → PermissionCapabilityAuthorizationService
// → IPermissionChecker.IsGrantedAsync(permissions)

// Empty permissions → allow (no IPermissionChecker required)
// Non-empty → ALL must be granted (AllGranted semantics)
// Any denied → pipeline returns UNAUTHORIZED
```

**Organization roles remain separate**: `UserOrganizationRoleAssignment` and `IOrganizationIdentityService.IsInRoleAsync()` are organization-scoped identity facts. They do NOT flow into `IPermissionChecker` or the Capability Authorization Bridge. The sole RBAC truth sources are `IPermissionChecker` and claims.

For full details, see:
- Design spec: `docs/superpowers/specs/2026-06-11-phase-5d-capability-authorization-bridge-design.md`
- Implementation plan: `docs/superpowers/plans/2026-06-11-phase-5d-capability-authorization-bridge.md`

---

## 9. Tests Quick Reference

Run all Organization tests:

```bash
dotnet test framework/test/CrestCreates.Organization.Tests/
# 79 tests, 0 failures
```

Run specific test groups:

```bash
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~InMemoryOrganizationStoreTests"
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~OrganizationHierarchyServiceTests"
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~OrganizationIdentityServiceTests"
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~DataPermissionScopeProviderTests"
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~DataPermissionFilterBuilderTests"
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~DataPermissionRuntimeTests"
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~InMemoryDataPermissionScopeRuleStoreTests"
```

---

## 10. References

- Architecture summary: `docs/Feature/Organization/arch-design.md`
- Design spec (Phase 5c): `docs/superpowers/specs/2026-06-11-phase-5c-organization-identity-kernel-design.md`
- Implementation plan (Phase 5c): `docs/superpowers/plans/2026-06-11-phase-5c-organization-identity-kernel.md`
- Design spec (Phase 5d): `docs/superpowers/specs/2026-06-11-phase-5d-capability-authorization-bridge-design.md`
- Implementation plan (Phase 5d): `docs/superpowers/plans/2026-06-11-phase-5d-capability-authorization-bridge.md`
- Design spec (Phase 5e): `docs/superpowers/specs/2026-06-11-phase-5e-data-permission-runtime-foundation-design.md`
- Implementation plan (Phase 5e): `docs/superpowers/plans/2026-06-11-phase-5e-data-permission-runtime-foundation.md`
- Platform memory: `memory.md`
