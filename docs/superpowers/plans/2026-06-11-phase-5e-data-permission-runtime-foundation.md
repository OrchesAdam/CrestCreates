# Phase 5e — Data Permission Runtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish an organization-identity-driven data permission runtime that resolves `DataPermissionScope` from org identity + rule store, and converts it to an ORM-neutral `DataPermissionFilter` intermediate model.

**Architecture:** Three independently testable units (`IDataPermissionScopeProvider` + `IDataPermissionScopeRuleStore` for scope resolution, `IDataPermissionFilterBuilder` for filter construction) composed by a thin `IDataPermissionRuntime` facade. All types in existing `Organization.Abstractions` / `Organization` projects — no new projects.

**Tech Stack:** .NET 10, C# 14, xUnit, FluentAssertions, Moq (available, unused), ConcurrentDictionary, file-scoped namespaces, init-only properties, sealed classes.

**Design Spec:** `docs/superpowers/specs/2026-06-11-phase-5e-data-permission-runtime-foundation-design.md`

---

## File Structure

### Abstractions — New Files (8)

| File | Responsibility |
|------|---------------|
| `framework/src/CrestCreates.Organization.Abstractions/DataPermissionFilterOperator.cs` | Enum: Equal, In, True, False |
| `framework/src/CrestCreates.Organization.Abstractions/DataPermissionFilterRule.cs` | Single filter rule model |
| `framework/src/CrestCreates.Organization.Abstractions/DataPermissionFilter.cs` | AND-combined rule list + IsDenied/IsUnrestricted |
| `framework/src/CrestCreates.Organization.Abstractions/DataPermissionFieldMapping.cs` | Field name mapping bridge (UserIdField, OrgUnitIdField, TenantIdField) |
| `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionFilterBuilder.cs` | Build filter from scope + mapping |
| `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionRuntime.cs` | Composes scope resolution + filter building |
| `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeRequest.cs` | Input model for scope resolution |
| `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionScopeRuleStore.cs` | Tenant-aware scope rule store interface |
| `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeRule.cs` | Scope rule model |
| `framework/src/CrestCreates.Organization.Abstractions/DataPermissionAction.cs` | Static constants: Read, Create, Update, Delete, Query (Read alias) |

### Abstractions — Modified Files (3)

| File | Change |
|------|--------|
| `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScope.cs` | +TenantId, Resource, Action, Permission, IsEmpty, IsUnrestricted |
| `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeKind.cs` | +Custom = 5 |
| `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionScopeProvider.cs` | +GetScopeAsync(DataPermissionScopeRequest) overload |

### Implementation — New Files (3)

| File | Responsibility |
|------|---------------|
| `framework/src/CrestCreates.Organization/DefaultDataPermissionFilterBuilder.cs` | Fail-closed filter builder |
| `framework/src/CrestCreates.Organization/DefaultDataPermissionRuntime.cs` | Facade delegating to provider + builder |
| `framework/src/CrestCreates.Organization/InMemoryDataPermissionScopeRuleStore.cs` | ConcurrentDictionary-based rule store |

### Implementation — Modified Files (2)

| File | Change |
|------|--------|
| `framework/src/CrestCreates.Organization/DefaultDataPermissionScopeProvider.cs` | +IOrganizationHierarchyService, +IDataPermissionScopeRuleStore deps; full resolution algorithm |
| `framework/src/CrestCreates.Organization/OrganizationServiceCollectionExtensions.cs` | +3 new registrations |

### Tests — New Files (3)

| File | Tests |
|------|-------|
| `framework/test/CrestCreates.Organization.Tests/DataPermissionFilterBuilderTests.cs` | F1–F12 |
| `framework/test/CrestCreates.Organization.Tests/DataPermissionRuntimeTests.cs` | R1–R3 |
| `framework/test/CrestCreates.Organization.Tests/InMemoryDataPermissionScopeRuleStoreTests.cs` | S1–S5 |

### Tests — Modified Files (1)

| File | Change |
|------|--------|
| `framework/test/CrestCreates.Organization.Tests/DataPermissionScopeProviderTests.cs` | Replace 2 stub tests with D1–D12 |

---

### Task 1: Create All New Contract Types in Organization.Abstractions

**Files:**
- Create: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionFilterOperator.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionFilterRule.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionFilter.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionFieldMapping.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionFilterBuilder.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionRuntime.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeRequest.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionScopeRuleStore.cs`
- Create: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionAction.cs`

- [ ] **Step 1: Create `DataPermissionFilterOperator.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

public enum DataPermissionFilterOperator
{
    Equal,
    In
}
```

- [ ] **Step 2: Create `DataPermissionFilterRule.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionFilterRule
{
    public required string FieldName { get; init; }
    public DataPermissionFilterOperator Operator { get; init; }
    public string? Value { get; init; }
    public IReadOnlyList<string> Values { get; init; } = Array.Empty<string>();
}
```

- [ ] **Step 3: Create `DataPermissionFilter.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionFilter
{
    public bool IsDenied { get; init; }
    public bool IsUnrestricted { get; init; }
    public IReadOnlyList<DataPermissionFilterRule> Rules { get; init; } = Array.Empty<DataPermissionFilterRule>();
}
```

- [ ] **Step 4: Create `DataPermissionFieldMapping.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionFieldMapping
{
    public string? UserIdField { get; init; }
    public string? OrganizationUnitIdField { get; init; }
    public string? TenantIdField { get; init; }

    public bool HasUserIdField => !string.IsNullOrEmpty(UserIdField);
    public bool HasOrganizationUnitIdField => !string.IsNullOrEmpty(OrganizationUnitIdField);
    public bool HasTenantIdField => !string.IsNullOrEmpty(TenantIdField);
}
```

- [ ] **Step 5: Create `IDataPermissionFilterBuilder.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

public interface IDataPermissionFilterBuilder
{
    DataPermissionFilter Build(DataPermissionScope scope, DataPermissionFieldMapping mapping);
}
```

- [ ] **Step 6: Create `IDataPermissionRuntime.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

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

- [ ] **Step 7: Create `DataPermissionScopeRequest.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionScopeRequest
{
    public required string UserId { get; init; }
    public string? TenantId { get; init; }
    public string? Permission { get; init; }
    public string? Resource { get; init; }
    public string? Action { get; init; }
}
```

- [ ] **Step 8: Create `IDataPermissionScopeRuleStore.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

public interface IDataPermissionScopeRuleStore
{
    Task<DataPermissionScopeKind?> GetScopeKindAsync(
        string resource,
        string? action,
        string? permission,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    Task SaveRuleAsync(
        DataPermissionScopeRule rule,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 8a: Create `DataPermissionScopeRule.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionScopeRule
{
    public required string Resource { get; init; }
    public string? Action { get; init; }
    public string? Permission { get; init; }
    public string? TenantId { get; init; }
    public DataPermissionScopeKind ScopeKind { get; init; }
}
```

- [ ] **Step 9: Create `DataPermissionAction.cs`**

```csharp
namespace CrestCreates.Organization.Abstractions;

public static class DataPermissionAction
{
    public const string None = nameof(None);
    public const string Read = nameof(Read);
    public const string Create = nameof(Create);
    public const string Update = nameof(Update);
    public const string Delete = nameof(Delete);
    public const string Query = nameof(Query);
}
```

- [ ] **Step 10: Build Abstractions to verify no compile errors**

Run: `dotnet build framework/src/CrestCreates.Organization.Abstractions/`
Expected: Build succeeded. 0 Error(s).

- [ ] **Step 11: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/DataPermissionFilterOperator.cs \
        framework/src/CrestCreates.Organization.Abstractions/DataPermissionFilterRule.cs \
        framework/src/CrestCreates.Organization.Abstractions/DataPermissionFilter.cs \
        framework/src/CrestCreates.Organization.Abstractions/DataPermissionFieldMapping.cs \
        framework/src/CrestCreates.Organization.Abstractions/IDataPermissionFilterBuilder.cs \
        framework/src/CrestCreates.Organization.Abstractions/IDataPermissionRuntime.cs \
        framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeRequest.cs \
        framework/src/CrestCreates.Organization.Abstractions/IDataPermissionScopeRuleStore.cs \
        framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeRule.cs \
        framework/src/CrestCreates.Organization.Abstractions/DataPermissionAction.cs
git commit -m "feat(Phase5e): add DataPermissionFilter model + IDataPermissionRuntime + scope rule store contracts"
```

---

### Task 2: Enhance Existing Contracts in Organization.Abstractions

**Files:**
- Modify: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScope.cs`
- Modify: `framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeKind.cs`
- Modify: `framework/src/CrestCreates.Organization.Abstractions/IDataPermissionScopeProvider.cs`

- [ ] **Step 1: Enhance `DataPermissionScope.cs`**

Replace the entire file content:

```csharp
namespace CrestCreates.Organization.Abstractions;

public sealed class DataPermissionScope
{
    public DataPermissionScopeKind Kind { get; init; }
    public string? UserId { get; init; }
    public string? TenantId { get; init; }
    public string? Resource { get; init; }
    public string? Action { get; init; }
    public string? Permission { get; init; }
    public string? OrganizationUnitId { get; init; }
    public IReadOnlyList<string> OrganizationUnitIds { get; init; } = Array.Empty<string>();

    public bool IsEmpty => Kind == DataPermissionScopeKind.None;
    public bool IsUnrestricted => Kind == DataPermissionScopeKind.All;
}
```

- [ ] **Step 2: Add `Custom` to `DataPermissionScopeKind.cs`**

Old content:
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

Replace with:
```csharp
namespace CrestCreates.Organization.Abstractions;

public enum DataPermissionScopeKind
{
    None,
    Self,
    OwnOrganization,
    OwnOrganizationAndDescendants,
    All,
    Custom
}
```

- [ ] **Step 3: Add new overload to `IDataPermissionScopeProvider.cs`**

Old content:
```csharp
namespace CrestCreates.Organization.Abstractions;

public interface IDataPermissionScopeProvider
{
    Task<DataPermissionScope> GetScopeAsync(string userId, string permission, string? tenantId = null, CancellationToken cancellationToken = default);
}
```

Replace with:
```csharp
namespace CrestCreates.Organization.Abstractions;

public interface IDataPermissionScopeProvider
{
    Task<DataPermissionScope> GetScopeAsync(
        DataPermissionScopeRequest request,
        CancellationToken cancellationToken = default);

    Task<DataPermissionScope> GetScopeAsync(
        string userId, string permission, string? tenantId = null,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Build Abstractions to verify compile**

Run: `dotnet build framework/src/CrestCreates.Organization.Abstractions/`
Expected: Build succeeded. 0 Error(s).

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Organization.Abstractions/DataPermissionScope.cs \
        framework/src/CrestCreates.Organization.Abstractions/DataPermissionScopeKind.cs \
        framework/src/CrestCreates.Organization.Abstractions/IDataPermissionScopeProvider.cs
git commit -m "feat(Phase5e): enhance DataPermissionScope + DataPermissionScopeKind + IDataPermissionScopeProvider"
```

---

### Task 3: Write Filter Builder Tests (TDD — red phase)

**Files:**
- Create: `framework/test/CrestCreates.Organization.Tests/DataPermissionFilterBuilderTests.cs`

- [ ] **Step 1: Create test file with all 13 failing tests**

```csharp
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class DataPermissionFilterBuilderTests
{
    private static readonly DefaultDataPermissionFilterBuilder _builder = new();

    [Fact]
    public void Build_NoneScope_ReturnsDenied()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.None };
        var mapping = new DataPermissionFieldMapping();
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue();
        filter.IsUnrestricted.Should().BeFalse();
        filter.Rules.Should().BeEmpty();
    }

    [Fact]
    public void Build_AllScope_ReturnsUnrestricted()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.All };
        var mapping = new DataPermissionFieldMapping();
        var filter = _builder.Build(scope, mapping);
        filter.IsUnrestricted.Should().BeTrue();
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().BeEmpty();
    }

    [Fact]
    public void Build_SelfScope_WithUserIdField_ReturnsEqualRule()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.Self, UserId = "user-1" };
        var mapping = new DataPermissionFieldMapping { UserIdField = "CreatorId" };
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeFalse();
        filter.IsUnrestricted.Should().BeFalse();
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("CreatorId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[0].Value.Should().Be("user-1");
    }

    [Fact]
    public void Build_SelfScope_WithoutUserIdField_ReturnsDenied()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.Self, UserId = "user-1" };
        var mapping = new DataPermissionFieldMapping(); // no UserIdField
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue();
    }

    [Fact]
    public void Build_OwnOrganization_ReturnsEqualRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1"
        };
        var mapping = new DataPermissionFieldMapping { OrganizationUnitIdField = "OrgUnitId" };
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("OrgUnitId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[0].Value.Should().Be("dept-1");
    }

    [Fact]
    public void Build_OwnOrganization_WithoutOrgField_ReturnsDenied()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1"
        };
        var mapping = new DataPermissionFieldMapping(); // no OrganizationUnitIdField
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue();
    }

    [Fact]
    public void Build_OwnOrganizationAndDescendants_ReturnsInRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganizationAndDescendants,
            OrganizationUnitIds = new[] { "dept-1", "team-3", "team-4" }
        };
        var mapping = new DataPermissionFieldMapping { OrganizationUnitIdField = "OrgUnitId" };
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("OrgUnitId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.In);
        filter.Rules[0].Values.Should().BeEquivalentTo(new[] { "dept-1", "team-3", "team-4" });
    }

    [Fact]
    public void Build_OwnOrganizationAndDescendants_WithoutOrgField_ReturnsDenied()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganizationAndDescendants,
            OrganizationUnitIds = new[] { "dept-1" }
        };
        var mapping = new DataPermissionFieldMapping(); // no OrganizationUnitIdField
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue();
    }

    [Fact]
    public void Build_WithTenantIdField_AppendsTenantRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1",
            TenantId = "tenant-A"
        };
        var mapping = new DataPermissionFieldMapping
        {
            OrganizationUnitIdField = "OrgUnitId",
            TenantIdField = "TenantId"
        };
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().HaveCount(2);
        filter.Rules[1].FieldName.Should().Be("TenantId");
        filter.Rules[1].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[1].Value.Should().Be("tenant-A");
    }

    [Fact]
    public void Build_WithTenantIdField_ButNullTenantId_SkipsTenantRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1",
            TenantId = null
        };
        var mapping = new DataPermissionFieldMapping
        {
            OrganizationUnitIdField = "OrgUnitId",
            TenantIdField = "TenantId"
        };
        var filter = _builder.Build(scope, mapping);
        filter.Rules.Should().HaveCount(1); // only scope rule, no tenant rule
    }

    [Fact]
    public void Build_WithoutTenantIdField_SkipsTenantRule()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            OrganizationUnitId = "dept-1",
            TenantId = "tenant-A"
        };
        var mapping = new DataPermissionFieldMapping
        {
            OrganizationUnitIdField = "OrgUnitId"
            // no TenantIdField
        };
        var filter = _builder.Build(scope, mapping);
        filter.Rules.Should().HaveCount(1); // only scope rule
    }

    [Fact]
    public void Build_AllScope_WithTenantId_ReturnsTenantScoped()
    {
        var scope = new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.All,
            TenantId = "tenant-A"
        };
        var mapping = new DataPermissionFieldMapping { TenantIdField = "TenantId" };
        var filter = _builder.Build(scope, mapping);

        // NOT unrestricted — tenant scoping tightens the filter
        filter.IsUnrestricted.Should().BeFalse();
        filter.IsDenied.Should().BeFalse();
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("TenantId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[0].Value.Should().Be("tenant-A");
    }

    [Fact]
    public void Build_CustomScope_ReturnsDenied()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.Custom };
        var mapping = new DataPermissionFieldMapping();
        var filter = _builder.Build(scope, mapping);
        filter.IsDenied.Should().BeTrue(); // unknown → fail closed
    }
}
```

- [ ] **Step 2: Run tests to confirm they FAIL (no implementation)**

Run: `dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~DataPermissionFilterBuilderTests"`
Expected: Build FAILED — `DefaultDataPermissionFilterBuilder` not found.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Organization.Tests/DataPermissionFilterBuilderTests.cs
git commit -m "test(Phase5e): add DataPermissionFilterBuilderTests (12 tests, red)"
```

---

### Task 4: Implement DefaultDataPermissionFilterBuilder (TDD — green phase)

**Files:**
- Create: `framework/src/CrestCreates.Organization/DefaultDataPermissionFilterBuilder.cs`

- [ ] **Step 1: Implement `DefaultDataPermissionFilterBuilder.cs`**

```csharp
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultDataPermissionFilterBuilder : IDataPermissionFilterBuilder
{
    public DataPermissionFilter Build(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        return scope.Kind switch
        {
            DataPermissionScopeKind.None => new DataPermissionFilter { IsDenied = true },
            DataPermissionScopeKind.All => BuildAll(scope, mapping),
            DataPermissionScopeKind.Self => BuildSelf(scope, mapping),
            DataPermissionScopeKind.OwnOrganization => BuildOwnOrganization(scope, mapping),
            DataPermissionScopeKind.OwnOrganizationAndDescendants => BuildOwnOrganizationAndDescendants(scope, mapping),
            _ => new DataPermissionFilter { IsDenied = true }, // Custom / unknown → fail closed
        };
    }

    private static DataPermissionFilter BuildAll(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (mapping.HasTenantIdField && scope.TenantId is not null)
        {
            return new DataPermissionFilter
            {
                IsUnrestricted = false, // tenant scoping tightens
                Rules = new[]
                {
                    new DataPermissionFilterRule
                    {
                        FieldName = mapping.TenantIdField!,
                        Operator = DataPermissionFilterOperator.Equal,
                        Value = scope.TenantId
                    }
                }
            };
        }

        return new DataPermissionFilter { IsUnrestricted = true };
    }

    private static DataPermissionFilter BuildSelf(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (!mapping.HasUserIdField)
            return new DataPermissionFilter { IsDenied = true };

        var rules = new List<DataPermissionFilterRule>
        {
            new()
            {
                FieldName = mapping.UserIdField!,
                Operator = DataPermissionFilterOperator.Equal,
                Value = scope.UserId
            }
        };
        AppendTenantRule(rules, scope, mapping);
        return new DataPermissionFilter { Rules = rules };
    }

    private static DataPermissionFilter BuildOwnOrganization(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (!mapping.HasOrganizationUnitIdField || scope.OrganizationUnitId is null)
            return new DataPermissionFilter { IsDenied = true };

        var rules = new List<DataPermissionFilterRule>
        {
            new()
            {
                FieldName = mapping.OrganizationUnitIdField!,
                Operator = DataPermissionFilterOperator.Equal,
                Value = scope.OrganizationUnitId
            }
        };
        AppendTenantRule(rules, scope, mapping);
        return new DataPermissionFilter { Rules = rules };
    }

    private static DataPermissionFilter BuildOwnOrganizationAndDescendants(DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (!mapping.HasOrganizationUnitIdField || scope.OrganizationUnitIds.Count == 0)
            return new DataPermissionFilter { IsDenied = true };

        var rules = new List<DataPermissionFilterRule>
        {
            new()
            {
                FieldName = mapping.OrganizationUnitIdField!,
                Operator = DataPermissionFilterOperator.In,
                Values = scope.OrganizationUnitIds
            }
        };
        AppendTenantRule(rules, scope, mapping);
        return new DataPermissionFilter { Rules = rules };
    }

    private static void AppendTenantRule(List<DataPermissionFilterRule> rules, DataPermissionScope scope, DataPermissionFieldMapping mapping)
    {
        if (mapping.HasTenantIdField && scope.TenantId is not null)
        {
            rules.Add(new DataPermissionFilterRule
            {
                FieldName = mapping.TenantIdField!,
                Operator = DataPermissionFilterOperator.Equal,
                Value = scope.TenantId
            });
        }
    }
}
```

- [ ] **Step 2: Run filter builder tests**

Run: `dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~DataPermissionFilterBuilderTests"`
Expected: All 13 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Organization/DefaultDataPermissionFilterBuilder.cs
git commit -m "feat(Phase5e): implement DefaultDataPermissionFilterBuilder with fail-closed rules"
```

---

### Task 5: Write Rule Store Tests + Implement (TDD)

**Files:**
- Create: `framework/test/CrestCreates.Organization.Tests/InMemoryDataPermissionScopeRuleStoreTests.cs`
- Create: `framework/src/CrestCreates.Organization/InMemoryDataPermissionScopeRuleStore.cs`

- [ ] **Step 1: Create rule store test file (7 tests)**

```csharp
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class InMemoryDataPermissionScopeRuleStoreTests
{
    [Fact]
    public async Task GetScopeKind_MatchesExactRule()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = "books.read",
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.OwnOrganization
        });
        var result = await store.GetScopeKindAsync("Book", "Read", "books.read", "t-A");
        result.Should().Be(DataPermissionScopeKind.OwnOrganization);
    }

    [Fact]
    public async Task GetScopeKind_FallsBackToWildcardPermission()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.OwnOrganization
        });
        var result = await store.GetScopeKindAsync("Book", "Read", "any.permission", "t-A");
        result.Should().Be(DataPermissionScopeKind.OwnOrganization);
    }

    [Fact]
    public async Task GetScopeKind_FallsBackToWildcardActionAndPermission()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = null, Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.Self
        });
        var result = await store.GetScopeKindAsync("Book", "Write", "any.permission", "t-A");
        result.Should().Be(DataPermissionScopeKind.Self);
    }

    [Fact]
    public async Task GetScopeKind_ReturnsNull_WhenNoRule()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        var result = await store.GetScopeKindAsync("Book", "Read", "p", "t-A");
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetScopeKind_PrefersMoreSpecificRule()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = null, Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.OwnOrganization
        });
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All
        });
        var result = await store.GetScopeKindAsync("Book", "Read", "p", "t-A");
        result.Should().Be(DataPermissionScopeKind.All); // more specific wins
    }

    [Fact]
    public async Task GetScopeKind_TenantRuleOverridesGlobalRule()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        // Global rule
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = null, ScopeKind = DataPermissionScopeKind.Self
        });
        // Tenant-specific rule
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All
        });
        // Query with matching tenant → tenant rule wins
        var result = await store.GetScopeKindAsync("Book", "Read", "p", "t-A");
        result.Should().Be(DataPermissionScopeKind.All);
    }

    [Fact]
    public async Task GetScopeKind_OtherTenantRuleDoesNotApply()
    {
        var store = new InMemoryDataPermissionScopeRuleStore();
        await store.SaveRuleAsync(new DataPermissionScopeRule
        {
            Resource = "Book", Action = "Read", Permission = null,
            TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All
        });
        // Different tenant queries → no match
        var result = await store.GetScopeKindAsync("Book", "Read", "p", "t-B");
        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to confirm FAIL**

Run: `dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~InMemoryDataPermissionScopeRuleStoreTests"`
Expected: Build FAILED — `InMemoryDataPermissionScopeRuleStore` SaveRuleAsync not matching interface.

- [ ] **Step 3: Implement `InMemoryDataPermissionScopeRuleStore.cs`**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class InMemoryDataPermissionScopeRuleStore : IDataPermissionScopeRuleStore
{
    private readonly ConcurrentDictionary<string, DataPermissionScopeKind> _rules = new();

    public Task<DataPermissionScopeKind?> GetScopeKindAsync(
        string resource,
        string? action,
        string? permission,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        // Match priority: tenant-exact > global-exact > tenant-wildcard-perm > global-wildcard-perm
        //                > tenant-wildcard-action > global-wildcard-action
        var keys = new[]
        {
            $"{resource}::{action ?? "*"}::{permission ?? "*"}::{tenantId ?? "*"}",
            $"{resource}::{action ?? "*"}::{permission ?? "*"}::*",
            $"{resource}::{action ?? "*"}::*::{tenantId ?? "*"}",
            $"{resource}::{action ?? "*"}::*::*",
            $"{resource}::*::*::{tenantId ?? "*"}",
            $"{resource}::*::*::*",
        };

        foreach (var key in keys)
        {
            if (_rules.TryGetValue(key, out var kind))
                return Task.FromResult<DataPermissionScopeKind?>(kind);
        }

        return Task.FromResult<DataPermissionScopeKind?>(null);
    }

    public Task SaveRuleAsync(
        DataPermissionScopeRule rule,
        CancellationToken cancellationToken = default)
    {
        var key = $"{rule.Resource}::{rule.Action ?? "*"}::{rule.Permission ?? "*"}::{rule.TenantId ?? "*"}";
        _rules[key] = rule.ScopeKind;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run rule store tests**

Run: `dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~InMemoryDataPermissionScopeRuleStoreTests"`
Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Organization.Tests/InMemoryDataPermissionScopeRuleStoreTests.cs \
        framework/src/CrestCreates.Organization/InMemoryDataPermissionScopeRuleStore.cs
git commit -m "feat(Phase5e): implement tenant-aware InMemoryDataPermissionScopeRuleStore with SaveRuleAsync"
```

---

### Task 6: Write Runtime Tests + Implement (TDD)

**Files:**
- Create: `framework/test/CrestCreates.Organization.Tests/DataPermissionRuntimeTests.cs`
- Create: `framework/src/CrestCreates.Organization/DefaultDataPermissionRuntime.cs`

- [ ] **Step 1: Create runtime test file**

```csharp
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class DataPermissionRuntimeTests
{
    [Fact]
    public async Task ResolveScopeAsync_DelegatesToScopeProvider()
    {
        var store = new InMemoryOrganizationStore();
        var identityService = new DefaultOrganizationIdentityService(store);
        var hierarchyService = new DefaultOrganizationHierarchyService(store);
        var ruleStore = new InMemoryDataPermissionScopeRuleStore();
        var scopeProvider = new DefaultDataPermissionScopeProvider(identityService, hierarchyService, ruleStore);
        var filterBuilder = new DefaultDataPermissionFilterBuilder();
        var runtime = new DefaultDataPermissionRuntime(scopeProvider, filterBuilder);

        var request = new DataPermissionScopeRequest { UserId = "user-1" };
        var scope = await runtime.ResolveScopeAsync(request);
        scope.Kind.Should().Be(DataPermissionScopeKind.Self);
    }

    [Fact]
    public void BuildFilter_DelegatesToFilterBuilder()
    {
        var scope = new DataPermissionScope { Kind = DataPermissionScopeKind.All };
        var mapping = new DataPermissionFieldMapping();

        var scopeProvider = new DefaultDataPermissionScopeProvider(
            new DefaultOrganizationIdentityService(new InMemoryOrganizationStore()),
            new DefaultOrganizationHierarchyService(new InMemoryOrganizationStore()),
            new InMemoryDataPermissionScopeRuleStore());
        var filterBuilder = new DefaultDataPermissionFilterBuilder();
        var runtime = new DefaultDataPermissionRuntime(scopeProvider, filterBuilder);

        var filter = runtime.BuildFilter(scope, mapping);
        filter.IsUnrestricted.Should().BeTrue();
    }

    [Fact]
    public async Task EndToEnd_ResolveThenBuild_ProducesExpectedFilter()
    {
        var store = new InMemoryOrganizationStore();
        await store.SaveMembershipAsync(new UserOrganizationMembership
        {
            Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1",
            IsPrimary = true, IsActive = true
        });

        var identityService = new DefaultOrganizationIdentityService(store);
        var hierarchyService = new DefaultOrganizationHierarchyService(store);
        var ruleStore = new InMemoryDataPermissionScopeRuleStore();
        var scopeProvider = new DefaultDataPermissionScopeProvider(identityService, hierarchyService, ruleStore);
        var filterBuilder = new DefaultDataPermissionFilterBuilder();
        var runtime = new DefaultDataPermissionRuntime(scopeProvider, filterBuilder);

        var request = new DataPermissionScopeRequest { UserId = "user-1" };
        var scope = await runtime.ResolveScopeAsync(request);
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);

        var mapping = new DataPermissionFieldMapping { OrganizationUnitIdField = "OrgId" };
        var filter = runtime.BuildFilter(scope, mapping);
        filter.Rules.Should().HaveCount(1);
        filter.Rules[0].FieldName.Should().Be("OrgId");
        filter.Rules[0].Operator.Should().Be(DataPermissionFilterOperator.Equal);
        filter.Rules[0].Value.Should().Be("dept-1");
    }
}
```

- [ ] **Step 2: Run tests to confirm FAIL (no DefaultDataPermissionRuntime)**

Run: `dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~DataPermissionRuntimeTests"`
Expected: Build FAILED — `DefaultDataPermissionRuntime` not found.

- [ ] **Step 3: Implement `DefaultDataPermissionRuntime.cs`**

```csharp
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

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

    public Task<DataPermissionScope> ResolveScopeAsync(
        DataPermissionScopeRequest request,
        CancellationToken cancellationToken = default)
        => _scopeProvider.GetScopeAsync(request, cancellationToken);

    public DataPermissionFilter BuildFilter(
        DataPermissionScope scope,
        DataPermissionFieldMapping mapping)
        => _filterBuilder.Build(scope, mapping);
}
```

- [ ] **Step 4: Run runtime tests**

Run: `dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~DataPermissionRuntimeTests"`
Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Organization.Tests/DataPermissionRuntimeTests.cs \
        framework/src/CrestCreates.Organization/DefaultDataPermissionRuntime.cs
git commit -m "feat(Phase5e): implement DefaultDataPermissionRuntime facade"
```

---

### Task 7: Upgrade DefaultDataPermissionScopeProvider

**Files:**
- Modify: `framework/src/CrestCreates.Organization/DefaultDataPermissionScopeProvider.cs`

- [ ] **Step 1: Rewrite `DefaultDataPermissionScopeProvider.cs`**

Replace the entire file content:

```csharp
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization;

public sealed class DefaultDataPermissionScopeProvider : IDataPermissionScopeProvider
{
    private readonly IOrganizationIdentityService _identityService;
    private readonly IOrganizationHierarchyService _hierarchyService;
    private readonly IDataPermissionScopeRuleStore _ruleStore;

    public DefaultDataPermissionScopeProvider(
        IOrganizationIdentityService identityService,
        IOrganizationHierarchyService hierarchyService,
        IDataPermissionScopeRuleStore ruleStore)
    {
        _identityService = identityService;
        _hierarchyService = hierarchyService;
        _ruleStore = ruleStore;
    }

    public async Task<DataPermissionScope> GetScopeAsync(
        DataPermissionScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Check rule store for explicit scope kind
        if (request.Resource is not null)
        {
            var ruleKind = await _ruleStore.GetScopeKindAsync(
                request.Resource, request.Action, request.Permission, request.TenantId, cancellationToken);

            if (ruleKind is not null)
                return await ResolveByKindAsync(ruleKind.Value, request, cancellationToken);
        }

        // Step 2: Fall back to org-membership-based scope
        var context = await _identityService.GetContextAsync(
            request.UserId, request.TenantId, cancellationToken);

        if (context.PrimaryOrganizationUnitId is null)
        {
            return new DataPermissionScope
            {
                Kind = DataPermissionScopeKind.Self,
                UserId = request.UserId,
                TenantId = request.TenantId,
                Resource = request.Resource,
                Action = request.Action,
                Permission = request.Permission
            };
        }

        return new DataPermissionScope
        {
            Kind = DataPermissionScopeKind.OwnOrganization,
            UserId = request.UserId,
            TenantId = request.TenantId,
            Resource = request.Resource,
            Action = request.Action,
            Permission = request.Permission,
            OrganizationUnitId = context.PrimaryOrganizationUnitId,
            OrganizationUnitIds = context.OrganizationUnitIds
        };
    }

    // Old overload — adapter
    public Task<DataPermissionScope> GetScopeAsync(
        string userId,
        string permission,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
        => GetScopeAsync(new DataPermissionScopeRequest
        {
            UserId = userId,
            Permission = permission,
            TenantId = tenantId
        }, cancellationToken);

    private async Task<DataPermissionScope> ResolveByKindAsync(
        DataPermissionScopeKind kind,
        DataPermissionScopeRequest request,
        CancellationToken ct)
    {
        // Self and All don't need identity context
        if (kind is DataPermissionScopeKind.Self or DataPermissionScopeKind.All)
        {
            return new DataPermissionScope
            {
                Kind = kind,
                UserId = request.UserId,
                TenantId = request.TenantId,
                Resource = request.Resource,
                Action = request.Action,
                Permission = request.Permission
            };
        }

        // None is always deny
        if (kind == DataPermissionScopeKind.None)
        {
            return new DataPermissionScope { Kind = DataPermissionScopeKind.None };
        }

        // OwnOrganization / OwnOrganizationAndDescendants need identity context
        var context = await _identityService.GetContextAsync(
            request.UserId, request.TenantId, ct);

        if (kind == DataPermissionScopeKind.OwnOrganization)
        {
            if (context.PrimaryOrganizationUnitId is null)
                return new DataPermissionScope { Kind = DataPermissionScopeKind.None };

            return new DataPermissionScope
            {
                Kind = kind,
                UserId = request.UserId,
                TenantId = request.TenantId,
                Resource = request.Resource,
                Action = request.Action,
                Permission = request.Permission,
                OrganizationUnitId = context.PrimaryOrganizationUnitId,
                OrganizationUnitIds = context.OrganizationUnitIds
            };
        }

        // OwnOrganizationAndDescendants
        if (context.PrimaryOrganizationUnitId is null)
            return new DataPermissionScope { Kind = DataPermissionScopeKind.None };

        var descendants = await _hierarchyService.GetDescendantsAsync(
            context.PrimaryOrganizationUnitId, request.TenantId, ct);

        var allIds = new List<string> { context.PrimaryOrganizationUnitId };
        allIds.AddRange(descendants.Select(d => d.Id));

        return new DataPermissionScope
        {
            Kind = kind,
            UserId = request.UserId,
            TenantId = request.TenantId,
            Resource = request.Resource,
            Action = request.Action,
            Permission = request.Permission,
            OrganizationUnitId = context.PrimaryOrganizationUnitId,
            OrganizationUnitIds = allIds
        };
    }
}
```

- [ ] **Step 2: Build to verify compile**

Run: `dotnet build framework/src/CrestCreates.Organization/`
Expected: Build succeeded. 0 Error(s).

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Organization/DefaultDataPermissionScopeProvider.cs
git commit -m "feat(Phase5e): upgrade DefaultDataPermissionScopeProvider with full resolution algorithm"
```

---

### Task 8: Rewrite Scope Provider Tests (D1–D12)

**Files:**
- Create: `framework/test/CrestCreates.Organization.Tests/DataPermissionScopeProviderTests.cs` (replace)

- [ ] **Step 1: Replace test file with 12 comprehensive tests**

```csharp
using CrestCreates.Organization;
using CrestCreates.Organization.Abstractions;

namespace CrestCreates.Organization.Tests;

public class DataPermissionScopeProviderTests
{
    private static async Task<(
        InMemoryOrganizationStore Store,
        InMemoryDataPermissionScopeRuleStore RuleStore,
        DefaultDataPermissionScopeProvider Provider
    )> CreateProviderAsync(
        List<UserOrganizationMembership>? memberships = null,
        List<OrganizationUnit>? orgUnits = null)
    {
        var store = new InMemoryOrganizationStore();
        if (orgUnits is not null)
            foreach (var u in orgUnits)
                await store.SaveOrganizationUnitAsync(u);
        if (memberships is not null)
            foreach (var m in memberships)
                await store.SaveMembershipAsync(m);

        var identity = new DefaultOrganizationIdentityService(store);
        var hierarchy = new DefaultOrganizationHierarchyService(store);
        var ruleStore = new InMemoryDataPermissionScopeRuleStore();
        var provider = new DefaultDataPermissionScopeProvider(identity, hierarchy, ruleStore);
        return (store, ruleStore, provider);
    }

    private static DataPermissionScopeRequest Request(string userId,
        string? tenantId = null, string? resource = null,
        string? action = null, string? permission = null)
        => new()
        {
            UserId = userId,
            TenantId = tenantId,
            Resource = resource,
            Action = action,
            Permission = permission
        };

    // D1: No org → Self
    [Fact]
    public async Task GetScope_ReturnsSelf_WhenNoOrganization()
    {
        var (_, _, provider) = await CreateProviderAsync();
        var scope = await provider.GetScopeAsync(Request("user-1"));
        scope.Kind.Should().Be(DataPermissionScopeKind.Self);
        scope.UserId.Should().Be("user-1");
        scope.OrganizationUnitId.Should().BeNull();
    }

    // D2: Primary org → OwnOrganization
    [Fact]
    public async Task GetScope_ReturnsOwnOrganization_WhenPrimaryExists()
    {
        var (_, _, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        var scope = await provider.GetScopeAsync(Request("user-1"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
        scope.UserId.Should().Be("user-1");
        scope.OrganizationUnitId.Should().Be("dept-1");
    }

    // D3: Rule → OwnOrganizationAndDescendants with hierarchy
    [Fact]
    public async Task GetScope_ReturnsOwnOrganizationAndDescendants_WhenRuleConfigured()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            },
            orgUnits: new()
            {
                new() { Id = "dept-1", Name = "Dept" },
                new() { Id = "team-3", ParentId = "dept-1", Name = "Team 3" },
                new() { Id = "team-4", ParentId = "dept-1", Name = "Team 4" }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", ScopeKind = DataPermissionScopeKind.OwnOrganizationAndDescendants });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganizationAndDescendants);
        scope.OrganizationUnitIds.Should().BeEquivalentTo(["dept-1", "team-3", "team-4"]);
    }

    // D4: Rule → All
    [Fact]
    public async Task GetScope_ReturnsAll_WhenRuleConfigured()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Report", Action = "Read", ScopeKind = DataPermissionScopeKind.All });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Report", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.All);
        scope.IsUnrestricted.Should().BeTrue();
        scope.OrganizationUnitId.Should().BeNull();
    }

    // D5: Rule → None
    [Fact]
    public async Task GetScope_ReturnsNone_WhenRuleConfigured()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync();
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "SecretDoc", Action = "Read", ScopeKind = DataPermissionScopeKind.None });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "SecretDoc", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.None);
        scope.IsEmpty.Should().BeTrue();
    }

    // D6: Rule → OwnOrganization, no primary org → fail closed
    [Fact]
    public async Task GetScope_ReturnsNone_WhenOwnOrganizationWithoutPrimaryOrg()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(); // no memberships
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Write", ScopeKind = DataPermissionScopeKind.OwnOrganization });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Write"));
        scope.Kind.Should().Be(DataPermissionScopeKind.None);
    }

    // D7: Rule → OwnOrganizationAndDescendants, no primary org → fail closed
    [Fact]
    public async Task GetScope_ReturnsNone_WhenOwnOrganizationAndDescendantsWithoutPrimaryOrg()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(); // no memberships
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", ScopeKind = DataPermissionScopeKind.OwnOrganizationAndDescendants });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.None);
    }

    // D8: No rule, has org → fallback OwnOrganization
    [Fact]
    public async Task GetScope_FallsBackToOwnOrganization_WhenNoRuleAndHasOrg()
    {
        var (_, _, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
        scope.OrganizationUnitId.Should().Be("dept-1");
    }

    // D9: No rule, no org → fallback Self
    [Fact]
    public async Task GetScope_FallsBackToSelf_WhenNoRuleAndNoOrg()
    {
        var (_, _, provider) = await CreateProviderAsync();

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.Self);
    }

    // D10: Rule overrides org membership
    [Fact]
    public async Task GetScope_RuleOverridesOrgMembership()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", ScopeKind = DataPermissionScopeKind.All });

        var scope = await provider.GetScopeAsync(Request("user-1", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.All); // rule wins
        scope.OrganizationUnitId.Should().BeNull(); // no org IDs for All
    }

    // D11: Tenant isolation
    [Fact]
    public async Task GetScope_IsTenantAware()
    {
        var (_, _, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", TenantId = "t-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });

        // Query in same tenant — finds org
        var scope = await provider.GetScopeAsync(Request("user-1", tenantId: "t-1"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
        scope.OrganizationUnitId.Should().Be("dept-1");

        // Query in different tenant — no org found
        var scope2 = await provider.GetScopeAsync(Request("user-1", tenantId: "t-2"));
        scope2.Kind.Should().Be(DataPermissionScopeKind.Self);
        scope2.OrganizationUnitId.Should().BeNull();
    }

    // D12: Old overload delegates to new
    [Fact]
    public async Task GetScope_OldOverload_DelegatesToNewMethod()
    {
        var (_, _, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });

        var newScope = await provider.GetScopeAsync(Request("user-1", permission: "read:docs"));
        var oldScope = await provider.GetScopeAsync("user-1", "read:docs");

        newScope.Kind.Should().Be(oldScope.Kind);
        newScope.OrganizationUnitId.Should().Be(oldScope.OrganizationUnitId);
    }

    // D13: Tenant-specific rule overrides global rule
    [Fact]
    public async Task GetScope_TenantRuleOverridesGlobalRule()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", TenantId = "t-A", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", ScopeKind = DataPermissionScopeKind.Self });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All });

        var scope = await provider.GetScopeAsync(Request("user-1", tenantId: "t-A", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.All); // tenant rule overrides global
    }

    // D14: Other tenant rule does not apply
    [Fact]
    public async Task GetScope_OtherTenantRuleDoesNotApply()
    {
        var (_, ruleStore, provider) = await CreateProviderAsync(
            memberships: new()
            {
                new() { Id = "m-1", UserId = "user-1", TenantId = "t-B", OrganizationUnitId = "dept-1", IsPrimary = true, IsActive = true }
            });
        await ruleStore.SaveRuleAsync(new DataPermissionScopeRule
            { Resource = "Book", Action = "Read", TenantId = "t-A", ScopeKind = DataPermissionScopeKind.All });

        // "t-B" queries with only "t-A" rules → falls back to org-membership
        var scope = await provider.GetScopeAsync(Request("user-1", tenantId: "t-B", resource: "Book", action: "Read"));
        scope.Kind.Should().Be(DataPermissionScopeKind.OwnOrganization);
    }
}
```

- [ ] **Step 2: Run scope provider tests**

Run: `dotnet test framework/test/CrestCreates.Organization.Tests/ --filter "FullyQualifiedName~DataPermissionScopeProviderTests"`
Expected: All 14 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Organization.Tests/DataPermissionScopeProviderTests.cs
git commit -m "test(Phase5e): rewrite DataPermissionScopeProviderTests (2→14 tests)"
```

---

### Task 9: Extend DI Registration

**Files:**
- Modify: `framework/src/CrestCreates.Organization/OrganizationServiceCollectionExtensions.cs`

- [ ] **Step 1: Add 3 new registrations**

Current content:
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

Edit: Add 3 new `TryAdd*` lines between `IDataPermissionScopeProvider` and `IOrganizationContextAccessor`:

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
        services.TryAddSingleton<IDataPermissionScopeRuleStore, InMemoryDataPermissionScopeRuleStore>();
        services.TryAddSingleton<IDataPermissionFilterBuilder, DefaultDataPermissionFilterBuilder>();
        services.TryAddScoped<IDataPermissionRuntime, DefaultDataPermissionRuntime>();
        services.TryAddSingleton<IOrganizationContextAccessor, NullOrganizationContextAccessor>();
        return services;
    }
}
```

- [ ] **Step 2: Build Organization project**

Run: `dotnet build framework/src/CrestCreates.Organization/`
Expected: Build succeeded. 0 Error(s).

- [ ] **Step 3: Build test project to verify DI-referenced types compile**

Run: `dotnet build framework/test/CrestCreates.Organization.Tests/`
Expected: Build succeeded. 0 Error(s).

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Organization/OrganizationServiceCollectionExtensions.cs
git commit -m "feat(Phase5e): register IDataPermissionScopeRuleStore, IDataPermissionFilterBuilder, IDataPermissionRuntime in AddOrganizationKernel"
```

---

### Task 10: Full Test Run & Regression Check

- [ ] **Step 1: Run all Organization tests**

Run: `dotnet test framework/test/CrestCreates.Organization.Tests/`
Expected: All tests PASS. Count should be: 42 existing + 37 new = 79 tests.

- [ ] **Step 2: Check regression — Capability tests**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests/`
Expected: All 117 tests PASS (zero changes to Capability).

- [ ] **Step 3: Check regression — Authorization**

Run: `dotnet test framework/test/CrestCreates.Application.Tests/ --filter "FullyQualifiedName~Permission"`
Expected: All PASS (zero changes to Authorization).

- [ ] **Step 4: Build entire solution to verify no project-level breakage**

Run: `dotnet build`
Expected: Build succeeded. 0 Error(s).

- [ ] **Step 5: Commit (if any cleanup)**

```bash
git status
# Commit any remaining changes if needed
```

---

### Task 11: Update memory.md

**Files:**
- Modify: `memory.md`

- [ ] **Step 1: Add Phase 5e entry to memory.md**

Find the section "### Organization Identity Kernel (Phase 5c, 2026-06-11)" and "### Capability Authorization Bridge (Phase 5d, 2026-06-11)" in memory.md.

After the Phase 5d section, insert:

```
### Data Permission Runtime Foundation (Phase 5e, 2026-06-11)

- Enhanced `DataPermissionScope` with `TenantId`, `Resource`, `Action`, `Permission`, `IsEmpty`, `IsUnrestricted`.
- `DataPermissionScopeKind` + `Custom` (reserved).
- `DataPermissionScopeRequest` input model replacing parameter list.
- `IDataPermissionScopeRuleStore` + `InMemoryDataPermissionScopeRuleStore` (ConcurrentDictionary, wildcard fallback).
- `IDataPermissionScopeProvider` extended with new `GetScopeAsync(DataPermissionScopeRequest)` overload; old overload kept as adapter.
- `DefaultDataPermissionScopeProvider` upgraded: rule store resolution (resource/action/permission → kind), hierarchy-backed resolution for `OwnOrganizationAndDescendants`, fail-closed when no primary org.
- `DataPermissionFilter` / `DataPermissionFilterRule` / `DataPermissionFilterOperator` / `DataPermissionFieldMapping` — ORM-neutral filter model.
- `IDataPermissionFilterBuilder` + `DefaultDataPermissionFilterBuilder` — fail-closed filter construction (missing mapping → deny; tenant scoping additive).
- `IDataPermissionRuntime` + `DefaultDataPermissionRuntime` — facade composing scope resolution + filter building.
- DI: 3 new registrations in `AddOrganizationKernel()` (`TryAddSingleton` for rule store + filter builder, `TryAddScoped` for runtime).
- 37 new tests (14 scope provider, 13 filter builder, 3 runtime, 7 rule store), 0 regressions on Capability/Authorization.
- **Caveat**: No EF/SqlSugar/Mongo filter integration. No `AuthorizationMiddleware`/`PermissionCapabilityAuthorizationService` changes. Legacy `IDataPermissionFilter` untouched. `Custom` scope kind not resolved by provider.
```

- [ ] **Step 2: Update the "Last Updated" date at the top of memory.md**

Change `Last Updated: 2026-06-11` to `Last Updated: 2026-06-11` (same date, no change).

Actually, check line 1: `Last Updated: 2026-06-11`. Same date — no change needed.

- [ ] **Step 3: Commit**

```bash
git add memory.md
git commit -m "docs(Phase5e): update memory.md with Data Permission Runtime Foundation status"
```

---

## Acceptance Criteria

```bash
# All Organization tests (79 tests)
dotnet test framework/test/CrestCreates.Organization.Tests/

# Capability regression (117 tests, zero changes)
dotnet test framework/test/CrestCreates.Capability.Tests/

# Authorization regression
dotnet test framework/test/CrestCreates.Application.Tests/ --filter "FullyQualifiedName~Permission"

# Full solution build (no project-level breakage)
dotnet build
```

---

## References

- Design Spec: `docs/superpowers/specs/2026-06-11-phase-5e-data-permission-runtime-foundation-design.md`
- Organization Abstractions: `framework/src/CrestCreates.Organization.Abstractions/`
- Organization Implementation: `framework/src/CrestCreates.Organization/`
- Organization Tests: `framework/test/CrestCreates.Organization.Tests/`
- Phase 5c Spec: `docs/superpowers/specs/2026-06-11-phase-5c-organization-identity-kernel-design.md`
- Phase 5d Spec: `docs/superpowers/specs/2026-06-11-phase-5d-capability-authorization-bridge-design.md`
- memory.md: `memory.md`
