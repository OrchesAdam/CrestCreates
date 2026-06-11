# Phase 5c — Organization Identity Kernel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the minimum Organization Identity Kernel (models, composite-key store, hierarchy queries with tenant scoping, identity queries, data-permission stub) with InMemory implementation and 42 tests.

**Architecture:** Three new projects — `CrestCreates.Organization.Abstractions` (pure models + interfaces, zero deps), `CrestCreates.Organization` (InMemory store + services + DI), `CrestCreates.Organization.Tests` (xUnit + Moq + FluentAssertions). No dependency on Workflow, HumanTask, Capability, or ASP.NET Core.

**Tech Stack:** .NET 10.0, ConcurrentDictionary, xUnit, Moq, FluentAssertions

**Spec:** `docs/superpowers/specs/2026-06-11-phase-5c-organization-identity-kernel-design.md`

---

### Task 1: Scaffold Organization.Abstractions project

**Files:**
- Create: `framework/src/CrestCreates.Organization.Abstractions/CrestCreates.Organization.Abstractions.csproj`

- [ ] **Step 1: Create project directory and .csproj**

```bash
mkdir -p framework/src/CrestCreates.Organization.Abstractions
```

Write `framework/src/CrestCreates.Organization.Abstractions/CrestCreates.Organization.Abstractions.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Organization.Abstractions</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Add to .slnx**

Add these lines inside `<Folder Name="/src/core/">` (alphabetically, after `CrestCreates.Modularity` and before `CrestCreates.Schema.Abstractions`):

```
Search for `CrestCreates.Modularity` in CrestCreates.slnx, then insert after that line:
```

```xml
    <Project Path="framework/src/CrestCreates.Organization.Abstractions/CrestCreates.Organization.Abstractions.csproj" />
```

- [ ] **Step 3: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization.Abstractions/
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/ CrestCreates.slnx
git commit -m "feat(org): scaffold Organization.Abstractions project"
```

---

### Task 2: Scaffold Organization project

**Files:**
- Create: `framework/src/CrestCreates.Organization/CrestCreates.Organization.csproj`

- [ ] **Step 1: Create project directory and .csproj**

```bash
mkdir -p framework/src/CrestCreates.Organization
```

Write `framework/src/CrestCreates.Organization/CrestCreates.Organization.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Organization</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\CrestCreates.Organization.Abstractions\CrestCreates.Organization.Abstractions.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="CrestCreates.Organization.Tests" />
  </ItemGroup>
</Project>
```

Note: `Microsoft.Extensions.DependencyInjection.Abstractions` is explicitly referenced (not relying on SDK implicit includes). Version comes from `Directory.Packages.props` central package management.

- [ ] **Step 2: Add to .slnx**

Inside `<Folder Name="/src/core/">`, after the Abstractions entry just added:

```xml
    <Project Path="framework/src/CrestCreates.Organization/CrestCreates.Organization.csproj" />
```

- [ ] **Step 3: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization/
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Organization/ CrestCreates.slnx
git commit -m "feat(org): scaffold Organization project"
```

---

### Task 3: Add OrganizationUnit and Position models

**Files:**
- Create: `framework/src/CrestCreates.Organization.Abstractions/OrganizationUnit.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/Position.cs`

- [ ] **Step 1: Write OrganizationUnit.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/OrganizationUnit.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

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

- [ ] **Step 2: Write Position.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/Position.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

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

- [ ] **Step 3: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization.Abstractions/
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/
git commit -m "feat(org): add OrganizationUnit and Position models"
```

---

### Task 4: Add UserOrganizationMembership and UserOrganizationRoleAssignment models

**Files:**
- Create: `framework/src/CrestCreates.Organization.Abstractions/UserOrganizationMembership.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/UserOrganizationRoleAssignment.cs`

- [ ] **Step 1: Write UserOrganizationMembership.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/UserOrganizationMembership.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

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

- [ ] **Step 2: Write UserOrganizationRoleAssignment.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/UserOrganizationRoleAssignment.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

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

- [ ] **Step 3: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization.Abstractions/
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/
git commit -m "feat(org): add UserOrganizationMembership and UserOrganizationRoleAssignment models"
```

---

### Task 5: Add OrganizationContext and IOrganizationContextAccessor

**Files:**
- Create: `framework/src/CrestCreates.Organization.Abstractions/OrganizationContext.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/IOrganizationContextAccessor.cs`

- [ ] **Step 1: Write OrganizationContext.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/OrganizationContext.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

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

- [ ] **Step 2: Write IOrganizationContextAccessor.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/IOrganizationContextAccessor.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

public interface IOrganizationContextAccessor
{
    OrganizationContext? Current { get; }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization.Abstractions/
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/
git commit -m "feat(org): add OrganizationContext and IOrganizationContextAccessor"
```

---

### Task 6: Add exceptions

**Files:**
- Create: `framework/src/CrestCreates.Organization.Abstractions/OrganizationException.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/OrganizationHierarchyException.cs`

- [ ] **Step 1: Write OrganizationException.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/OrganizationException.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

public class OrganizationException : Exception
{
    public OrganizationException(string message) : base(message) { }
    public OrganizationException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 2: Write OrganizationHierarchyException.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/OrganizationHierarchyException.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

public class OrganizationHierarchyException : OrganizationException
{
    public OrganizationHierarchyException(string message) : base(message) { }
    public OrganizationHierarchyException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization.Abstractions/
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/
git commit -m "feat(org): add OrganizationException and OrganizationHierarchyException"
```

---

### Task 7: Add Store interface

**Files:**
- Create: `framework/src/CrestCreates.Organization.Abstractions/IOrganizationStore.cs`

- [ ] **Step 1: Write IOrganizationStore.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/IOrganizationStore.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

public interface IOrganizationStore
{
    Task SaveOrganizationUnitAsync(OrganizationUnit organizationUnit, CancellationToken cancellationToken = default);
    Task<OrganizationUnit?> GetOrganizationUnitByIdAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    Task SavePositionAsync(Position position, CancellationToken cancellationToken = default);
    Task<Position?> GetPositionByIdAsync(string positionId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Position>> GetPositionsAsync(string? tenantId = null, CancellationToken cancellationToken = default);

    Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByOrganizationUnitAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default);

    Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserOrganizationRoleAssignment>> GetRoleAssignmentsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization.Abstractions/
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/
git commit -m "feat(org): add IOrganizationStore interface"
```

---

### Task 8: Add Hierarchy and Identity service interfaces

**Files:**
- Create: `framework/src/CrestCreates.Organization.Abstractions/IOrganizationHierarchyService.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/IOrganizationIdentityService.cs`

- [ ] **Step 1: Write IOrganizationHierarchyService.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/IOrganizationHierarchyService.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

public interface IOrganizationHierarchyService
{
    Task<IReadOnlyList<OrganizationUnit>> GetAncestorsAsync(string orgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationUnit>> GetDescendantsAsync(string orgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<bool> IsDescendantOfAsync(string orgUnitId, string ancestorOrgUnitId, string? tenantId = null, CancellationToken ct = default);
    Task<bool> IsUserInOrganizationAsync(string userId, string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<bool> IsUserInDescendantOrganizationAsync(string userId, string ancestorOrganizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write IOrganizationIdentityService.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/IOrganizationIdentityService.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

public interface IOrganizationIdentityService
{
    Task<OrganizationContext> GetContextAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<bool> IsInRoleAsync(string userId, string roleId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<bool> HasPositionAsync(string userId, string positionId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetUserOrganizationUnitIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetUserRoleIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetUserPositionIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization.Abstractions/
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/
git commit -m "feat(org): add IOrganizationHierarchyService and IOrganizationIdentityService"
```

---

### Task 9: Add DataPermission scope models and interface

**Files:**
- Create: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeKind.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScope.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionScopeProvider.cs`

- [ ] **Step 1: Write DataPermissionScopeKind.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeKind.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

public enum DataPermissionScopeKind
{
    None,
    Self,
    OwnOrganization,
    OwnOrganizationAndDescendants,
    All
}
```

- [ ] **Step 2: Write DataPermissionScope.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScope.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionScope
{
    public DataPermissionScopeKind Kind { get; init; }
    public string? UserId { get; init; }
    public string? OrganizationUnitId { get; init; }
    public IReadOnlyList<string> OrganizationUnitIds { get; init; } = Array.Empty<string>();
}
```

- [ ] **Step 3: Write IDataPermissionScopeProvider.cs**

Write `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionScopeProvider.cs`:

```csharp
namespace CrestCreates.Organization.Abstractions;

public interface IDataPermissionScopeProvider
{
    Task<DataPermissionScope> GetScopeAsync(string userId, string permission, string? tenantId = null, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Verify Abstractions builds clean**

```bash
dotnet build framework/src/CrestCreates.Organization.Abstractions/
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/
git commit -m "feat(org): add DataPermission scope models and IDataPermissionScopeProvider"
```

---

### Task 10: Add Clone() methods to models (in Abstractions)

**Files:**
- Modify: `framework/src/CrestCreates.Organization.Abstractions/OrganizationUnit.cs`
- Modify: `framework/src/CrestCreates.Organization.Abstractions/Position.cs`
- Modify: `framework/src/CrestCreates.Organization.Abstractions/UserOrganizationMembership.cs`
- Modify: `framework/src/CrestCreates.Organization.Abstractions/UserOrganizationRoleAssignment.cs`

Clone() follows the existing `HumanTaskInstance.Clone()` / `WorkflowInstance.Clone()` pattern — defined directly on the model class in Abstractions.

- [ ] **Step 1: Add Clone() to OrganizationUnit.cs**

Modify `framework/src/CrestCreates.Organization.Abstractions/OrganizationUnit.cs` — add Clone() after the properties:

```csharp
    public OrganizationUnit Clone() => new()
    {
        Id = Id,
        TenantId = TenantId,
        Name = Name,
        Code = Code,
        ParentId = ParentId,
        SortOrder = SortOrder,
        IsActive = IsActive,
        CreatedAt = CreatedAt
    };
```

- [ ] **Step 2: Add Clone() to Position.cs**

Modify `framework/src/CrestCreates.Organization.Abstractions/Position.cs`:

```csharp
    public Position Clone() => new()
    {
        Id = Id,
        TenantId = TenantId,
        Name = Name,
        Code = Code,
        IsActive = IsActive,
        CreatedAt = CreatedAt
    };
```

- [ ] **Step 3: Add Clone() to UserOrganizationMembership.cs**

Modify `framework/src/CrestCreates.Organization.Abstractions/UserOrganizationMembership.cs`:

```csharp
    public UserOrganizationMembership Clone() => new()
    {
        Id = Id,
        TenantId = TenantId,
        UserId = UserId,
        OrganizationUnitId = OrganizationUnitId,
        PositionId = PositionId,
        IsPrimary = IsPrimary,
        IsActive = IsActive,
        CreatedAt = CreatedAt
    };
```

- [ ] **Step 4: Add Clone() to UserOrganizationRoleAssignment.cs**

Modify `framework/src/CrestCreates.Organization.Abstractions/UserOrganizationRoleAssignment.cs`:

```csharp
    public UserOrganizationRoleAssignment Clone() => new()
    {
        Id = Id,
        TenantId = TenantId,
        UserId = UserId,
        RoleId = RoleId,
        OrganizationUnitId = OrganizationUnitId,
        IsActive = IsActive,
        CreatedAt = CreatedAt
    };
```

- [ ] **Step 5: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization.Abstractions/
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/
git commit -m "feat(org): add Clone() methods to all organization models"
```

---

### Task 11: Add NullOrganizationContextAccessor

**Files:**
- Create: `framework/src/CrestCreates.Organization/NullOrganizationContextAccessor.cs`

- [ ] **Step 1: Write NullOrganizationContextAccessor.cs**

Write `framework/src/CrestCreates.Organization/NullOrganizationContextAccessor.cs`:

```csharp
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class NullOrganizationContextAccessor : IOrganizationContextAccessor
{
    public OrganizationContext? Current => null;
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization/
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Organization/
git commit -m "feat(org): add NullOrganizationContextAccessor"
```

---

### Task 12: Add InMemoryOrganizationStore

**Files:**
- Create: `framework/src/CrestCreates.Organization/InMemoryOrganizationStore.cs`

- [ ] **Step 1: Write InMemoryOrganizationStore.cs**

Write `framework/src/CrestCreates.Organization/InMemoryOrganizationStore.cs`:

```csharp
using System.Collections.Concurrent;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class InMemoryOrganizationStore : IOrganizationStore
{
    private readonly ConcurrentDictionary<string, OrganizationUnit> _orgUnits = new();
    private readonly ConcurrentDictionary<string, Position> _positions = new();
    private readonly ConcurrentDictionary<string, UserOrganizationMembership> _memberships = new();
    private readonly ConcurrentDictionary<string, UserOrganizationRoleAssignment> _roleAssignments = new();

    // ── OrganizationUnit (composite key: tenantId + ":" + id) ──

    public Task SaveOrganizationUnitAsync(OrganizationUnit organizationUnit, CancellationToken cancellationToken = default)
    {
        var key = CompKey(organizationUnit.TenantId, organizationUnit.Id);
        _orgUnits[key] = organizationUnit.Clone();
        return Task.CompletedTask;
    }

    public Task<OrganizationUnit?> GetOrganizationUnitByIdAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var key = CompKey(tenantId, organizationUnitId);
        if (_orgUnits.TryGetValue(key, out var existing))
            return Task.FromResult<OrganizationUnit?>(existing.Clone());
        return Task.FromResult<OrganizationUnit?>(null);
    }

    public Task<IReadOnlyList<OrganizationUnit>> GetOrganizationUnitsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<OrganizationUnit> query = _orgUnits.Values;
        if (tenantId is not null)
            query = query.Where(o => o.TenantId == tenantId);

        var result = query.Select(o => o.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<OrganizationUnit>)result);
    }

    // ── Position (composite key: tenantId + ":" + id) ──

    public Task SavePositionAsync(Position position, CancellationToken cancellationToken = default)
    {
        var key = CompKey(position.TenantId, position.Id);
        _positions[key] = position.Clone();
        return Task.CompletedTask;
    }

    public Task<Position?> GetPositionByIdAsync(string positionId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var key = CompKey(tenantId, positionId);
        if (_positions.TryGetValue(key, out var existing))
            return Task.FromResult<Position?>(existing.Clone());
        return Task.FromResult<Position?>(null);
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<Position> query = _positions.Values;
        if (tenantId is not null)
            query = query.Where(p => p.TenantId == tenantId);

        var result = query.Select(p => p.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<Position>)result);
    }

    // ── Helpers ──

    private static string CompKey(string? tenantId, string id) => $"{tenantId ?? ""}:{id}";

    // ── UserOrganizationMembership ──

    public Task SaveMembershipAsync(UserOrganizationMembership membership, CancellationToken cancellationToken = default)
    {
        _memberships[membership.Id] = membership.Clone();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<UserOrganizationMembership> query = _memberships.Values.Where(m => m.UserId == userId);
        if (tenantId is not null)
            query = query.Where(m => m.TenantId == tenantId);

        var result = query.Select(m => m.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)result);
    }

    public Task<IReadOnlyList<UserOrganizationMembership>> GetMembershipsByOrganizationUnitAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<UserOrganizationMembership> query = _memberships.Values.Where(m => m.OrganizationUnitId == organizationUnitId);
        if (tenantId is not null)
            query = query.Where(m => m.TenantId == tenantId);

        var result = query.Select(m => m.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<UserOrganizationMembership>)result);
    }

    // ── UserOrganizationRoleAssignment ──

    public Task SaveRoleAssignmentAsync(UserOrganizationRoleAssignment assignment, CancellationToken cancellationToken = default)
    {
        _roleAssignments[assignment.Id] = assignment.Clone();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UserOrganizationRoleAssignment>> GetRoleAssignmentsByUserAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        IEnumerable<UserOrganizationRoleAssignment> query = _roleAssignments.Values.Where(a => a.UserId == userId);
        if (tenantId is not null)
            query = query.Where(a => a.TenantId == tenantId);

        var result = query.Select(a => a.Clone()).ToList().AsReadOnly();
        return Task.FromResult((IReadOnlyList<UserOrganizationRoleAssignment>)result);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization/
```
Expected: Build succeeded. No errors about missing ConcurrentDictionary (it's in System.Collections.Concurrent which is part of the BCL).

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Organization/
git commit -m "feat(org): add InMemoryOrganizationStore with ConcurrentDictionary"
```

---

### Task 13: Add DefaultOrganizationHierarchyService

**Files:**
- Create: `framework/src/CrestCreates.Organization/DefaultOrganizationHierarchyService.cs`

- [ ] **Step 1: Write DefaultOrganizationHierarchyService.cs**

Write `framework/src/CrestCreates.Organization/DefaultOrganizationHierarchyService.cs`:

```csharp
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultOrganizationHierarchyService : IOrganizationHierarchyService
{
    private readonly IOrganizationStore _store;

    public DefaultOrganizationHierarchyService(IOrganizationStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<OrganizationUnit>> GetAncestorsAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var allUnits = await _store.GetOrganizationUnitsAsync(tenantId, cancellationToken: cancellationToken);
        var unitMap = allUnits.ToDictionary(u => u.Id);
        var result = new List<OrganizationUnit>();
        var visited = new HashSet<string> { organizationUnitId };
        var currentId = organizationUnitId;

        while (true)
        {
            if (!unitMap.TryGetValue(currentId, out var current))
                break;

            var parentId = current.ParentId;
            if (parentId is null)
                break;

            if (!visited.Add(parentId))
                throw new OrganizationHierarchyException(
                    $"Circular hierarchy detected: organization unit '{parentId}' is already in the ancestor chain starting from '{organizationUnitId}'.");

            if (!unitMap.TryGetValue(parentId, out var parent))
                break;

            result.Add(parent.Clone());
            currentId = parentId;
        }

        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<OrganizationUnit>> GetDescendantsAsync(string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var allUnits = await _store.GetOrganizationUnitsAsync(tenantId, cancellationToken: cancellationToken);
        var childrenMap = allUnits
            .GroupBy(u => u.ParentId)
            .ToDictionary(g => g.Key ?? string.Empty, g => g.ToList());

        var result = new List<OrganizationUnit>();
        var visited = new HashSet<string> { organizationUnitId };
        var queue = new Queue<string>();

        // Seed with direct children
        if (childrenMap.TryGetValue(organizationUnitId, out var directChildren))
        {
            foreach (var child in directChildren)
            {
                queue.Enqueue(child.Id);
            }
        }

        // BFS
        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();

            if (!visited.Add(currentId))
                throw new OrganizationHierarchyException(
                    $"Circular hierarchy detected: organization unit '{currentId}' appears multiple times in the descendant tree of '{organizationUnitId}'.");

            var current = allUnits.FirstOrDefault(u => u.Id == currentId);
            if (current is null)
                continue;

            result.Add(current.Clone());

            if (childrenMap.TryGetValue(currentId, out var children))
            {
                foreach (var child in children)
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return result.AsReadOnly();
    }

    public async Task<bool> IsDescendantOfAsync(string organizationUnitId, string ancestorOrganizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        if (organizationUnitId == ancestorOrganizationUnitId)
            return false;

        var ancestors = await GetAncestorsAsync(organizationUnitId, tenantId, cancellationToken);
        return ancestors.Any(a => a.Id == ancestorOrganizationUnitId);
    }

    public async Task<bool> IsUserInOrganizationAsync(string userId, string organizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        return memberships.Any(m => m.IsActive && m.OrganizationUnitId == organizationUnitId);
    }

    public async Task<bool> IsUserInDescendantOrganizationAsync(string userId, string ancestorOrganizationUnitId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        var activeMembershipOrgIds = memberships.Where(m => m.IsActive).Select(m => m.OrganizationUnitId).ToHashSet();

        if (activeMembershipOrgIds.Count == 0)
            return false;

        // Check if user directly belongs to the ancestor
        if (activeMembershipOrgIds.Contains(ancestorOrganizationUnitId))
            return true;

        // Check if user belongs to any descendant of the ancestor
        var descendants = await GetDescendantsAsync(ancestorOrganizationUnitId, tenantId, cancellationToken);
        var descendantIds = descendants.Select(d => d.Id).ToHashSet();
        return activeMembershipOrgIds.Overlaps(descendantIds);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization/
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Organization/
git commit -m "feat(org): add DefaultOrganizationHierarchyService with cycle detection"
```

---

### Task 14: Add DefaultOrganizationIdentityService

**Files:**
- Create: `framework/src/CrestCreates.Organization/DefaultOrganizationIdentityService.cs`

- [ ] **Step 1: Write DefaultOrganizationIdentityService.cs**

Write `framework/src/CrestCreates.Organization/DefaultOrganizationIdentityService.cs`:

```csharp
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultOrganizationIdentityService : IOrganizationIdentityService
{
    private readonly IOrganizationStore _store;

    public DefaultOrganizationIdentityService(IOrganizationStore store)
    {
        _store = store;
    }

    public async Task<OrganizationContext> GetContextAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        var activeMemberships = memberships.Where(m => m.IsActive).ToList();

        var primary = activeMemberships
            .Where(m => m.IsPrimary)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefault();

        var roleAssignments = await _store.GetRoleAssignmentsByUserAsync(userId, tenantId, cancellationToken);
        var activeRoles = roleAssignments.Where(r => r.IsActive).ToList();

        return new OrganizationContext
        {
            TenantId = tenantId,
            UserId = userId,
            PrimaryOrganizationUnitId = primary?.OrganizationUnitId,
            OrganizationUnitIds = activeMemberships.Select(m => m.OrganizationUnitId).Distinct().ToList().AsReadOnly(),
            RoleIds = activeRoles.Select(r => r.RoleId).Distinct().ToList().AsReadOnly(),
            PositionIds = activeMemberships.Where(m => m.PositionId is not null).Select(m => m.PositionId!).Distinct().ToList().AsReadOnly()
        };
    }

    public async Task<bool> IsInRoleAsync(string userId, string roleId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var assignments = await _store.GetRoleAssignmentsByUserAsync(userId, tenantId, cancellationToken);
        return assignments.Any(a => a.IsActive && a.RoleId == roleId);
    }

    public async Task<bool> HasPositionAsync(string userId, string positionId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        return memberships.Any(m => m.IsActive && m.PositionId == positionId);
    }

    public async Task<IReadOnlyList<string>> GetUserOrganizationUnitIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        return memberships.Where(m => m.IsActive).Select(m => m.OrganizationUnitId).Distinct().ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<string>> GetUserRoleIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var assignments = await _store.GetRoleAssignmentsByUserAsync(userId, tenantId, cancellationToken);
        return assignments.Where(a => a.IsActive).Select(a => a.RoleId).Distinct().ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<string>> GetUserPositionIdsAsync(string userId, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var memberships = await _store.GetMembershipsByUserAsync(userId, tenantId, cancellationToken);
        return memberships.Where(m => m.IsActive && m.PositionId is not null).Select(m => m.PositionId!).Distinct().ToList().AsReadOnly();
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization/
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Organization/
git commit -m "feat(org): add DefaultOrganizationIdentityService"
```

---

### Task 15: Add DefaultDataPermissionScopeProvider

**Files:**
- Create: `framework/src/CrestCreates.Organization/DefaultDataPermissionScopeProvider.cs`

- [ ] **Step 1: Write DefaultDataPermissionScopeProvider.cs**

Write `framework/src/CrestCreates.Organization/DefaultDataPermissionScopeProvider.cs`:

```csharp
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultDataPermissionScopeProvider : IDataPermissionScopeProvider
{
    private readonly IOrganizationIdentityService _identityService;

    public DefaultDataPermissionScopeProvider(IOrganizationIdentityService identityService)
    {
        _identityService = identityService;
    }

    public async Task<DataPermissionScope> GetScopeAsync(string userId, string permission, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var context = await _identityService.GetContextAsync(userId, tenantId, cancellationToken);

        if (context.PrimaryOrganizationUnitId is null)
        {
            return new DataPermissionScope { Kind = DataPermissionScopeKind.Self, UserId = userId };
        }

        return new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            UserId = userId,
            OrganizationUnitId = context.PrimaryOrganizationUnitId,
            OrganizationUnitIds = context.OrganizationUnitIds
        };
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization/
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Organization/
git commit -m "feat(org): add DefaultDataPermissionScopeProvider"
```

---

### Task 16: Add DI registration extension

**Files:**
- Create: `framework/src/CrestCreates.Organization/OrganizationServiceCollectionExtensions.cs`

- [ ] **Step 1: Write OrganizationServiceCollectionExtensions.cs**

Write `framework/src/CrestCreates.Organization/OrganizationServiceCollectionExtensions.cs`:

```csharp
using CrestCreates.Organization.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Organization;

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

- [ ] **Step 2: Verify build**

```bash
dotnet build framework/src/CrestCreates.Organization/
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Organization/
git commit -m "feat(org): add AddOrganizationKernel DI extension"
```

---

### Task 17: Scaffold test project

**Files:**
- Create: `framework/test/CrestCreates.Organization.Tests/CrestCreates.Organization.Tests.csproj`
- Create: `framework/test/CrestCreates.Organization.Tests/Usings.cs`

- [ ] **Step 1: Check if test directory exists**

```bash
ls framework/test/CrestCreates.Organization.Tests/ 2>/dev/null && echo "EXISTS" || echo "NOT_FOUND"
```

If it exists with old data-scope Organization tests (from before Phase 5c), move them to `99_RecycleBin` per AGENTS.md rules:

```bash
# If EXISTS: move old test directory to recycle bin
timestamp=$(date +%Y%m%d-%H%M%S)
mv framework/test/CrestCreates.Organization.Tests "99_RecycleBin/CrestCreates.Organization.Tests-$timestamp"
```

Then create the fresh Phase 5c test directory:

```bash
mkdir -p framework/test/CrestCreates.Organization.Tests
```

- [ ] **Step 2: Write .csproj**

Write `framework/test/CrestCreates.Organization.Tests/CrestCreates.Organization.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CrestCreates.Organization.Tests</RootNamespace>
    <AssemblyName>CrestCreates.Organization.Tests</AssemblyName>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CrestCreates.Organization\CrestCreates.Organization.csproj" />
    <ProjectReference Include="..\CrestCreates.TestBase\CrestCreates.TestBase.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write Usings.cs**

Write `framework/test/CrestCreates.Organization.Tests/Usings.cs`:

```csharp
global using Xunit;
global using FluentAssertions;
global using Moq;
```

- [ ] **Step 4: Add to .slnx**

Inside `<Folder Name="/src/test/">`, add:

```xml
    <Project Path="framework/test/CrestCreates.Organization.Tests/CrestCreates.Organization.Tests.csproj" />
```

- [ ] **Step 5: Verify build**

```bash
dotnet build framework/test/CrestCreates.Organization.Tests/
```
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.Organization.Tests/ CrestCreates.slnx
git commit -m "test(org): scaffold Organization.Tests project"
```

---

### Task 18: Add InMemoryOrganizationStore tests

**Files:**
- Create: `framework/test/CrestCreates.Organization.Tests/InMemoryOrganizationStoreTests.cs`

- [ ] **Step 1: Write failing test for InMemoryOrganizationStoreTests.cs**

Write `framework/test/CrestCreates.Organization.Tests/InMemoryOrganizationStoreTests.cs`:

```csharp
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class InMemoryOrganizationStoreTests
{
    private readonly InMemoryOrganizationStore _store = new();

    [Fact]
    public async Task SaveOrganizationUnit_And_GetById_Returns_UpsertedUnit()
    {
        var unit = new OrganizationUnit
        {
            Id = "dept-1",
            Name = "Engineering",
            Code = "ENG",
        };

        await _store.SaveOrganizationUnitAsync(unit);
        var result = await _store.GetOrganizationUnitByIdAsync("dept-1");

        result.Should().NotBeNull();
        result!.Id.Should().Be("dept-1");
        result.Name.Should().Be("Engineering");
        result.Code.Should().Be("ENG");

        // Mutation check: result is a Clone, not the original reference
        result.Should().NotBeSameAs(unit);
    }

    [Fact]
    public async Task SaveOrganizationUnit_Upserts_ExistingUnit()
    {
        var unit = new OrganizationUnit { Id = "dept-1", Name = "Engineering" };
        await _store.SaveOrganizationUnitAsync(unit);

        var updated = new OrganizationUnit { Id = "dept-1", Name = "Engineering V2" };
        await _store.SaveOrganizationUnitAsync(updated);

        var result = await _store.GetOrganizationUnitByIdAsync("dept-1");
        result!.Name.Should().Be("Engineering V2");
    }

    [Fact]
    public async Task GetOrganizationUnitById_ReturnsNull_WhenNotFound()
    {
        var result = await _store.GetOrganizationUnitByIdAsync("nonexistent");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetOrganizationUnits_ReturnsAll_WhenNoTenantFilter()
    {
        await _store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "dept-1", Name = "Eng", TenantId = "t1" });
        await _store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "dept-2", Name = "HR", TenantId = "t2" });

        var result = await _store.GetOrganizationUnitsAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetOrganizationUnits_FiltersByTenant()
    {
        await _store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "dept-1", Name = "Eng", TenantId = "t1" });
        await _store.SaveOrganizationUnitAsync(new OrganizationUnit { Id = "dept-2", Name = "HR", TenantId = "t2" });

        var result = await _store.GetOrganizationUnitsAsync("t1");

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("dept-1");
    }

    [Fact]
    public async Task SaveMembership_And_GetByUser_Returns_Membership()
    {
        var membership = new UserOrganizationMembership
        {
            Id = "m-1",
            UserId = "user-1",
            OrganizationUnitId = "dept-1",
            IsActive = true,
            IsPrimary = true,
            PositionId = "pos-1",
        };

        await _store.SaveMembershipAsync(membership);
        var result = await _store.GetMembershipsByUserAsync("user-1");

        result.Should().HaveCount(1);
        result[0].Id.Should().Be("m-1");
        result[0].PositionId.Should().Be("pos-1");

        // Clone check
        result[0].Should().NotBeSameAs(membership);
    }

    [Fact]
    public async Task GetMembershipsByOrganizationUnit_Returns_CorrectMemberships()
    {
        await _store.SaveMembershipAsync(new UserOrganizationMembership { Id = "m-1", UserId = "u1", OrganizationUnitId = "dept-1" });
        await _store.SaveMembershipAsync(new UserOrganizationMembership { Id = "m-2", UserId = "u2", OrganizationUnitId = "dept-1" });
        await _store.SaveMembershipAsync(new UserOrganizationMembership { Id = "m-3", UserId = "u3", OrganizationUnitId = "dept-2" });

        var result = await _store.GetMembershipsByOrganizationUnitAsync("dept-1");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAndGetRoleAssignment_Returns_CorrectAssignment()
    {
        var assignment = new UserOrganizationRoleAssignment { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = true };
        await _store.SaveRoleAssignmentAsync(assignment);

        var result = await _store.GetRoleAssignmentsByUserAsync("user-1");

        result.Should().HaveCount(1);
        result[0].RoleId.Should().Be("admin");

        // Clone check
        result[0].Should().NotBeSameAs(assignment);
    }

    [Fact]
    public async Task GetRoleAssignments_ReturnsEmpty_WhenNoAssignments()
    {
        var result = await _store.GetRoleAssignmentsByUserAsync("user-unknown");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveAndGetPosition_Works()
    {
        var position = new Position { Id = "pos-1", Name = "Manager", Code = "MGR" };
        await _store.SavePositionAsync(position);

        var result = await _store.GetPositionByIdAsync("pos-1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Manager");
        result.Should().NotBeSameAs(position);
    }

    [Fact]
    public async Task GetPositions_FiltersByTenant()
    {
        await _store.SavePositionAsync(new Position { Id = "pos-1", Name = "MGR", TenantId = "t1" });
        await _store.SavePositionAsync(new Position { Id = "pos-2", Name = "DEV", TenantId = "t2" });

        var result = await _store.GetPositionsAsync("t1");
        result.Should().HaveCount(1);
        result[0].Id.Should().Be("pos-1");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail/pass**

```bash
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~InMemoryOrganizationStoreTests"
```
Expected: All 11 tests pass (store was already built in Task 12).

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Organization.Tests/
git commit -m "test(org): add InMemoryOrganizationStore tests (11 tests)"
```

---

### Task 19: Add OrganizationHierarchyService tests

**Files:**
- Create: `framework/test/CrestCreates.Organization.Tests/OrganizationHierarchyServiceTests.cs`

- [ ] **Step 1: Write OrganizationHierarchyServiceTests.cs**

Write `framework/test/CrestCreates.Organization.Tests/OrganizationHierarchyServiceTests.cs`:

```csharp
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class OrganizationHierarchyServiceTests
{
    private static async Task<DefaultOrganizationHierarchyService> CreateServiceAsync(
        List<OrganizationUnit> orgUnits,
        List<UserOrganizationMembership>? memberships = null)
    {
        var store = new InMemoryOrganizationStore();
        foreach (var unit in orgUnits)
            await store.SaveOrganizationUnitAsync(unit);
        if (memberships is not null)
            foreach (var m in memberships)
                await store.SaveMembershipAsync(m);
        return new DefaultOrganizationHierarchyService(store);
    }

    [Fact]
    public async Task GetAncestors_ReturnsParentChain()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "root", Name = "Root" },
            new() { Id = "dept", Name = "Department", ParentId = "root" },
            new() { Id = "team", Name = "Team", ParentId = "dept" },
        };
        var service = await CreateServiceAsync(orgUnits);

        var ancestors = await service.GetAncestorsAsync("team");

        ancestors.Should().HaveCount(2);
        ancestors[0].Id.Should().Be("dept");
        ancestors[1].Id.Should().Be("root");
    }

    [Fact]
    public async Task GetAncestors_ReturnsEmpty_WhenNoParent()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "root", Name = "Root" } };
        var service = await CreateServiceAsync(orgUnits);

        var ancestors = await service.GetAncestorsAsync("root");

        ancestors.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDescendants_ReturnsChildren()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "root", Name = "Root" },
            new() { Id = "dept1", Name = "Dept1", ParentId = "root" },
            new() { Id = "team1", Name = "Team1", ParentId = "dept1" },
            new() { Id = "dept2", Name = "Dept2", ParentId = "root" },
        };
        var service = await CreateServiceAsync(orgUnits);

        var descendants = await service.GetDescendantsAsync("root");

        descendants.Should().HaveCount(3);
        descendants.Select(d => d.Id).Should().Contain(["dept1", "team1", "dept2"]);
    }

    [Fact]
    public async Task GetDescendants_ReturnsEmpty_WhenLeafNode()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "root", Name = "Root" },
            new() { Id = "leaf", Name = "Leaf", ParentId = "root" },
        };
        var service = await CreateServiceAsync(orgUnits);

        var descendants = await service.GetDescendantsAsync("leaf");

        descendants.Should().BeEmpty();
    }

    [Fact]
    public async Task IsDescendantOf_ReturnsTrue()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "root", Name = "Root" },
            new() { Id = "dept", Name = "Dept", ParentId = "root" },
            new() { Id = "team", Name = "Team", ParentId = "dept" },
        };
        var service = await CreateServiceAsync(orgUnits);

        var isDescendant = await service.IsDescendantOfAsync("team", "root");

        isDescendant.Should().BeTrue();
    }

    [Fact]
    public async Task IsDescendantOf_ReturnsFalse_WhenNotDescendant()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "root", Name = "Root" },
            new() { Id = "dept", Name = "Dept", ParentId = "root" },
        };
        var service = await CreateServiceAsync(orgUnits);

        var isDescendant = await service.IsDescendantOfAsync("root", "dept");

        isDescendant.Should().BeFalse();
    }

    [Fact]
    public async Task IsDescendantOf_ReturnsFalse_WhenSame()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "root", Name = "Root" } };
        var service = await CreateServiceAsync(orgUnits);

        var isDescendant = await service.IsDescendantOfAsync("root", "root");

        isDescendant.Should().BeFalse();
    }

    [Fact]
    public async Task GetAncestors_DetectsCycle_ThrowsHierarchyException()
    {
        // A -> B -> C -> A (cycle)
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "a", Name = "A", ParentId = "c" },
            new() { Id = "b", Name = "B", ParentId = "a" },
            new() { Id = "c", Name = "C", ParentId = "b" },
        };
        var service = await CreateServiceAsync(orgUnits);

        var act = async () => await service.GetAncestorsAsync("a");

        await act.Should().ThrowAsync<OrganizationHierarchyException>()
            .WithMessage("*Circular hierarchy*");
    }

    [Fact]
    public async Task GetDescendants_DetectsCycle_ThrowsHierarchyException()
    {
        // A -> B -> C -> A (cycle)
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "a", Name = "A", ParentId = "c" },
            new() { Id = "b", Name = "B", ParentId = "a" },
            new() { Id = "c", Name = "C", ParentId = "b" },
        };
        var service = await CreateServiceAsync(orgUnits);

        var act = async () => await service.GetDescendantsAsync("a");

        await act.Should().ThrowAsync<OrganizationHierarchyException>()
            .WithMessage("*Circular hierarchy*");
    }

    [Fact]
    public async Task IsUserInOrganization_ReturnsTrue_WhenActiveMember()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "dept", Name = "Dept" } };
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept", IsActive = true },
        };
        var service = await CreateServiceAsync(orgUnits, memberships);

        var isIn = await service.IsUserInOrganizationAsync("user-1", "dept");

        isIn.Should().BeTrue();
    }

    [Fact]
    public async Task IsUserInOrganization_ReturnsFalse_WhenInactiveMember()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "dept", Name = "Dept" } };
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept", IsActive = false },
        };
        var service = await CreateServiceAsync(orgUnits, memberships);

        var isIn = await service.IsUserInOrganizationAsync("user-1", "dept");

        isIn.Should().BeFalse();
    }

    [Fact]
    public async Task IsUserInOrganization_ReturnsFalse_WhenNotMember()
    {
        var orgUnits = new List<OrganizationUnit> { new() { Id = "dept", Name = "Dept" } };
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "other", IsActive = true },
        };
        var service = await CreateServiceAsync(orgUnits, memberships);

        var isIn = await service.IsUserInOrganizationAsync("user-1", "dept");

        isIn.Should().BeFalse();
    }

    [Fact]
    public async Task IsUserInDescendantOrganization_ReturnsTrue_WhenUserInDescendant()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "root", Name = "Root" },
            new() { Id = "dept", Name = "Dept", ParentId = "root" },
            new() { Id = "team", Name = "Team", ParentId = "dept" },
        };
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "team", IsActive = true },
        };
        var service = await CreateServiceAsync(orgUnits, memberships);

        var isIn = await service.IsUserInDescendantOrganizationAsync("user-1", "root");

        isIn.Should().BeTrue();
    }

    [Fact]
    public async Task IsUserInDescendantOrganization_ReturnsFalse_WhenUserInUnrelatedOrg()
    {
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "root", Name = "Root" },
            new() { Id = "dept", Name = "Dept", ParentId = "root" },
            new() { Id = "other", Name = "OtherRoot" },
        };
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "other", IsActive = true },
        };
        var service = await CreateServiceAsync(orgUnits, memberships);

        var isIn = await service.IsUserInDescendantOrganizationAsync("user-1", "root");

        isIn.Should().BeFalse();
    }

    [Fact]
    public async Task GetAncestors_IsolatesByTenant()
    {
        // Same org unit ID "dept" exists in two tenants, each with different parents.
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "dept", Name = "Dept-T1", TenantId = "t1", ParentId = "root-t1" },
            new() { Id = "root-t1", Name = "Root-T1", TenantId = "t1" },
            new() { Id = "dept", Name = "Dept-T2", TenantId = "t2", ParentId = "root-t2" },
            new() { Id = "root-t2", Name = "Root-T2", TenantId = "t2" },
        };
        var service = await CreateServiceAsync(orgUnits);

        var ancestors = await service.GetAncestorsAsync("dept", "t1");

        ancestors.Should().HaveCount(1);
        ancestors[0].Id.Should().Be("root-t1");
        ancestors[0].TenantId.Should().Be("t1");
    }

    [Fact]
    public async Task GetAncestors_CrossTenantParent_Excluded()
    {
        // Org unit's parent belongs to different tenant — treated as no parent (no cross-tenant leakage).
        var orgUnits = new List<OrganizationUnit>
        {
            new() { Id = "dept", Name = "Dept", TenantId = "t1", ParentId = "root-t2" },
            new() { Id = "root-t2", Name = "Root-T2", TenantId = "t2" },
        };
        var service = await CreateServiceAsync(orgUnits);

        var ancestors = await service.GetAncestorsAsync("dept", "t1");

        // root-t2 is not in t1 scope, so it's not found — ancestors should be empty
        ancestors.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~OrganizationHierarchyServiceTests"
```
Expected: All 16 tests pass.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Organization.Tests/
git commit -m "test(org): add OrganizationHierarchyService tests (16 tests)"
```

---

### Task 20: Add OrganizationIdentityService tests

**Files:**
- Create: `framework/test/CrestCreates.Organization.Tests/OrganizationIdentityServiceTests.cs`

- [ ] **Step 1: Write OrganizationIdentityServiceTests.cs**

Write `framework/test/CrestCreates.Organization.Tests/OrganizationIdentityServiceTests.cs`:

```csharp
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class OrganizationIdentityServiceTests
{
    private static async Task<DefaultOrganizationIdentityService> CreateServiceAsync(
        List<UserOrganizationMembership>? memberships = null,
        List<UserOrganizationRoleAssignment>? roleAssignments = null)
    {
        var store = new InMemoryOrganizationStore();
        if (memberships is not null)
            foreach (var m in memberships)
                await store.SaveMembershipAsync(m);
        if (roleAssignments is not null)
            foreach (var r in roleAssignments)
                await store.SaveRoleAssignmentAsync(r);
        return new DefaultOrganizationIdentityService(store);
    }

    [Fact]
    public async Task GetContext_ReturnsOrganizationsRolesPositions()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true, PositionId = "pos-1", CreatedAt = DateTimeOffset.UtcNow.AddDays(-1) },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-2", IsActive = true, PositionId = "pos-2", CreatedAt = DateTimeOffset.UtcNow },
        };
        var roleAssignments = new List<UserOrganizationRoleAssignment>
        {
            new() { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = true },
            new() { Id = "ra-2", UserId = "user-1", RoleId = "user", IsActive = true },
            new() { Id = "ra-3", UserId = "user-1", RoleId = "inactive-role", IsActive = false },
        };
        var service = await CreateServiceAsync(memberships, roleAssignments);

        var context = await service.GetContextAsync("user-1");

        context.UserId.Should().Be("user-1");
        context.PrimaryOrganizationUnitId.Should().Be("dept-1");
        context.OrganizationUnitIds.Should().BeEquivalentTo(["dept-1", "dept-2"]);
        context.RoleIds.Should().BeEquivalentTo(["admin", "user"]);
        context.PositionIds.Should().BeEquivalentTo(["pos-1", "pos-2"]);
    }

    [Fact]
    public async Task GetContext_DeduplicatesIds()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true, PositionId = "pos-1" },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true, PositionId = "pos-1" },
        };
        var service = await CreateServiceAsync(memberships);

        var context = await service.GetContextAsync("user-1");

        context.OrganizationUnitIds.Should().HaveCount(1);
        context.PositionIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetContext_PrimaryUnitIsNull_WhenNoPrimary()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = false, IsActive = true },
        };
        var service = await CreateServiceAsync(memberships);

        var context = await service.GetContextAsync("user-1");

        context.PrimaryOrganizationUnitId.Should().BeNull();
    }

    [Fact]
    public async Task GetContext_ExcludesInactiveMemberships()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-2", IsActive = false },
        };
        var service = await CreateServiceAsync(memberships);

        var context = await service.GetContextAsync("user-1");

        context.OrganizationUnitIds.Should().BeEquivalentTo(["dept-1"]);
    }

    [Fact]
    public async Task IsInRole_ReturnsTrue_WhenActiveAssignment()
    {
        var roleAssignments = new List<UserOrganizationRoleAssignment>
        {
            new() { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = true },
        };
        var service = await CreateServiceAsync(roleAssignments: roleAssignments);

        var isIn = await service.IsInRoleAsync("user-1", "admin");

        isIn.Should().BeTrue();
    }

    [Fact]
    public async Task IsInRole_ReturnsFalse_WhenInactiveAssignment()
    {
        var roleAssignments = new List<UserOrganizationRoleAssignment>
        {
            new() { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = false },
        };
        var service = await CreateServiceAsync(roleAssignments: roleAssignments);

        var isIn = await service.IsInRoleAsync("user-1", "admin");

        isIn.Should().BeFalse();
    }

    [Fact]
    public async Task IsInRole_ReturnsFalse_WhenNoAssignment()
    {
        var service = await CreateServiceAsync();

        var isIn = await service.IsInRoleAsync("user-1", "admin");

        isIn.Should().BeFalse();
    }

    [Fact]
    public async Task HasPosition_ReturnsTrue_WhenActiveMembership()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", PositionId = "pos-1", IsActive = true },
        };
        var service = await CreateServiceAsync(memberships);

        var has = await service.HasPositionAsync("user-1", "pos-1");

        has.Should().BeTrue();
    }

    [Fact]
    public async Task HasPosition_ReturnsFalse_WhenInactiveMembership()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", PositionId = "pos-1", IsActive = false },
        };
        var service = await CreateServiceAsync(memberships);

        var has = await service.HasPositionAsync("user-1", "pos-1");

        has.Should().BeFalse();
    }

    [Fact]
    public async Task HasPosition_ReturnsFalse_WhenNoPosition()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true },
        };
        var service = await CreateServiceAsync(memberships);

        var has = await service.HasPositionAsync("user-1", "pos-unknown");

        has.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserOrganizationUnitIds_ReturnsDistinctActive()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-1", IsActive = true },
            new() { Id = "m-3", UserId = "user-1", OrganizationUnitId = "dept-2", IsActive = false },
        };
        var service = await CreateServiceAsync(memberships);

        var ids = await service.GetUserOrganizationUnitIdsAsync("user-1");

        ids.Should().BeEquivalentTo(["dept-1"]);
    }

    [Fact]
    public async Task GetUserRoleIds_ReturnsDistinctActive()
    {
        var roleAssignments = new List<UserOrganizationRoleAssignment>
        {
            new() { Id = "ra-1", UserId = "user-1", RoleId = "admin", IsActive = true },
            new() { Id = "ra-2", UserId = "user-1", RoleId = "admin", IsActive = true },
            new() { Id = "ra-3", UserId = "user-1", RoleId = "user", IsActive = false },
        };
        var service = await CreateServiceAsync(roleAssignments: roleAssignments);

        var ids = await service.GetUserRoleIdsAsync("user-1");

        ids.Should().BeEquivalentTo(["admin"]);
    }

    [Fact]
    public async Task GetUserPositionIds_ReturnsDistinctActive()
    {
        var memberships = new List<UserOrganizationMembership>
        {
            new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", PositionId = "pos-1", IsActive = true },
            new() { Id = "m-2", UserId = "user-1", OrganizationUnitId = "dept-2", PositionId = "pos-1", IsActive = true },
            new() { Id = "m-3", UserId = "user-1", OrganizationUnitId = "dept-3", PositionId = "pos-2", IsActive = false },
        };
        var service = await CreateServiceAsync(memberships);

        var ids = await service.GetUserPositionIdsAsync("user-1");

        ids.Should().BeEquivalentTo(["pos-1"]);
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~OrganizationIdentityServiceTests"
```
Expected: All 13 tests pass.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Organization.Tests/
git commit -m "test(org): add OrganizationIdentityService tests (13 tests)"
```

---

### Task 21: Add DataPermissionScopeProvider tests

**Files:**
- Create: `framework/test/CrestCreates.Organization.Tests/DataPermissionScopeProviderTests.cs`

- [ ] **Step 1: Write DataPermissionScopeProviderTests.cs**

Write `framework/test/CrestCreates.Organization.Tests/DataPermissionScopeProviderTests.cs`:

```csharp
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class DataPermissionScopeProviderTests
{
    [Fact]
    public async Task GetScope_ReturnsSelf_WhenNoOrganization()
    {
        var store = new InMemoryOrganizationStore();
        var identityService = new DefaultOrganizationIdentityService(store);
        var provider = new DefaultDataPermissionScopeProvider(identityService);

        var scope = await provider.GetScopeAsync("user-1", "read:documents");

        scope.Kind.Should().Be(DataPermissionScopeKind.Self);
        scope.UserId.Should().Be("user-1");
        scope.OrganizationUnitId.Should().BeNull();
    }

    [Fact]
    public async Task GetScope_ReturnsOwnOrganization_WhenPrimaryExists()
    {
        var store = new InMemoryOrganizationStore();
        await store.SaveMembershipAsync(new UserOrganizationMembership
        {
            Id = "m-1",
            UserId = "user-1",
            OrganizationUnitId = "dept-1",
            IsPrimary = true,
            IsActive = true,
        });
        var identityService = new DefaultOrganizationIdentityService(store);
        var provider = new DefaultDataPermissionScopeProvider(identityService);

        var scope = await provider.GetScopeAsync("user-1", "read:documents");

        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
        scope.UserId.Should().Be("user-1");
        scope.OrganizationUnitId.Should().Be("dept-1");
    }
}
```

- [ ] **Step 2: Run tests**

```bash
dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~DataPermissionScopeProviderTests"
```
Expected: Both tests pass.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Organization.Tests/
git commit -m "test(org): add DataPermissionScopeProvider tests (2 tests)"
```

---

### Task 22: Full build and regression

- [ ] **Step 1: Build entire solution**

```bash
dotnet build
```
Expected: Build succeeded with 0 errors.

- [ ] **Step 2: Run Organization tests**

```bash
dotnet test framework/test/CrestCreates.Organization.Tests/
```
Expected: All ~40 tests pass (11 store + 14 hierarchy + 13 identity + 2 datascope).

- [ ] **Step 3: Run regression — HumanTask tests**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests/
```
Expected: All 21 tests pass. 0 failures.

- [ ] **Step 4: Run regression — Workflow tests**

```bash
dotnet test framework/test/CrestCreates.Workflow.Tests/
```
Expected: All 57 tests pass. 0 failures.

- [ ] **Step 5: Run regression — Capability tests**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/
```
Expected: All tests pass.

- [ ] **Step 6: Run regression — Metadata tests**

```bash
dotnet test framework/test/CrestCreates.Metadata.Tests/
```
Expected: All tests pass.

- [ ] **Step 7: Commit final status**

```bash
git add .
git commit -m "test(org): confirm full build and regression pass for Phase 5c"
```
(Only if there are any pending changes; otherwise skip.)

---

### Task 23: Update memory.md

**Files:**
- Modify: `memory.md`

- [ ] **Step 1: Update Organization status in memory.md**

Find the "组织架构权限" row in the Major TODO section and update it:

```
| 组织架构权限 | P2 | **部分完成** | ~~Organization 实体~~ + ~~OrganizationHierarchyService~~ + ~~DataPermissionFilter~~ + Phase 5c: Organization Identity Kernel (OrganizationUnit/Position/UserOrganizationMembership/UserOrganizationRoleAssignment models, IOrganizationStore/InMemory, IOrganizationHierarchyService with tenantId scoping, IOrganizationIdentityService, DataPermissionScope stub, 40+ tests). No OrganizationAppService, no database persistence, no API endpoints. |
```

- [ ] **Step 2: Commit**

```bash
git add memory.md
git commit -m "docs: update memory.md for Phase 5c Organization Identity Kernel"
```

---

## Summary

| # | Task | Files | Tests |
|---|------|-------|-------|
| 1 | Scaffold Abstractions .csproj | 1 create | — |
| 2 | Scaffold Organization .csproj | 1 create | — |
| 3 | OrganizationUnit + Position models | 2 create | — |
| 4 | Membership + RoleAssignment models | 2 create | — |
| 5 | Context + ContextAccessor | 2 create | — |
| 6 | Exceptions | 2 create | — |
| 7 | IOrganizationStore | 1 create | — |
| 8 | Hierarchy + Identity interfaces | 2 create | — |
| 9 | DataPermission models + interface | 3 create | — |
| 10 | Clone() methods (on models) | 4 modify | — |
| 11 | NullOrganizationContextAccessor | 1 create | — |
| 12 | InMemoryOrganizationStore | 1 create | — |
| 13 | DefaultOrganizationHierarchyService | 1 create | — |
| 14 | DefaultOrganizationIdentityService | 1 create | — |
| 15 | DefaultDataPermissionScopeProvider | 1 create | — |
| 16 | DI extension | 1 create | — |
| 17 | Scaffold test project | 2 create | — |
| 18 | Store tests | 1 create | 11 |
| 19 | Hierarchy tests | 1 create | 16 |
| 20 | Identity tests | 1 create | 13 |
| 21 | DataPermission tests | 1 create | 2 |
| 22 | Full build + regression | — | ~120 total |
| 23 | Update memory.md | 1 modify | — |

**Total**: ~20 created files, 5 modified files, 42 new tests, 0 expected regressions across 78 existing tests.
