# Phase 5f: HumanTask Assignee Resolver Foundation — Design Spec

**Date**: 2026-06-12
**Status**: Approved
**Depends on**: Phase 5b (Durable Runtime Store Contracts), Phase 5c (Organization Identity Kernel)

---

## 1. Objective

Establish the minimal main chain for HumanTask assignee resolution. When `DefaultHumanTaskRuntime.CreateAsync` creates a task instance, it must go through `IHumanTaskAssigneeResolver` to determine who the task is assigned to (or made available to), rather than relying solely on ad-hoc `request.AssigneeUserId`/`AssigneeRoleId` checks.

```
HumanTaskCreationRequest + HumanTaskDescriptor
         ↓
 IHumanTaskAssigneeResolver.ResolveAsync
         ↓
 HumanTaskAssigneeResolution
         ↓
 DefaultHumanTaskRuntime.CreateAsync
         ↓
 HumanTaskInstance (persisted with resolved assignee/candidate/org/position)
         ↓
 InMemoryHumanTaskInstanceStore (extended pending queries)
```

---

## 2. Constraints

### Phase 5f does NOT:

- Claim / Delegate / Transfer / Escalate tasks
- SLA / Timeout runtime / Reminder / Notification
- UI / API / AppService
- EF Core / SqlSugar / Dapper / MongoDB / Redis persistence
- Outbox / Distributed lock
- Workflow Branch / Transition / Retry / Compensation / SubWorkflow changes
- Capability Authorization modification
- DataPermission integration
- IPermissionChecker modification
- Claims/token modification
- Organization role → RBAC wiring
- Runtime reflection
- RoundRobin / LeastLoaded real algorithms
- Descriptor model overhaul
- New `AssigneeStrategyKind`
- New `UserId`/`RoleId`/`OrganizationUnitId`/`PositionId` fields on `HumanTaskDescriptor`

### Boundaries (unchanged):

- **Workflow**: zero changes. `HumanTaskStepExecutor` unchanged. `WorkflowExecutionContext` unchanged. No `IServiceProvider` injection, no HTTP/claims/token dependency.
- **Organization**: HumanTask projects do NOT reference `CrestCreates.Organization.Abstractions`.
- **Descriptor model**: `AssigneeStrategy` remains a simple enum. No parameter fields added.

---

## 3. Design

### 3.1 New Types (HumanTask.Abstractions)

#### 3.1.1 `HumanTaskAssigneeResolution`

Result DTO, immutable via init-only properties. Candidate lists are snapshotted.

```csharp
public sealed class HumanTaskAssigneeResolution
{
    // Data fields
    public string? AssigneeUserId { get; init; }
    public string? AssigneeRoleId { get; init; }
    public IReadOnlyList<string> CandidateUserIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CandidateRoleIds { get; init; } = Array.Empty<string>();
    public string? OrganizationUnitId { get; init; }
    public string? PositionId { get; init; }
    public string? AssigneeResolutionReason { get; init; }

    // Computed properties (whitespace-only strings treated as null)
    public bool IsAssigned => !string.IsNullOrWhiteSpace(AssigneeUserId)
                           || !string.IsNullOrWhiteSpace(AssigneeRoleId);
    public bool HasCandidates => CandidateUserIds.Count > 0 || CandidateRoleIds.Count > 0;
    public bool IsUnassigned => !IsAssigned && !HasCandidates
        && string.IsNullOrWhiteSpace(OrganizationUnitId)
        && string.IsNullOrWhiteSpace(PositionId);
}
```

**Rules**:
- `CandidateUserIds` / `CandidateRoleIds` must be snapshot (never expose mutable List).
- `AssigneeResolutionReason` is for debug/audit only — not used in core logic.

**Snapshot responsibility at each layer** (prevents mutable collection leaks):

| Layer | File | Responsibility |
|-------|------|----------------|
| Resolver | `DefaultHumanTaskAssigneeResolver` | Return `Array.Empty<string>()` or `string[]` (never `List<string>` cast to `IReadOnlyList`) |
| Runtime | `DefaultHumanTaskRuntime.CreateAsync` | Apply resolution to instance: `CandidateUserIds = resolution.CandidateUserIds.ToArray()`, `CandidateRoleIds = resolution.CandidateRoleIds.ToArray()` |
| Instance | `HumanTaskInstance.Clone()` | `CandidateUserIds = this.CandidateUserIds.ToArray()`, `CandidateRoleIds = this.CandidateRoleIds.ToArray()` |
| Store | `InMemoryHumanTaskInstanceStore.SaveAsync` | Relies on `Clone()` — never stores external collection references directly |

#### 3.1.2 `IHumanTaskAssigneeResolver`

```csharp
public interface IHumanTaskAssigneeResolver
{
    Task<HumanTaskAssigneeResolution> ResolveAsync(
        HumanTaskDescriptor descriptor,
        HumanTaskCreationRequest request,
        CancellationToken cancellationToken = default);
}
```

**Constraints**:
- No dependency on WorkflowInstance
- No dependency on IServiceProvider
- Does not modify descriptor
- Does not save instance
- Does not access HTTP Context
- Does not perform RBAC authorization
- Does not call IPermissionChecker
- Does not integrate DataPermission

### 3.2 New Implementation (HumanTask)

#### 3.2.1 `DefaultHumanTaskAssigneeResolver`

Resolution priority, in order:

| Priority | Condition | Action |
|----------|-----------|--------|
| 1 | `!string.IsNullOrWhiteSpace(request.AssigneeUserId)` | `AssigneeUserId = request.AssigneeUserId` |
| | `!string.IsNullOrWhiteSpace(request.AssigneeRoleId)` (also) | `CandidateRoleIds = [request.AssigneeRoleId]` (user takes precedence, role becomes candidate) |
| 2 | `!string.IsNullOrWhiteSpace(request.AssigneeRoleId)` (no user) | `AssigneeRoleId = request.AssigneeRoleId` |
| 3 | `!string.IsNullOrWhiteSpace(request.RequestedOrganizationUnitId)` | `OrganizationUnitId = request.RequestedOrganizationUnitId` |
| | `!string.IsNullOrWhiteSpace(request.RequestedPositionId)` | `PositionId = request.RequestedPositionId` |
| 4 | `descriptor.AssigneeStrategy` | See strategy adapter table below |

**Strategy adapter behavior**:

| Strategy | Behavior | ResolutionReason |
|----------|----------|-----------------|
| `SingleUser` | Only assigns if `request.AssigneeUserId` is set (already handled by priority 1). Otherwise unassigned. | N/A (no change to resolution) |
| `CandidateGroup` | Only assigns if `request.AssigneeRoleId` is set (already handled by priority 1/2). Otherwise unassigned. | N/A |
| `RoundRobin` | Returns unassigned. | `"RoundRobin strategy is not yet implemented"` |
| `LeastLoaded` | Returns unassigned. | `"LeastLoaded strategy is not yet implemented"` |

> **Note**: Priorities 1-3 are applied BEFORE strategy. Strategy is a fallback — it does not override explicit request values.

Priority 1 convention (locked in test): when both `AssigneeUserId` and `AssigneeRoleId` are provided, user wins as assignee, role is recorded as a candidate.

### 3.3 Modified Types

#### 3.3.1 `HumanTaskCreationRequest` (3 new fields)

```csharp
// New fields added:
public string? RequestedOrganizationUnitId { get; init; }
public string? RequestedPositionId { get; init; }
public string? RequestedByUserId { get; init; }
```

> `RequestedByUserId` is context/audit metadata only. It is NOT used for resolution in Phase 5f. Do not add duplicate `RequestedAssigneeUserId`/`RequestedAssigneeRoleId` — `AssigneeUserId`/`AssigneeRoleId` already serve that purpose.

#### 3.3.2 `HumanTaskInstance` (5 new fields)

```csharp
// New fields:
public IReadOnlyList<string> CandidateUserIds { get; set; } = Array.Empty<string>();
public IReadOnlyList<string> CandidateRoleIds { get; set; } = Array.Empty<string>();
public string? OrganizationUnitId { get; set; }
public string? PositionId { get; set; }
public string? AssigneeResolutionReason { get; set; }
```

**Requirements**:
- `Clone()` MUST copy all 5 new fields
- Store save/return MUST snapshot candidate lists (not expose mutable references)
- Candidate lists use `IReadOnlyList<string>`, not `List<string>` as a leaky abstraction
- No new Claim/Delegate status tables

#### 3.3.3 `DefaultHumanTaskRuntime.CreateAsync` (modified flow)

New flow:

1. Resolve `HumanTaskDescriptor` (unchanged)
2. Call `_resolver.ResolveAsync(descriptor, request, ct)` → `HumanTaskAssigneeResolution`
3. Create `HumanTaskInstance`
4. Apply resolution fields to instance:
   - `AssigneeUserId`, `AssigneeRoleId`, `CandidateUserIds`, `CandidateRoleIds`
   - `OrganizationUnitId`, `PositionId`, `AssigneeResolutionReason`
5. Status decision (non-whitespace identity fields → Assigned):
   - `!string.IsNullOrWhiteSpace(resolution.AssigneeUserId)` OR `!string.IsNullOrWhiteSpace(resolution.AssigneeRoleId)` → `Assigned`
   - Otherwise → `Created`
6. `SaveAsync`
7. Return instance

**Error semantics**:
- Resolver exception → propagates, store is NOT called, instance is NOT saved
- No `HumanTaskCreatedEvent` published (event does not exist in the project)

**Constructor change**: +1 parameter `IHumanTaskAssigneeResolver`.

#### 3.3.4 `IHumanTaskInstanceStore` (4 new methods)

```csharp
Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateUserAsync(
    string userId, CancellationToken ct = default);

Task<IReadOnlyList<HumanTaskInstance>> GetPendingByCandidateRoleAsync(
    string roleId, CancellationToken ct = default);

Task<IReadOnlyList<HumanTaskInstance>> GetPendingByOrganizationAsync(
    string organizationUnitId, CancellationToken ct = default);

Task<IReadOnlyList<HumanTaskInstance>> GetPendingByPositionAsync(
    string positionId, CancellationToken ct = default);
```

**Semantics**:
- `pending` = `Status == Created || Status == Assigned`
- Completed / Cancelled are excluded
- `CandidateUserIds` / `CandidateRoleIds` matched via `.Contains()`
- `OrganizationUnitId` / `PositionId` matched via exact equality
- Return cloned snapshots
- Store does NOT perform organization hierarchy descendant queries

#### 3.3.5 `InMemoryHumanTaskInstanceStore` (4 new query implementations)

Following the existing pattern (filter → clone → AsReadOnly → Task.FromResult):

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
```

Same pattern for `CandidateRole`, `Organization`, `Position` variants.

### 3.4 DI Registration

```csharp
public static IServiceCollection AddHumanTaskRuntime(this IServiceCollection services)
{
    services.TryAddSingleton<IHumanTaskInstanceStore, InMemoryHumanTaskInstanceStore>();
    services.TryAddScoped<IHumanTaskRuntime, DefaultHumanTaskRuntime>();
    services.TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>();  // NEW
    return services;
}
```

- `IHumanTaskInstanceStore` remains Singleton
- `IHumanTaskRuntime` remains Scoped
- `IHumanTaskAssigneeResolver` is Scoped (new)
- `TryAdd` semantics — never override consumer registrations
- No `IServiceProvider` lazy resolve workaround

---

## 4. File Manifest

| Action | File | Project |
|--------|------|---------|
| NEW | `HumanTaskAssigneeResolution.cs` | `HumanTask.Abstractions` |
| NEW | `IHumanTaskAssigneeResolver.cs` | `HumanTask.Abstractions` |
| NEW | `DefaultHumanTaskAssigneeResolver.cs` | `HumanTask` |
| MODIFY | `HumanTaskCreationRequest.cs` | `HumanTask.Abstractions` |
| MODIFY | `HumanTaskInstance.cs` | `HumanTask.Abstractions` |
| MODIFY | `IHumanTaskInstanceStore.cs` | `HumanTask.Abstractions` |
| MODIFY | `DefaultHumanTaskRuntime.cs` | `HumanTask` |
| MODIFY | `InMemoryHumanTaskInstanceStore.cs` | `HumanTask` |
| MODIFY | `HumanTaskServiceCollectionExtensions.cs` | `HumanTask` |
| NEW | `HumanTaskAssigneeResolverTests.cs` | `HumanTask.Tests` |
| MODIFY | `HumanTaskRuntimeTests.cs` | `HumanTask.Tests` |
| MODIFY | `InMemoryHumanTaskInstanceStoreTests.cs` | `HumanTask.Tests` |

---

## 5. Test Plan (20 tests)

### 5.1 Resolver Tests (new file: `HumanTaskAssigneeResolverTests.cs`)

1. `AssigneeResolver_ExplicitUser_AssignsUser` — `request.AssigneeUserId = "u1"` → `resolution.AssigneeUserId == "u1"`
2. `AssigneeResolver_ExplicitRole_AssignsRole` — `request.AssigneeRoleId = "r1"` → `resolution.AssigneeRoleId == "r1"`
3. `AssigneeResolver_UserTakesPrecedence_WhenUserAndRoleBothProvided` — both set → user wins, role in CandidateRoleIds
4. `AssigneeResolver_SingleUserWithoutExplicitAssignee_ReturnsUnassigned` — empty request, default SingleUser strategy → `IsUnassigned == true`
5. `AssigneeResolver_CandidateGroup_WithExplicitRole_AssignsRole` — CandidateGroup + role → `AssigneeRoleId = "r1"`
6. `AssigneeResolver_RoundRobin_ReturnsUnassigned` — RoundRobin → unassigned + ResolutionReason
7. `AssigneeResolver_LeastLoaded_ReturnsUnassigned` — LeastLoaded → unassigned + ResolutionReason
8. `AssigneeResolver_RequestOrgAndPosition_StoresContext` — RequestedOrganizationUnitId/PositionId → stored in resolution

### 5.2 Runtime Tests (modify: `HumanTaskRuntimeTests.cs`)

9. `HumanTaskRuntime_CreateAsync_Applies_AssigneeResolution_User` — resolver returns user → instance assigned
10. `HumanTaskRuntime_CreateAsync_Applies_AssigneeResolution_Role` — resolver returns role → instance assigned
11. `HumanTaskRuntime_CreateAsync_WithCandidates_StatusCreated` — resolver returns candidates only → Status = Created
12. `HumanTaskRuntime_CreateAsync_Stores_OrganizationUnit_And_Position` — org/position stored on instance
13. `HumanTaskRuntime_CreateAsync_ResolverException_Propagates_AndDoesNotSave` — resolver throws → exception propagates, store untouched
14. `HumanTaskRuntime_CreateAsync_ExplicitAssignment_Works_WithoutOrganizationServices` — end-to-end without org DI

### 5.3 Instance/Store Tests (modify: `InMemoryHumanTaskInstanceStoreTests.cs`)

15. `HumanTaskInstance_Clone_Copies_AssigneeResolutionFields` — clone preserves all 5 new fields
16. `InMemoryHumanTaskInstanceStore_QueryCandidateUser_ReturnsPendingOnly` — GetPendingByCandidateUserAsync returns only Created/Assigned with matching candidate
17. `InMemoryHumanTaskInstanceStore_QueryCandidateRole_ReturnsPendingOnly`
18. `InMemoryHumanTaskInstanceStore_QueryOrganization_ReturnsPendingOnly`
19. `InMemoryHumanTaskInstanceStore_QueryPosition_ReturnsPendingOnly`
20. `InMemoryHumanTaskInstanceStore_ReturnsClones_ForNewFields` — returned instances are clones (mutations don't affect store)

### 5.4 Regression Gate

- `dotnet test framework/test/CrestCreates.HumanTask.Tests` — all pass (existing + new)
- `dotnet test framework/test/CrestCreates.Workflow.Tests` — all pass (zero Workflow changes)
- `dotnet test framework/test/CrestCreates.Organization.Tests` — all pass (no Organization dependency)
- `dotnet build` — 0 errors

---

## 6. Error Handling Summary

| Scenario | Behavior |
|----------|----------|
| Resolver throws | Exception propagates from `CreateAsync`. Store NOT called. Instance NOT saved. |
| Resolver returns `IsUnassigned` | Instance created with `Status = Created`. No error. |
| `InMemoryHumanTaskInstanceStore` candidate list mutation via returned clone | Prevented — store returns `.Clone()` snapshot. Resolver returns `Array.Empty<string>()` or `string[].` Runtime does `.ToArray()` defense copy. Clone does `.ToArray()`. |
| Concurrent save (`RuntimeConcurrencyException`) | Existing behavior preserved — CAS loop unchanged |

---

## 7. Dependencies (Project References)

```
HumanTask.Abstractions
  └─ Metadata.Abstractions
  └─ Schema.Abstractions
  └─ Capability.Abstractions
  └─ EventBus.Abstractions
  (NO Organization.Abstractions reference added)

HumanTask
  └─ HumanTask.Abstractions
  └─ Metadata.Abstractions
  └─ Metadata
  └─ EventBus.Abstractions
  (NO Organization reference added)
```

---

## 8. Unresolved for Future Phases

- RoundRobin / LeastLoaded real algorithms (require persistent counters + load tracking)
- Organization-based automatic assignee selection (requires `IOrganizationIdentityService` dependency)
- Claim / Delegate / Transfer task operations
- Escalation with timeout runtime
- Notification on assignment
- `RequestedByUserId` usage in resolution logic
- `HumanTaskCreatedEvent` publication
