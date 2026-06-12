# Phase 5f: HumanTask Assignee Resolver Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the minimal main chain for HumanTask assignee resolution — resolve `AssigneeUserId`/`AssigneeRoleId`/candidates/org/position from `HumanTaskCreationRequest` and `HumanTaskDescriptor`, persist on `HumanTaskInstance`, and expose via extended store queries.

**Architecture:** New `IHumanTaskAssigneeResolver` interface + `HumanTaskAssigneeResolution` DTO in Abstractions, `DefaultHumanTaskAssigneeResolver` in implementation. `DefaultHumanTaskRuntime.CreateAsync` wired through resolver. `HumanTaskInstance` extended with 5 fields. `InMemoryHumanTaskInstanceStore` extended with 4 new pending queries. Zero Workflow/Organization changes.

**Tech Stack:** C# 13, .NET 10, xUnit + FluentAssertions + Moq

**Spec:** `docs/superpowers/specs/2026-06-12-phase-5f-humantask-assignee-resolver-design.md`

---

## File Structure

```
NEW:  framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskAssigneeResolution.cs
NEW:  framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskAssigneeResolver.cs
NEW:  framework/src/CrestCreates.HumanTask/DefaultHumanTaskAssigneeResolver.cs
MOD:  framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCreationRequest.cs
MOD:  framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs
MOD:  framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskInstanceStore.cs
MOD:  framework/src/CrestCreates.HumanTask/DefaultHumanTaskRuntime.cs
MOD:  framework/src/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs
MOD:  framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs
NEW:  framework/test/CrestCreates.HumanTask.Tests/HumanTaskAssigneeResolverTests.cs
MOD:  framework/test/CrestCreates.HumanTask.Tests/HumanTaskRuntimeTests.cs
MOD:  framework/test/CrestCreates.HumanTask.Tests/InMemoryHumanTaskInstanceStoreTests.cs
```

---

## Task 1: HumanTaskAssigneeResolution — New DTO

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskAssigneeResolution.cs`

- [ ] **Step 1: Write the type**

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskAssigneeResolution
{
    public string? AssigneeUserId { get; init; }
    public string? AssigneeRoleId { get; init; }
    public IReadOnlyList<string> CandidateUserIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CandidateRoleIds { get; init; } = Array.Empty<string>();
    public string? OrganizationUnitId { get; init; }
    public string? PositionId { get; init; }
    public string? AssigneeResolutionReason { get; init; }

    public bool IsAssigned => !string.IsNullOrWhiteSpace(AssigneeUserId)
                           || !string.IsNullOrWhiteSpace(AssigneeRoleId);

    public bool HasCandidates => CandidateUserIds.Count > 0 || CandidateRoleIds.Count > 0;

    public bool IsUnassigned => !IsAssigned && !HasCandidates
        && string.IsNullOrWhiteSpace(OrganizationUnitId)
        && string.IsNullOrWhiteSpace(PositionId);
}
```

**Rules locked in this type:**
- Init-only — immutable after construction
- `IReadOnlyList<T>` prevents mutable List interface leak
- `!string.IsNullOrWhiteSpace(...)` — empty/whitespace strings treated as null
- `IsUnassigned` requires ALL five identity fields to be empty/whitespace

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.HumanTask.Abstractions
```

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskAssigneeResolution.cs
git commit -m "feat(Phase5f): add HumanTaskAssigneeResolution DTO"
```

---

## Task 2: IHumanTaskAssigneeResolver — Interface

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskAssigneeResolver.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskAssigneeResolver
{
    Task<HumanTaskAssigneeResolution> ResolveAsync(
        HumanTaskDescriptor descriptor,
        HumanTaskCreationRequest request,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.HumanTask.Abstractions
```

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskAssigneeResolver.cs
git commit -m "feat(Phase5f): add IHumanTaskAssigneeResolver interface"
```

---

## Task 3: DefaultHumanTaskAssigneeResolver — Implementation

**Files:**
- Create: `framework/src/CrestCreates.HumanTask/DefaultHumanTaskAssigneeResolver.cs`

- [ ] **Step 1: Write the resolver**

```csharp
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class DefaultHumanTaskAssigneeResolver : IHumanTaskAssigneeResolver
{
    public Task<HumanTaskAssigneeResolution> ResolveAsync(
        HumanTaskDescriptor descriptor,
        HumanTaskCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        // Priority 1: explicit user
        if (!string.IsNullOrWhiteSpace(request.AssigneeUserId))
        {
            return Task.FromResult(new HumanTaskAssigneeResolution
            {
                AssigneeUserId = request.AssigneeUserId,
                CandidateRoleIds = !string.IsNullOrWhiteSpace(request.AssigneeRoleId)
                    ? new[] { request.AssigneeRoleId }
                    : Array.Empty<string>()
            });
        }

        // Priority 2: explicit role (no user)
        if (!string.IsNullOrWhiteSpace(request.AssigneeRoleId))
        {
            return Task.FromResult(new HumanTaskAssigneeResolution
            {
                AssigneeRoleId = request.AssigneeRoleId
            });
        }

        // Priority 3: auxiliary context
        if (!string.IsNullOrWhiteSpace(request.RequestedOrganizationUnitId)
            || !string.IsNullOrWhiteSpace(request.RequestedPositionId))
        {
            return Task.FromResult(new HumanTaskAssigneeResolution
            {
                OrganizationUnitId = request.RequestedOrganizationUnitId,
                PositionId = request.RequestedPositionId
            });
        }

        // Priority 4: strategy fallback
        return Task.FromResult(ResolveByStrategy(descriptor));
    }

    private static HumanTaskAssigneeResolution ResolveByStrategy(HumanTaskDescriptor descriptor)
    {
        return descriptor.AssigneeStrategy switch
        {
            AssigneeStrategy.SingleUser or AssigneeStrategy.CandidateGroup
                => new HumanTaskAssigneeResolution(), // unassigned — explicit values already checked in priorities 1-2

            AssigneeStrategy.RoundRobin
                => new HumanTaskAssigneeResolution
                {
                    AssigneeResolutionReason = "RoundRobin strategy is not yet implemented"
                },

            AssigneeStrategy.LeastLoaded
                => new HumanTaskAssigneeResolution
                {
                    AssigneeResolutionReason = "LeastLoaded strategy is not yet implemented"
                },

            _ => new HumanTaskAssigneeResolution()
        };
    }
}
```

**Snapshot contract**: Resolver returns `Array.Empty<string>()` or `new[] { ... }` (string arrays). Never `List<string>` cast to `IReadOnlyList`.

- [ ] **Step 2: Build to verify compilation**

```bash
dotnet build framework/src/CrestCreates.HumanTask
```

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/DefaultHumanTaskAssigneeResolver.cs
git commit -m "feat(Phase5f): add DefaultHumanTaskAssigneeResolver — 4-priority resolution"
```

---

## Task 4: Resolver Unit Tests

**Files:**
- Create: `framework/test/CrestCreates.HumanTask.Tests/HumanTaskAssigneeResolverTests.cs`

- [ ] **Step 1: Write all resolver tests**

```csharp
using CrestCreates.HumanTask.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskAssigneeResolverTests
{
    private static HumanTaskDescriptor CreateDescriptor(
        string id = "ht_01",
        AssigneeStrategy strategy = AssigneeStrategy.SingleUser)
    {
        return new HumanTaskDescriptor
        {
            Id = id, Name = "test", Version = 1,
            AssigneeStrategy = strategy,
            Interaction = new Metadata.Abstractions.VersionedDescriptorRef<
                Metadata.Abstractions.IInteractionDescriptor>("form_01", 1)
        };
    }

    private readonly DefaultHumanTaskAssigneeResolver _resolver = new();

    [Fact]
    public async Task AssigneeResolver_ExplicitUser_AssignsUser()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeUserId = "user-1"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.AssigneeUserId.Should().Be("user-1");
        resolution.IsAssigned.Should().BeTrue();
        resolution.CandidateRoleIds.Should().BeEmpty();
    }

    [Fact]
    public async Task AssigneeResolver_ExplicitRole_AssignsRole()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeRoleId = "role-manager"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.AssigneeRoleId.Should().Be("role-manager");
        resolution.IsAssigned.Should().BeTrue();
        resolution.AssigneeUserId.Should().BeNull();
    }

    [Fact]
    public async Task AssigneeResolver_UserTakesPrecedence_WhenUserAndRoleBothProvided()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeUserId = "user-1",
            AssigneeRoleId = "role-manager"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.AssigneeUserId.Should().Be("user-1");
        resolution.AssigneeRoleId.Should().BeNull();
        resolution.CandidateRoleIds.Should().ContainSingle("role-manager");
        resolution.IsAssigned.Should().BeTrue();
    }

    [Fact]
    public async Task AssigneeResolver_SingleUserWithoutExplicitAssignee_ReturnsUnassigned()
    {
        var descriptor = CreateDescriptor(strategy: AssigneeStrategy.SingleUser);
        var request = new HumanTaskCreationRequest { HumanTaskId = "ht_01" };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.IsUnassigned.Should().BeTrue();
        resolution.IsAssigned.Should().BeFalse();
        resolution.HasCandidates.Should().BeFalse();
        resolution.AssigneeResolutionReason.Should().BeNull();
    }

    [Fact]
    public async Task AssigneeResolver_CandidateGroup_WithExplicitRole_AssignsRole()
    {
        var descriptor = CreateDescriptor(strategy: AssigneeStrategy.CandidateGroup);
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeRoleId = "role-reviewers"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.AssigneeRoleId.Should().Be("role-reviewers");
        resolution.IsAssigned.Should().BeTrue();
    }

    [Fact]
    public async Task AssigneeResolver_RoundRobin_ReturnsUnassigned()
    {
        var descriptor = CreateDescriptor(strategy: AssigneeStrategy.RoundRobin);
        var request = new HumanTaskCreationRequest { HumanTaskId = "ht_01" };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.IsUnassigned.Should().BeTrue();
        resolution.AssigneeResolutionReason.Should().Be(
            "RoundRobin strategy is not yet implemented");
    }

    [Fact]
    public async Task AssigneeResolver_LeastLoaded_ReturnsUnassigned()
    {
        var descriptor = CreateDescriptor(strategy: AssigneeStrategy.LeastLoaded);
        var request = new HumanTaskCreationRequest { HumanTaskId = "ht_01" };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.IsUnassigned.Should().BeTrue();
        resolution.AssigneeResolutionReason.Should().Be(
            "LeastLoaded strategy is not yet implemented");
    }

    [Fact]
    public async Task AssigneeResolver_RequestOrgAndPosition_StoresContext()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            RequestedOrganizationUnitId = "org-dept-1",
            RequestedPositionId = "pos-manager"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        resolution.OrganizationUnitId.Should().Be("org-dept-1");
        resolution.PositionId.Should().Be("pos-manager");
        resolution.IsAssigned.Should().BeFalse();
        resolution.HasCandidates.Should().BeFalse();
    }

    [Fact]
    public async Task AssigneeResolver_WhitespaceUserId_IsTreatedAsNull()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeUserId = "   ",
            AssigneeRoleId = "role-manager"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        // Whitespace user ignored; falls through to priority 2 (role)
        resolution.AssigneeRoleId.Should().Be("role-manager");
        resolution.AssigneeUserId.Should().BeNull();
    }

    [Fact]
    public async Task AssigneeResolver_WhitespaceOrgPosition_IsNotStoredInUnassigned()
    {
        var descriptor = CreateDescriptor();
        var request = new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            RequestedOrganizationUnitId = "  ",
            RequestedPositionId = "\t"
        };

        var resolution = await _resolver.ResolveAsync(descriptor, request);

        // Whitespace org/position should not be stored for IsUnassigned check
        resolution.IsUnassigned.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run resolver tests**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests --filter "FullyQualifiedName~AssigneeResolver"
```
Expected: 10 pass, 0 fail.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.HumanTask.Tests/HumanTaskAssigneeResolverTests.cs
git commit -m "test(Phase5f): add HumanTaskAssigneeResolver unit tests — 10 tests covering all 4 priorities + whitespace guard"
```

---

## Task 5: Extend HumanTaskCreationRequest

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCreationRequest.cs`

- [ ] **Step 1: Add 3 new fields**

Append before the closing `}` of the class:

```csharp
    public string? RequestedOrganizationUnitId { get; init; }
    public string? RequestedPositionId { get; init; }
    public string? RequestedByUserId { get; init; }
```

Full file after edit:

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCreationRequest
{
    public string HumanTaskId { get; init; } = default!;
    public int? Version { get; init; }

    public string? TenantId { get; init; }

    public string? AssigneeUserId { get; init; }
    public string? AssigneeRoleId { get; init; }

    public string? WorkflowInstanceId { get; init; }
    public string? WorkflowStepId { get; init; }

    public object? Input { get; init; }

    // Phase 5f: assignee resolution context
    public string? RequestedOrganizationUnitId { get; init; }
    public string? RequestedPositionId { get; init; }
    public string? RequestedByUserId { get; init; }
}
```

- [ ] **Step 2: Build to verify no downstream breaks (resolver already reads these fields)**

```bash
dotnet build framework/src/CrestCreates.HumanTask.Abstractions && dotnet build framework/src/CrestCreates.HumanTask
```

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCreationRequest.cs
git commit -m "feat(Phase5f): extend HumanTaskCreationRequest — RequestedOrganizationUnitId, RequestedPositionId, RequestedByUserId"
```

---

## Task 6: Extend HumanTaskInstance

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs`

- [ ] **Step 1: Add 5 new fields + extend Clone()**

Add fields after `CancelledAt` (before `ConcurrencyStamp`):

```csharp
    // Phase 5f: assignee resolution fields
    public IReadOnlyList<string> CandidateUserIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> CandidateRoleIds { get; set; } = Array.Empty<string>();
    public string? OrganizationUnitId { get; set; }
    public string? PositionId { get; set; }
    public string? AssigneeResolutionReason { get; set; }
```

Extend `Clone()` — add after `UpdatedAt = this.UpdatedAt` line, before closing `};`:

```csharp
            CandidateUserIds = this.CandidateUserIds.ToArray(),
            CandidateRoleIds = this.CandidateRoleIds.ToArray(),
            OrganizationUnitId = this.OrganizationUnitId,
            PositionId = this.PositionId,
            AssigneeResolutionReason = this.AssigneeResolutionReason
```

Full `Clone()` method after edit:

```csharp
    public HumanTaskInstance Clone()
    {
        return new HumanTaskInstance
        {
            Id = this.Id,
            HumanTaskId = this.HumanTaskId,
            HumanTaskVersion = this.HumanTaskVersion,
            Status = this.Status,
            TenantId = this.TenantId,
            AssigneeUserId = this.AssigneeUserId,
            AssigneeRoleId = this.AssigneeRoleId,
            WorkflowInstanceId = this.WorkflowInstanceId,
            WorkflowStepId = this.WorkflowStepId,
            Input = this.Input,
            Output = this.Output,
            Outcome = this.Outcome,
            CreatedAt = this.CreatedAt,
            CompletedAt = this.CompletedAt,
            CancelledAt = this.CancelledAt,
            CancellationReason = this.CancellationReason,
            ConcurrencyStamp = this.ConcurrencyStamp,
            UpdatedAt = this.UpdatedAt,
            CandidateUserIds = this.CandidateUserIds.ToArray(),
            CandidateRoleIds = this.CandidateRoleIds.ToArray(),
            OrganizationUnitId = this.OrganizationUnitId,
            PositionId = this.PositionId,
            AssigneeResolutionReason = this.AssigneeResolutionReason
        };
    }
```

**Snapshot contract**: `.ToArray()` on candidate lists prevents mutable reference leak through Clone.

- [ ] **Step 2: Build to verify**

```bash
dotnet build framework/src/CrestCreates.HumanTask.Abstractions
```

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs
git commit -m "feat(Phase5f): extend HumanTaskInstance — 5 assignee resolution fields + Clone snapshot"
```

---

## Task 7: Extend IHumanTaskInstanceStore — 4 New Query Methods

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskInstanceStore.cs`

- [ ] **Step 1: Add 4 new methods**

Append before closing `}` of the interface:

```csharp
    // Phase 5f: assignee resolution queries
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(
        string userId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(
        string roleId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(
        string organizationUnitId, CancellationToken ct = default);

    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(
        string positionId, CancellationToken ct = default);
```

- [ ] **Step 2: Build — WILL FAIL (store not yet implementing new methods)**

```bash
dotnet build framework/src/CrestCreates.HumanTask
```
Expected: build failure — `InMemoryHumanTaskInstanceStore` does not implement new interface methods. This is expected; Task 8 will add them.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskInstanceStore.cs
git commit -m "feat(Phase5f): extend IHumanTaskInstanceStore — 4 pending-by-candidate/org/position queries"
```

---

## Task 8: Implement New Queries in InMemoryHumanTaskInstanceStore

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs`

- [ ] **Step 1: Add 4 query methods**

Append before closing `}` of the class (after `GetPendingByWorkflowAsync`):

```csharp
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(
        string userId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.CandidateUserIds.Contains(userId))
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(
        string roleId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.CandidateRoleIds.Contains(roleId))
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(
        string organizationUnitId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.OrganizationUnitId == organizationUnitId)
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(
        string positionId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.PositionId == positionId)
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build framework/src/CrestCreates.HumanTask
```
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs
git commit -m "feat(Phase5f): implement 4 new pending queries in InMemoryHumanTaskInstanceStore"
```

---

## Task 9: Add Store Query Tests + Instance Clone Test

**Files:**
- Modify: `framework/test/CrestCreates.HumanTask.Tests/InMemoryHumanTaskInstanceStoreTests.cs`

- [ ] **Step 1: Add 6 new test methods**

Append before closing `}` of the class. Add a helper first, then 6 tests:

```csharp
    private static HumanTaskInstance CreateInstanceWithCandidates(
        string id, HumanTaskInstanceStatus status,
        string[]? candidateUsers = null, string[]? candidateRoles = null,
        string? orgId = null, string? positionId = null)
    {
        return new HumanTaskInstance
        {
            Id = id,
            HumanTaskId = "ht_01",
            HumanTaskVersion = 1,
            Status = status,
            CandidateUserIds = candidateUsers ?? Array.Empty<string>(),
            CandidateRoleIds = candidateRoles ?? Array.Empty<string>(),
            OrganizationUnitId = orgId,
            PositionId = positionId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    [Fact]
    public async Task HumanTaskInstance_Clone_Copies_AssigneeResolutionFields()
    {
        var original = new HumanTaskInstance
        {
            Id = "inst-01",
            HumanTaskId = "ht_01",
            HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Created,
            CandidateUserIds = new[] { "user-a", "user-b" },
            CandidateRoleIds = new[] { "role-reviewers" },
            OrganizationUnitId = "org-dept-1",
            PositionId = "pos-manager",
            AssigneeResolutionReason = "test reason",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var clone = original.Clone();

        clone.CandidateUserIds.Should().BeEquivalentTo(new[] { "user-a", "user-b" });
        clone.CandidateRoleIds.Should().BeEquivalentTo(new[] { "role-reviewers" });
        clone.OrganizationUnitId.Should().Be("org-dept-1");
        clone.PositionId.Should().Be("pos-manager");
        clone.AssigneeResolutionReason.Should().Be("test reason");
    }

    [Fact]
    public async Task InMemoryHumanTaskInstanceStore_QueryCandidateUser_ReturnsPendingOnly()
    {
        var store = new InMemoryHumanTaskInstanceStore();

        var created = CreateInstanceWithCandidates("inst-01", HumanTaskInstanceStatus.Created,
            candidateUsers: new[] { "user-a" });
        var assigned = CreateInstanceWithCandidates("inst-02", HumanTaskInstanceStatus.Assigned,
            candidateUsers: new[] { "user-a" });
        var completed = CreateInstanceWithCandidates("inst-03", HumanTaskInstanceStatus.Completed,
            candidateUsers: new[] { "user-a" });
        var otherUser = CreateInstanceWithCandidates("inst-04", HumanTaskInstanceStatus.Created,
            candidateUsers: new[] { "user-b" });

        await store.SaveAsync(created);
        await store.SaveAsync(assigned);
        await store.SaveAsync(completed);
        await store.SaveAsync(otherUser);

        var pending = await store.GetPendingByCandidateUserAsync("user-a");

        pending.Should().HaveCount(2);
        pending.Should().Contain(i => i.Id == "inst-01");
        pending.Should().Contain(i => i.Id == "inst-02");
        pending.Should().NotContain(i => i.Id == "inst-03");
        pending.Should().NotContain(i => i.Id == "inst-04");
    }

    [Fact]
    public async Task InMemoryHumanTaskInstanceStore_QueryCandidateRole_ReturnsPendingOnly()
    {
        var store = new InMemoryHumanTaskInstanceStore();

        var created = CreateInstanceWithCandidates("inst-01", HumanTaskInstanceStatus.Created,
            candidateRoles: new[] { "role-x" });
        var assigned = CreateInstanceWithCandidates("inst-02", HumanTaskInstanceStatus.Assigned,
            candidateRoles: new[] { "role-x" });
        var completed = CreateInstanceWithCandidates("inst-03", HumanTaskInstanceStatus.Completed,
            candidateRoles: new[] { "role-x" });
        var otherRole = CreateInstanceWithCandidates("inst-04", HumanTaskInstanceStatus.Created,
            candidateRoles: new[] { "role-y" });

        await store.SaveAsync(created);
        await store.SaveAsync(assigned);
        await store.SaveAsync(completed);
        await store.SaveAsync(otherRole);

        var pending = await store.GetPendingByCandidateRoleAsync("role-x");

        pending.Should().HaveCount(2);
        pending.Should().Contain(i => i.Id == "inst-01");
        pending.Should().Contain(i => i.Id == "inst-02");
    }

    [Fact]
    public async Task InMemoryHumanTaskInstanceStore_QueryOrganization_ReturnsPendingOnly()
    {
        var store = new InMemoryHumanTaskInstanceStore();

        var created = CreateInstanceWithCandidates("inst-01", HumanTaskInstanceStatus.Created,
            orgId: "org-dept-1");
        var assigned = CreateInstanceWithCandidates("inst-02", HumanTaskInstanceStatus.Assigned,
            orgId: "org-dept-1");
        var completed = CreateInstanceWithCandidates("inst-03", HumanTaskInstanceStatus.Completed,
            orgId: "org-dept-1");
        var otherOrg = CreateInstanceWithCandidates("inst-04", HumanTaskInstanceStatus.Created,
            orgId: "org-dept-2");

        await store.SaveAsync(created);
        await store.SaveAsync(assigned);
        await store.SaveAsync(completed);
        await store.SaveAsync(otherOrg);

        var pending = await store.GetPendingByOrganizationAsync("org-dept-1");

        pending.Should().HaveCount(2);
        pending.Should().Contain(i => i.Id == "inst-01");
        pending.Should().Contain(i => i.Id == "inst-02");
    }

    [Fact]
    public async Task InMemoryHumanTaskInstanceStore_QueryPosition_ReturnsPendingOnly()
    {
        var store = new InMemoryHumanTaskInstanceStore();

        var created = CreateInstanceWithCandidates("inst-01", HumanTaskInstanceStatus.Created,
            positionId: "pos-manager");
        var assigned = CreateInstanceWithCandidates("inst-02", HumanTaskInstanceStatus.Assigned,
            positionId: "pos-manager");
        var completed = CreateInstanceWithCandidates("inst-03", HumanTaskInstanceStatus.Completed,
            positionId: "pos-manager");
        var otherPos = CreateInstanceWithCandidates("inst-04", HumanTaskInstanceStatus.Created,
            positionId: "pos-engineer");

        await store.SaveAsync(created);
        await store.SaveAsync(assigned);
        await store.SaveAsync(completed);
        await store.SaveAsync(otherPos);

        var pending = await store.GetPendingByPositionAsync("pos-manager");

        pending.Should().HaveCount(2);
        pending.Should().Contain(i => i.Id == "inst-01");
        pending.Should().Contain(i => i.Id == "inst-02");
    }

    [Fact]
    public async Task InMemoryHumanTaskInstanceStore_ReturnsClones_ForNewFields()
    {
        var store = new InMemoryHumanTaskInstanceStore();
        var instance = CreateInstanceWithCandidates("inst-01", HumanTaskInstanceStatus.Created,
            candidateUsers: new[] { "user-a" }, candidateRoles: new[] { "role-x" },
            orgId: "org-dept-1", positionId: "pos-manager");

        await store.SaveAsync(instance);

        var returned = await store.GetPendingByCandidateUserAsync("user-a");
        var clone = returned[0];

        // Mutate the returned clone — should not affect store
        // (IReadOnlyList prevents mutation; this verifies it's a different object)
        clone.OrganizationUnitId = "mutated";

        var recheck = await store.GetPendingByOrganizationAsync("org-dept-1");
        recheck.Should().HaveCount(1); // still findable by original org
    }
```

- [ ] **Step 2: Run store tests**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests --filter "FullyQualifiedName~InMemoryHumanTaskInstanceStoreTests"
```
Expected: all existing + 6 new tests pass.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.HumanTask.Tests/InMemoryHumanTaskInstanceStoreTests.cs
git commit -m "test(Phase5f): add 6 store/instance tests — clone fields, 4 pending queries, mutation guard"
```

---

## Task 10: Wire Resolver into DefaultHumanTaskRuntime

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask/DefaultHumanTaskRuntime.cs`

- [ ] **Step 1: Inject resolver, rewrite CreateAsync**

Change constructor to accept `IHumanTaskAssigneeResolver`:

```csharp
    private readonly IHumanTaskRegistry _registry;
    private readonly IHumanTaskInstanceStore _store;
    private readonly ILocalEventBus _eventBus;
    private readonly IHumanTaskAssigneeResolver _resolver;  // NEW

    public DefaultHumanTaskRuntime(
        IHumanTaskRegistry registry,
        IHumanTaskInstanceStore store,
        ILocalEventBus eventBus,
        IHumanTaskAssigneeResolver resolver)  // NEW parameter
    {
        _registry = registry;
        _store = store;
        _eventBus = eventBus;
        _resolver = resolver;
    }
```

Replace `CreateAsync` method:

```csharp
    public async Task<HumanTaskInstance> CreateAsync(
        HumanTaskCreationRequest request, CancellationToken ct = default)
    {
        HumanTaskDescriptor? descriptor;
        if (request.Version.HasValue)
            descriptor = _registry.GetByVersion(request.HumanTaskId, request.Version.Value);
        else
            descriptor = _registry.GetById(request.HumanTaskId);

        if (descriptor == null)
            throw new InvalidOperationException(
                $"HumanTask descriptor '{request.HumanTaskId}' not found.");

        // Phase 5f: resolve assignee before creating instance
        var resolution = await _resolver.ResolveAsync(descriptor, request, ct)
            .ConfigureAwait(false);

        var instance = new HumanTaskInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            HumanTaskId = descriptor.Id,
            HumanTaskVersion = descriptor.Version,
            Status = (!string.IsNullOrWhiteSpace(resolution.AssigneeUserId)
                   || !string.IsNullOrWhiteSpace(resolution.AssigneeRoleId))
                ? HumanTaskInstanceStatus.Assigned
                : HumanTaskInstanceStatus.Created,
            TenantId = request.TenantId,
            AssigneeUserId = resolution.AssigneeUserId,
            AssigneeRoleId = resolution.AssigneeRoleId,
            CandidateUserIds = resolution.CandidateUserIds.ToArray(),
            CandidateRoleIds = resolution.CandidateRoleIds.ToArray(),
            OrganizationUnitId = resolution.OrganizationUnitId,
            PositionId = resolution.PositionId,
            AssigneeResolutionReason = resolution.AssigneeResolutionReason,
            WorkflowInstanceId = request.WorkflowInstanceId,
            WorkflowStepId = request.WorkflowStepId,
            Input = request.Input,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }
```

**Snapshot contract**: `resolution.CandidateUserIds.ToArray()` and `.CandidateRoleIds.ToArray()` — defense copy before storing on instance.

- [ ] **Step 2: Build to verify + run existing HumanTask tests to check no regressions**

```bash
dotnet build framework/src/CrestCreates.HumanTask && dotnet test framework/test/CrestCreates.HumanTask.Tests
```
Expected: build succeeds. Existing tests may fail due to resolver not being injected — this will be fixed in Task 12 (tests). Old `HumanTaskRuntimeTests.CreateAsync_Creates_Instance_From_Descriptor` will break because the runtime constructor changed. This is expected; we fix tests in Task 12.

- [ ] **Step 3: Verify existing tests fail as expected (constructor dependency missing)**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests --filter "FullyQualifiedName~HumanTaskRuntimeTests.CreateAsync_Creates_Instance_From_Descriptor"
```
Expected: FAIL — `InvalidOperationException` or DI error from missing resolver.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/DefaultHumanTaskRuntime.cs
git commit -m "feat(Phase5f): wire IHumanTaskAssigneeResolver into DefaultHumanTaskRuntime.CreateAsync"
```

---

## Task 11: Register Resolver in DI

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs`

- [ ] **Step 1: Add resolver registration**

Add one line after the existing `TryAddScoped` for runtime:

```csharp
using CrestCreates.HumanTask.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.HumanTask;

public static class HumanTaskServiceCollectionExtensions
{
    public static IServiceCollection AddHumanTaskRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IHumanTaskInstanceStore, InMemoryHumanTaskInstanceStore>();
        services.TryAddScoped<IHumanTaskRuntime, DefaultHumanTaskRuntime>();
        services.TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>();
        return services;
    }
}
```

- [ ] **Step 2: Build to verify**

```bash
dotnet build framework/src/CrestCreates.HumanTask
```

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs
git commit -m "feat(Phase5f): register IHumanTaskAssigneeResolver as Scoped in AddHumanTaskRuntime"
```

---

## Task 12: Update Runtime Tests for Resolver Integration + New Tests

**Files:**
- Modify: `framework/test/CrestCreates.HumanTask.Tests/HumanTaskRuntimeTests.cs`

- [ ] **Step 1: Add resolver mock to CreateRuntime helper**

Replace the `CreateRuntime` private helper method:

```csharp
    private static (DefaultHumanTaskRuntime runtime, InMemoryHumanTaskInstanceStore store,
        Mock<ILocalEventBus> eventBusMock, Mock<IHumanTaskAssigneeResolver> resolverMock)
        CreateRuntime(HumanTaskRegistry registry,
            Mock<ILocalEventBus>? busMock = null,
            Mock<IHumanTaskAssigneeResolver>? resolverMock = null)
    {
        var store = new InMemoryHumanTaskInstanceStore();
        var eventBus = busMock ?? new Mock<ILocalEventBus>();
        var resolver = resolverMock ?? new Mock<IHumanTaskAssigneeResolver>();

        // Default resolver behavior: unassigned (passes through to old logic)
        resolver
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution());

        var runtime = new DefaultHumanTaskRuntime(registry, store, eventBus.Object, resolver.Object);
        return (runtime, store, eventBus, resolver);
    }
```

Update all existing `CreateRuntime` call sites in the file — change from `(runtime, store, _)` to `(runtime, store, _, _)` or `(runtime, store, eventBus, _)` depending on usage:

- Line `CreateAsync_Creates_Instance_From_Descriptor`: change `(runtime, store, _)` → `(runtime, store, _, _)`
- Line `CreateAsync_Throws_When_Descriptor_Not_Found`: change `(runtime, _, _)` → `(runtime, _, _, _)`
- Line `CompleteAsync_Completes_Instance_And_Publishes_Event`: change `(runtime, store, _)` → `(runtime, store, eventBus, _)` where `eventBus` variable already exists and the last `_` is the resolver mock (unused)
- Line `CompleteAsync_Throws_When_Outcome_Invalid`: change `(runtime, store, _)` → `(runtime, store, _, _)`
- Line `CompleteAsync_Throws_When_Instance_Already_Completed`: change `(runtime, store, _)` → `(runtime, store, _, _)`
- Line `CancelAsync_Cancels_Instance`: change `(runtime, store, _)` → `(runtime, store, _, _)`
- Line `CompleteAsync_DoesNotPublishEvent_When_SaveConcurrencyFails`: the constructor call `new DefaultHumanTaskRuntime(registry, throwingStore, eventBusMock.Object)` needs a 4th arg (resolver mock). Create one inline: `new Mock<IHumanTaskAssigneeResolver>().Object` with default unassigned behavior.

- [ ] **Step 2: Add 6 new runtime tests**

Append before the closing `}` of class (after `CompleteAsync_DoesNotPublishEvent_When_SaveConcurrencyFails`):

```csharp
    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_Applies_AssigneeResolution_User()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution
            {
                AssigneeUserId = "resolved-user",
                CandidateRoleIds = new[] { "resolved-role" }
            });
        var (runtime, store, _, _) = CreateRuntime(registry, resolverMock: resolverMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        instance.AssigneeUserId.Should().Be("resolved-user");
        instance.CandidateRoleIds.Should().BeEquivalentTo(new[] { "resolved-role" });
        instance.Status.Should().Be(HumanTaskInstanceStatus.Assigned);

        var stored = await store.GetByIdAsync(instance.Id);
        stored!.AssigneeUserId.Should().Be("resolved-user");
        stored!.CandidateRoleIds.Should().BeEquivalentTo(new[] { "resolved-role" });
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_Applies_AssigneeResolution_Role()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution
            {
                AssigneeRoleId = "resolved-role"
            });
        var (runtime, store, _, _) = CreateRuntime(registry, resolverMock: resolverMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        instance.AssigneeRoleId.Should().Be("resolved-role");
        instance.Status.Should().Be(HumanTaskInstanceStatus.Assigned);
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_WithCandidates_StatusCreated()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution
            {
                CandidateUserIds = new[] { "candidate-1", "candidate-2" }
            });
        var (runtime, store, _, _) = CreateRuntime(registry, resolverMock: resolverMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        instance.CandidateUserIds.Should().BeEquivalentTo(new[] { "candidate-1", "candidate-2" });
        instance.Status.Should().Be(HumanTaskInstanceStatus.Created);
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_Stores_OrganizationUnit_And_Position()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution
            {
                OrganizationUnitId = "org-dept-1",
                PositionId = "pos-manager",
                AssigneeResolutionReason = "context-based assignment"
            });
        var (runtime, store, _, _) = CreateRuntime(registry, resolverMock: resolverMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        instance.OrganizationUnitId.Should().Be("org-dept-1");
        instance.PositionId.Should().Be("pos-manager");
        instance.AssigneeResolutionReason.Should().Be("context-based assignment");
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_ResolverException_Propagates_AndDoesNotSave()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var resolverMock = new Mock<IHumanTaskAssigneeResolver>();
        resolverMock
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("resolver failure"));

        var store = new InMemoryHumanTaskInstanceStore();
        var eventBus = new Mock<ILocalEventBus>();
        var runtime = new DefaultHumanTaskRuntime(
            registry, store, eventBus.Object, resolverMock.Object);

        await runtime.Invoking(r => r.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        })).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("resolver failure");

        // Store should be empty — no instance was saved
        var allPending = await store.GetPendingByWorkflowAsync("any-wf");
        allPending.Should().BeEmpty();
    }

    [Fact]
    public async Task HumanTaskRuntime_CreateAsync_ExplicitAssignment_Works_WithoutOrganizationServices()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        // Use the real resolver (no Organization dependency needed)
        var resolver = new DefaultHumanTaskAssigneeResolver();
        var store = new InMemoryHumanTaskInstanceStore();
        var eventBus = new Mock<ILocalEventBus>();
        var runtime = new DefaultHumanTaskRuntime(
            registry, store, eventBus.Object, resolver);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            AssigneeUserId = "user-1",
            RequestedOrganizationUnitId = "org-dept-1",
            RequestedPositionId = "pos-manager"
        });

        instance.AssigneeUserId.Should().Be("user-1");
        instance.OrganizationUnitId.Should().Be("org-dept-1");
        instance.PositionId.Should().Be("pos-manager");
        instance.Status.Should().Be(HumanTaskInstanceStatus.Assigned);
    }
```

- [ ] **Step 3: Also fix the `ConcurrencyThrowingHumanTaskInstanceStore` helper**

The class must implement the 4 new interface methods. Add after `GetPendingByWorkflowAsync`:

```csharp
    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(
        string userId, CancellationToken ct = default)
        => _inner.GetPendingByCandidateUserAsync(userId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(
        string roleId, CancellationToken ct = default)
        => _inner.GetPendingByCandidateRoleAsync(roleId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(
        string organizationUnitId, CancellationToken ct = default)
        => _inner.GetPendingByOrganizationAsync(organizationUnitId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(
        string positionId, CancellationToken ct = default)
        => _inner.GetPendingByPositionAsync(positionId, ct);
```

Also update the `CompleteAsync_DoesNotPublishEvent_When_SaveConcurrencyFails` test — the constructor call `new DefaultHumanTaskRuntime(registry, throwingStore, eventBusMock.Object)` now needs a 4th resolver parameter. Add a default mock:

```csharp
        var resolver = new Mock<IHumanTaskAssigneeResolver>();
        resolver
            .Setup(r => r.ResolveAsync(
                It.IsAny<HumanTaskDescriptor>(),
                It.IsAny<HumanTaskCreationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HumanTaskAssigneeResolution());

        var runtime = new DefaultHumanTaskRuntime(registry, throwingStore, eventBusMock.Object, resolver.Object);
```

- [ ] **Step 4: Run all HumanTask tests**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests
```
Expected: all tests pass (existing ~10 + 10 resolver + 6 store + 6 new runtime = ~32 tests total).

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.HumanTask.Tests/HumanTaskRuntimeTests.cs
git commit -m "test(Phase5f): update runtime tests for resolver integration — fix existing tests + 6 new tests"
```

---

## Task 13: Regression Gate — Full Solution Build + Cross-Project Tests

- [ ] **Step 1: Full solution build**

```bash
dotnet build
```
Expected: 0 errors across all projects.

- [ ] **Step 2: Run Workflow tests (must not regress)**

```bash
dotnet test framework/test/CrestCreates.Workflow.Tests
```
Expected: all pass (zero Workflow changes).

- [ ] **Step 3: Run Organization tests (must not regress)**

```bash
dotnet test framework/test/CrestCreates.Organization.Tests
```
Expected: all pass (no Organization dependency).

- [ ] **Step 4: Run Capability tests (must not regress)**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests
```
Expected: all pass.

- [ ] **Step 5: Final verify — all HumanTask tests**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests --verbosity normal
```
Expected: all tests green.

- [ ] **Step 6: Commit (if any test adjustments needed)**

```bash
git add -A && git commit -m "test(Phase5f): regression gate — full build + cross-project tests pass"
```

---

## Task 14: Update memory.md

**Files:**
- Modify: `memory.md`

- [ ] **Step 1: Add Phase 5f entry**

Append after the Phase 5e entry (before `---` divider of Known Important Decisions), add:

```markdown
### HumanTask Assignee Resolver Foundation (Phase 5f, 2026-06-12)

- `IHumanTaskAssigneeResolver` + `DefaultHumanTaskAssigneeResolver` — 4-priority resolution: explicit user > explicit role > auxiliary context (org/position) > strategy fallback.
- `HumanTaskAssigneeResolution` DTO with computed `IsAssigned`/`HasCandidates`/`IsUnassigned` using `!string.IsNullOrWhiteSpace`.
- `HumanTaskCreationRequest` extended: `RequestedOrganizationUnitId`, `RequestedPositionId`, `RequestedByUserId` (audit only).
- `HumanTaskInstance` extended: `CandidateUserIds`, `CandidateRoleIds`, `OrganizationUnitId`, `PositionId`, `AssigneeResolutionReason`. Clone snapshots with `.ToArray()`.
- `IHumanTaskInstanceStore` + `InMemoryHumanTaskInstanceStore` extended: 4 new pending queries (by candidate user/role, organization, position).
- `DefaultHumanTaskRuntime.CreateAsync` wired through resolver; status decision uses `!string.IsNullOrWhiteSpace`.
- DI: `TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>()`.
- 20 new tests (10 resolver, 6 runtime, 4 store). Zero Workflow changes. Zero Organization dependency.
- **Caveat**: RoundRobin/LeastLoaded return unassigned with reason string. No Organization-based auto-selection. No claim/delegate/transfer. No HumanTaskCreatedEvent.
```

- [ ] **Step 2: Commit**

```bash
git add memory.md
git commit -m "docs: update memory.md for Phase 5f HumanTask Assignee Resolver Foundation"
```

---

## Implementation Order

1. **Task 1**: `HumanTaskAssigneeResolution` (new DTO — no deps)
2. **Task 2**: `IHumanTaskAssigneeResolver` (interface — no deps)
3. **Task 3**: `DefaultHumanTaskAssigneeResolver` (depends on Tasks 1, 2)
4. **Task 4**: Resolver tests (depends on Task 3)
5. **Task 5**: `HumanTaskCreationRequest` fields (no deps)
6. **Task 6**: `HumanTaskInstance` fields + Clone (no deps)
7. **Task 7**: `IHumanTaskInstanceStore` methods (depends on Task 6)
8. **Task 8**: `InMemoryHumanTaskInstanceStore` impl (depends on Task 7)
9. **Task 9**: Store/Instance tests (depends on Tasks 6, 8)
10. **Task 10**: Wire resolver into `DefaultHumanTaskRuntime` (depends on Tasks 1-3, 6)
11. **Task 11**: DI registration (depends on Task 3)
12. **Task 12**: Runtime test updates (depends on Tasks 10, 11)
13. **Task 13**: Regression gate (depends on all)
14. **Task 14**: Update memory.md (depends on all)

Tasks 1, 2, 5, 6 can run in parallel. Tasks 7+8 are sequential. Tasks 4, 9, 12 are test-only and can partially overlap with implementation tasks.

---

## File Change Summary

| File | Lines Added | Lines Modified | Lines Deleted |
|------|-------------|----------------|---------------|
| `HumanTaskAssigneeResolution.cs` | +50 | 0 | 0 |
| `IHumanTaskAssigneeResolver.cs` | +10 | 0 | 0 |
| `DefaultHumanTaskAssigneeResolver.cs` | +55 | 0 | 0 |
| `HumanTaskCreationRequest.cs` | +3 | 0 | 0 |
| `HumanTaskInstance.cs` | +10 | +5 (Clone) | 0 |
| `IHumanTaskInstanceStore.cs` | +10 | 0 | 0 |
| `InMemoryHumanTaskInstanceStore.cs` | +60 | 0 | 0 |
| `DefaultHumanTaskRuntime.cs` | +15 | ~30 (CreateAsync) | ~15 (old CreateAsync) |
| `HumanTaskServiceCollectionExtensions.cs` | +1 | 0 | 0 |
| `HumanTaskAssigneeResolverTests.cs` | +230 | 0 | 0 |
| `HumanTaskRuntimeTests.cs` | +180 | ~20 (fixes) | 0 |
| `InMemoryHumanTaskInstanceStoreTests.cs` | +200 | 0 | 0 |
| **Total** | **~824** | **~55** | **~15** |
