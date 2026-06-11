# Phase 5b — Durable Runtime Store Contracts: Design Spec

**Date**: 2026-06-11
**Status**: Ready for Implementation
**Context**: Phase 5 completed. `IHumanTaskRuntime`, `IWorkflowInstanceStore`, `IHumanTaskInstanceStore` are operational but their contract boundaries are "memory-only" — no concurrency protection, no duplication defense, snapshot semantics missing.

---

## Goal

Harden Runtime Store contracts from "in-memory test-usable" to "future durable, concurrent, replaceable, auditable" stable boundaries. **No database persistence.** InMemory stores simulate durable store constraints (optimistic concurrency with atomic CAS, snapshot reads, correlation ambiguity detection).

## Architecture Principles

1. **Store ≠ State Machine**: Store persists/queries/protects concurrency. Runtime services (`IHumanTaskRuntime`, `IWorkflowExecutionRunner`, `IWorkflowContinuationService`) own business state transitions.
2. **Atomic CAS for Optimistic Concurrency**: Save uses `ConcurrentDictionary.TryUpdate(key, snapshot, existing)` so the stamp check and replacement happen as one atomic operation. No TOCTOU (time-of-check-time-of-use) gap.
3. **Shallow Snapshot Semantics**: Save stores a shallow copy; read returns a shallow copy. Framework-owned collections (`Dictionary`, `List`) are copied; `object?` opaque payloads (`Variables`, `Input`, `Output`, `Result`) are reference-shared — mutation of shared payload objects after save is unsupported. No reflection, no JSON serialization.
4. **Event-after-Persist**: `HumanTaskCompletedEvent` published ONLY after successful save. Concurrency failure suppresses the event.
5. **Idempotent Duplicate Handling**: Duplicate `HumanTaskCompletedEvent` does not double-advance workflow. `GetByWaitingHumanTaskId` returns only Suspended — if already resumed, return null (no-op).
6. **No reflection, NativeAOT-friendly**: Hand-written Clone methods.

## Scope Boundaries

### IN scope
- `IHasConcurrencyStamp` interface (Metadata.Abstractions)
- `RuntimeStoreException` / `RuntimeConcurrencyException` / `RuntimeEntityNotFoundException` (Metadata.Abstractions)
- `ConcurrencyStamp` + `UpdatedAt` on `WorkflowInstance`, `HumanTaskInstance`
- Atomic CAS concurrency in `InMemoryWorkflowInstanceStore`, `InMemoryHumanTaskInstanceStore`
- Shallow-snapshot save + read (hand-written Clone)
- `IWorkflowInstanceStore.GetByWaitingHumanTaskId` → Suspended-only filter + multi-match → `WorkflowCorrelationException`
- `IHumanTaskInstanceStore.GetPendingByWorkflowAsync` (new method)
- `DefaultHumanTaskRuntime.CompleteAsync` → reject already-completed, guard concurrency failure → no event
- `WorkflowContinuationService` → idempotent duplicate event (null → no-op), concurrency guard on save
- `WorkflowContinuationRequest` → legacy name comment + alias property
- 12 new/modified tests across Workflow.Tests and HumanTask.Tests (incl. real concurrent-conflict test)

### OUT of scope
- Database persistence (EF Core, SqlSugar, Dapper, Mongo, Redis)
- Outbox, UnitOfWork, distributed transactions, distributed locks
- Workflow Retry / Branch / Transition / Compensation / SubWorkflow
- HumanTask Claim / Delegate / Escalation / SLA / Timeout / Reminder
- Descriptor Topology Engine, Package/Snapshot, Dynamic API
- MCP Tool, Agent Tool, UI/Form rendering
- IServiceProvider in WorkflowExecutionContext
- Store as business state machine
- HumanTaskCompletedEvent with WorkflowInstanceId/WorkflowStepId
- DraftRecord / CapabilityExecutionRecord / DeadLetterMessage concurrency stamps (out of Phase 5b scope)

---

## Design

### 1. New Types in `CrestCreates.Metadata.Abstractions`

#### 1.1 Runtime Store Exceptions

```csharp
namespace CrestCreates.Metadata.Abstractions;

public class RuntimeStoreException : Exception
{
    public RuntimeStoreException(string message) : base(message) { }
    public RuntimeStoreException(string message, Exception innerException) : base(message, innerException) { }
}

public class RuntimeConcurrencyException : RuntimeStoreException
{
    public RuntimeConcurrencyException(string message) : base(message) { }
    public RuntimeConcurrencyException(string message, Exception innerException) : base(message, innerException) { }
}

public class RuntimeEntityNotFoundException : RuntimeStoreException
{
    public RuntimeEntityNotFoundException(string message) : base(message) { }
    public RuntimeEntityNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
```

**Placement rationale**: `CrestCreates.Metadata.Abstractions` is the framework-level abstractions project (references only `Domain.Shared`). These exceptions are cross-cutting — used by Workflow and HumanTask runtime modules. No new project needed.

**Why NOT in Domain.Shared**: Domain.Shared exceptions (`CrestException` hierarchy) serve the domain layer. Runtime store exceptions are infrastructure concerns — they belong alongside `BootstrapDependencyException` in Metadata.Abstractions.

> **Reuse check**: `WorkflowCorrelationException` already exists in `Workflow.Abstractions` — keep it, do NOT duplicate. `CrestEntityNotFoundException` exists in Domain.Shared — do NOT reuse for runtime store entities.

#### 1.2 `IHasConcurrencyStamp`

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IHasConcurrencyStamp
{
    string ConcurrencyStamp { get; set; }
}
```

Lightweight contract. No generic Repository, no `IHasRuntimeTimestamps`.

---

### 2. Modified Runtime Instance Types

#### 2.1 `WorkflowInstance` — Add `ConcurrencyStamp` + `UpdatedAt` + implement `IHasConcurrencyStamp`

Modify `CrestCreates.Workflow.Abstractions/WorkflowInstance.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;

public sealed class WorkflowInstance : IHasConcurrencyStamp
{
    // ... existing fields ...
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

- Existing fields: `InstanceId` (string), `Workflow` (VersionedDescriptorRef), `Status`, `CurrentStepId`, `StepIndex`, `WaitingHumanTaskId`, `StartedAt` (DateTimeOffset), `CompletedAt`, `Variables` (Dictionary), `StepVariables` (Dictionary), `StepResults` (List<WorkflowStepResult>), `ErrorMessage`.
- Do NOT duplicate `StartedAt`. `ConcurrencyStamp` is for optimistic concurrency only.
- Workflow.Abstractions already references Metadata.Abstractions (via `VersionedDescriptorRef`), so no new project reference needed.

#### 2.2 `HumanTaskInstance` — Add `ConcurrencyStamp` + `UpdatedAt` + implement `IHasConcurrencyStamp`

Modify `CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs`:

```csharp
using CrestCreates.Metadata.Abstractions;

public sealed class HumanTaskInstance : IHasConcurrencyStamp
{
    // ... existing fields ...
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? UpdatedAt { get; set; }
}
```

- Already has `CreatedAt`, `CompletedAt`, `CancelledAt` — do NOT change these.
- `UpdatedAt` is in addition to these timestamps, representing the last write time.
- HumanTask.Abstractions already references Metadata.Abstractions (via `VersionedDescriptorRef`), so no new project reference needed.

#### 2.3 Draft / Capability / DeadLetter — NOT in Phase 5b

Adding `ConcurrencyStamp` to `DraftRecord`, `CapabilityExecutionRecord`, or `DeadLetterMessage` is fully out of Phase 5b scope. These types are not in the critical `CompleteAsync → Save → Publish → Continuation` flow, and each would require its own store semantics and tests. Defer to future phases.

---

### 3. Modified InMemory Store Implementations

#### 3.1 `InMemoryWorkflowInstanceStore` — Atomic CAS Concurrency + Shallow Snapshot

**Current behavior** (Phase 5): Direct reference store. `_instances[instance.InstanceId] = instance`. `GetAsync` returns same reference. `GetByWaitingHumanTaskId` already has Suspended-only filter + `WorkflowCorrelationException` for multi-match.

**New SaveAsync with atomic CAS**:

```csharp
public Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default)
{
    // Always work with a shallow clone to simulate durable-store copy semantics
    var snapshot = instance.Clone();
    snapshot.UpdatedAt = DateTimeOffset.UtcNow;

    while (true)
    {
        if (!_instances.TryGetValue(instance.InstanceId, out var existing))
        {
            // First save — insert with fresh stamp
            snapshot.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            if (_instances.TryAdd(instance.InstanceId, snapshot))
            {
                instance.ConcurrencyStamp = snapshot.ConcurrencyStamp;
                instance.UpdatedAt = snapshot.UpdatedAt;
                return Task.CompletedTask;
            }
            // Race: another thread inserted — retry loop (will go to update path)
            continue;
        }

        // Update existing — check concurrency stamp atomically
        if (existing.ConcurrencyStamp != instance.ConcurrencyStamp)
            throw new RuntimeConcurrencyException(
                $"Concurrency conflict for WorkflowInstance '{instance.InstanceId}'. " +
                $"Expected stamp '{instance.ConcurrencyStamp}', actual '{existing.ConcurrencyStamp}'.");

        snapshot.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        if (_instances.TryUpdate(instance.InstanceId, snapshot, existing))
        {
            instance.ConcurrencyStamp = snapshot.ConcurrencyStamp;
            instance.UpdatedAt = snapshot.UpdatedAt;
            return Task.CompletedTask;
        }
        // Race: another thread updated between TryGetValue and TryUpdate — retry
    }
}
```

**Why CAS loop matters**: `TryGetValue` → compare → `_instances[id] = snapshot` has a TOCTOU gap. Two concurrent saves can both read the same stamp, both pass, and both publish downstream effects (duplicate events). The `TryUpdate(key, newValue, comparisonValue)` call is atomic — the dictionary itself validates that `existing` hasn't changed since read.

**Updated GetAsync**:

```csharp
public Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default)
{
    if (_instances.TryGetValue(instanceId, out var existing))
        return Task.FromResult<WorkflowInstance?>(existing.Clone());
    return Task.FromResult<WorkflowInstance?>(null);
}
```

**Unchanged: `GetByWaitingHumanTaskId`**: Already has Suspended-only filter + multi-match `WorkflowCorrelationException` from Phase 5. Phase 5b only adds `.Clone()` on return:

```csharp
public Task<WorkflowInstance?> GetByWaitingHumanTaskId(
    string humanTaskId, CancellationToken ct = default)
{
    var matches = _instances.Values
        .Where(i => i.Status == WorkflowInstanceStatus.Suspended &&
                    i.WaitingHumanTaskId == humanTaskId)
        .ToList();

    if (matches.Count > 1)
        throw new WorkflowCorrelationException(
            $"Multiple suspended instances found for HumanTask '{humanTaskId}'.");

    return Task.FromResult(matches.SingleOrDefault()?.Clone());
}
```

**`WorkflowInstance.Clone()`** (public, hand-written, no reflection):

```csharp
public WorkflowInstance Clone()
{
    return new WorkflowInstance
    {
        InstanceId = this.InstanceId,
        Workflow = this.Workflow,                       // readonly record struct — value copy
        Status = this.Status,
        CurrentStepId = this.CurrentStepId,
        StepIndex = this.StepIndex,
        WaitingHumanTaskId = this.WaitingHumanTaskId,
        StartedAt = this.StartedAt,
        CompletedAt = this.CompletedAt,
        Variables = new Dictionary<string, object?>(this.Variables),    // shallow: values are object? refs
        StepVariables = new Dictionary<string, object?>(this.StepVariables),
        StepResults = new List<WorkflowStepResult>(this.StepResults),   // shallow: elements are record refs
        ErrorMessage = this.ErrorMessage,
        ConcurrencyStamp = this.ConcurrencyStamp,
        UpdatedAt = this.UpdatedAt
    };
}
```

**Shallow-copy boundary**: `Variables`, `StepVariables`, `StepResults` collections are new instances (safe to mutate). Individual values within them are `object?` or record references — shared with the original. Mutation of shared opaque payloads after save is unsupported caller behavior.

**Clone visibility**: `Clone()` must be `public` because stores (`InMemoryWorkflowInstanceStore` in `CrestCreates.Workflow`) call it on instances defined in `CrestCreates.Workflow.Abstractions` — these are different assemblies with no `InternalsVisibleTo`. Similarly, `HumanTaskInstance.Clone()` is public.

**Key semantics**:
- Save always stores a **clone** — prevents shared reference mutation.
- Read always returns a **clone** — callers can't mutate store internals.
- First save: atomic `TryAdd` with fresh stamp.
- Update save: atomic `TryUpdate` with stamp comparison. Retry loop handles insertion races.
- `ConcurrencyStamp` comparison is exact (`!=`), case-sensitive.
- Stamp is written back to the caller's instance object after successful save.

#### 3.2 `InMemoryHumanTaskInstanceStore` — Same Concurrency Pattern

Same atomic CAS pattern as Workflow store, plus new `GetPendingByWorkflowAsync`:

```csharp
public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(
    string workflowInstanceId, CancellationToken ct = default)
{
    var results = _instances.Values
        .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                     i.Status == HumanTaskInstanceStatus.Assigned) &&
                    i.WorkflowInstanceId == workflowInstanceId)
        .Select(i => i.Clone())
        .ToList()
        .AsReadOnly();

    return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
}
```

- `GetPendingByAssigneeAsync`: existing filter logic stays; add `.Select(i => i.Clone())`.
- `GetByIdAsync`: always return clone, never direct reference.
- `HumanTaskInstance.Clone()` follows same shallow-copy pattern as WorkflowInstance.

---

### 4. Modified `DefaultHumanTaskRuntime` — Duplicate Completion Defense

In `CompleteAsync`:

1. Load instance via `_store.GetByIdAsync`.
2. If `null` → throw `RuntimeEntityNotFoundException`.
3. If `Status == Completed` → throw `InvalidOperationException` ("already completed").
4. If `Status == Cancelled` → throw `InvalidOperationException` ("cancelled").
5. Validate descriptor version (existing).
6. Validate outcome via `CompletionOutcomeMatcher.Resolve` (existing).
7. Set `Status = Completed`, `Outcome`, `Output`, `CompletedAt`.
8. `await _store.SaveAsync(instance, ct)` — may throw `RuntimeConcurrencyException`.
   - If it does: **DO NOT publish**. Let exception propagate.
9. If save succeeds: publish `HumanTaskCompletedEvent`.

**Current Phase 5 code already does save-then-publish** (correct ordering). Phase 5b adds:
- `RuntimeEntityNotFoundException` on missing instance (was `InvalidOperationException`).
- Concurrency guard: `SaveAsync` failure = no event. Caller sees the exception and can retry.

`CancelAsync`: same pattern — if `SaveAsync` throws, let it propagate. No event published on cancel (unchanged).

---

### 5. Modified `WorkflowContinuationService` — Duplicate Event Idempotency

**Current Phase 5 behavior**: `ContinueAsync` → `GetByWaitingHumanTaskId` → if `null`, throws `InvalidOperationException`.

**Phase 5b required behavior**: `null` = idempotent no-op. Must also mirror the existing Phase 5 step recording + variable pattern (was already correct, but needs to be preserved verbatim):

```csharp
public async Task ContinueAsync(
    WorkflowContinuationRequest request, CancellationToken ct = default)
{
    var instance = await _store.GetByWaitingHumanTaskId(request.HumanTaskId, ct)
        .ConfigureAwait(false);

    // Phase 5b change: null → idempotent no-op (was: throw)
    if (instance == null)
        return;

    if (instance.Status != WorkflowInstanceStatus.Suspended)
        throw new InvalidOperationException(
            $"Instance '{instance.InstanceId}' is not Suspended (status: {instance.Status}).");

    _stateMachine.ValidateTransition(
        WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running);

    var descriptor = _registry.GetByVersion(instance.Workflow.Id, instance.Workflow.Version);
    if (descriptor == null)
        throw new InvalidOperationException(
            $"Workflow '{instance.Workflow.Id}' version {instance.Workflow.Version} not found.");

    // Existing Phase 5 step recording (preserved verbatim)
    var currentStep = descriptor.Steps[instance.StepIndex];
    instance.StepResults.Add(new WorkflowStepResult
    {
        StepId = currentStep.Id,
        StepName = currentStep.Name,
        Status = StepExecutionStatus.Completed,
        Output = request.Result,
        ExecutedAt = DateTimeOffset.UtcNow
    });

    // Existing variable keys (preserved verbatim)
    instance.Variables["lastStepOutcome"] = request.Outcome;
    instance.Variables["lastStepResult"] = request.Result;
    instance.StepIndex++;
    instance.WaitingHumanTaskId = null;
    instance.Status = WorkflowInstanceStatus.Running;

    // Phase 5b: SaveAsync may throw RuntimeConcurrencyException — let it propagate
    await _store.SaveAsync(instance, ct).ConfigureAwait(false);

    // Existing lifecycle event (preserved verbatim)
    await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
    {
        EventType = "workflow.resumed",
        WorkflowInstanceId = instance.InstanceId,
        WorkflowId = descriptor.Id,
        Status = WorkflowInstanceStatus.Running
    }, ct).ConfigureAwait(false);

    await _executionRunner.RunAsync(instance, ct).ConfigureAwait(false);
}
```

**Key semantics preserved from Phase 5** (NOT changed):
- Variable keys: `"lastStepOutcome"`, `"lastStepResult"` (NOT "lastHumanTaskOutcome").
- Lifecycle event: `WorkflowLifecycleEvent` with `EventType = "workflow.resumed"` (NOT a new `WorkflowResumedEvent` type).
- Step recording: `WorkflowStepResult` added to `instance.StepResults` before save.

**Idempotency**: Second `HumanTaskCompletedEvent` for the same `HumanTaskInstanceId`:
1. `GetByWaitingHumanTaskId` returns `null` (workflow already resumed, `WaitingHumanTaskId` cleared).
2. `ContinueAsync` returns immediately — no-op. No exception.

**Concurrency guard**: If `SaveAsync` throws `RuntimeConcurrencyException`, do NOT run remaining steps, do NOT publish events. Let exception propagate.

---

### 6. `WorkflowContinuationRequest` — Legacy Field Name

Add comment and alias on `CrestCreates.Workflow.Abstractions/WorkflowContinuationRequest.cs`:

```csharp
public sealed class WorkflowContinuationRequest
{
    /// <summary>
    /// Legacy name. Since Phase 5, this value is <see cref="HumanTaskInstance.Id"/>,
    /// NOT <see cref="HumanTaskDescriptor.Id"/>. Do not rename to avoid cascading changes.
    /// </summary>
    public string HumanTaskId { get; init; } = string.Empty;

    /// <summary>
    /// Alias for <see cref="HumanTaskId"/>. Since Phase 5, this is HumanTaskInstance.Id.
    /// </summary>
    public string HumanTaskInstanceId => HumanTaskId;

    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
```

No force-rename. No call-site changes needed.

---

### 7. `GetByWaitingHumanTaskId` — Semantic Hardening

Already exists on `IWorkflowInstanceStore` (non-async suffix, keep as-is). Already has Suspended-only filter + `WorkflowCorrelationException` for multi-match. Phase 5b adds clone-on-return.

| Rule | Behavior |
|------|----------|
| Parameter | `humanTaskId` = `HumanTaskInstance.Id`, not `HumanTaskDescriptor.Id` |
| Status filter | Return ONLY `WorkflowInstanceStatus.Suspended` |
| No match | Return `null` (already current behavior) |
| Single match | Return clone of suspended instance |
| Multiple matches | Throw `WorkflowCorrelationException` (already current behavior) |
| Running/Completed/Failed | NOT returned even if `WaitingHumanTaskId` matches |
| Return value | Clone/snapshot, not internal reference (Phase 5b addition) |

---

### 8. Data Flow Changes (Phase 5 vs 5b)

```
Phase 5:
  CompleteAsync → SaveAsync(instance) → Publish(CompletedEvent)
  ContinueAsync → GetByWaitingHumanTaskId(id) → if null: throw → else: resume

Phase 5b:
  CompleteAsync → SaveAsync(instance) → [atomic CAS try] → Publish(CompletedEvent)
                                     ↘ [stamp mismatch]   → throw RuntimeConcurrencyException (no event)
  ContinueAsync → GetByWaitingHumanTaskId(id) → if null: return (no-op, was: throw)
                                             → if found: resume → SaveAsync → [atomic CAS try]
                                                                              ↘ [stamp mismatch] → throw
```

---

### 9. Test Plan

#### 9.1 `CrestCreates.Workflow.Tests` — New Tests

| # | Test | Assertions |
|---|------|-----------|
| 1 | `InMemoryWorkflowInstanceStore_Save_UpdatesConcurrencyStamp` | After first save: `ConcurrencyStamp` non-null, non-empty. After second save: `ConcurrencyStamp` changed. `UpdatedAt` set on both saves. |
| 2 | `InMemoryWorkflowInstanceStore_Save_Throws_On_StaleConcurrencyStamp` | Read instance once. Save copy1 (which modifies and saves). Save copy2 with original stamp → `RuntimeConcurrencyException`. |
| 3 | `InMemoryWorkflowInstanceStore_Save_Concurrent_Writes_Detect_Conflict` | Create one instance, save it (stamp A). Read back two independent clones from the store (both have stamp A). `Task.WhenAll` two saves with different payload mutations. Assert: exactly one save succeeds, the other throws `RuntimeConcurrencyException`. The persisted instance has exactly the state of the successful write (no merge, no lost update). |
| 4 | `InMemoryWorkflowInstanceStore_GetByWaitingHumanTaskId_Returns_SuspendedOnly` | Running/Completed/Failed instances with same WaitingHumanTaskId → not returned. Suspended → returned. |
| 5 | `InMemoryWorkflowInstanceStore_GetByWaitingHumanTaskId_Throws_When_MultipleSuspendedMatches` | Two Suspended instances share same WaitingHumanTaskId → `WorkflowCorrelationException`. |
| 6 | `WorkflowContinuation_DuplicateHumanTaskCompletedEvent_DoesNotDoubleAdvance` | First completion advances StepIndex. Second identical completion is no-op (returns, no exception). `StepResults` count unchanged. |

#### 9.2 `CrestCreates.HumanTask.Tests` — New Tests

| # | Test | Assertions |
|---|------|-----------|
| 7 | `InMemoryHumanTaskInstanceStore_Save_UpdatesConcurrencyStamp` | Same pattern as #1. |
| 8 | `InMemoryHumanTaskInstanceStore_Save_Throws_On_StaleConcurrencyStamp` | Same pattern as #2. |
| 9 | `InMemoryHumanTaskInstanceStore_GetPendingByAssignee_Returns_OpenOnly` | Only Created/Assigned returned. Completed/Cancelled excluded. Returned instances are clones (modifying clone doesn't affect store). |
| 10 | `InMemoryHumanTaskInstanceStore_GetPendingByWorkflow_Returns_OpenOnly` | Filter by WorkflowInstanceId. Only Created/Assigned. Completed/Cancelled excluded. |
| 11 | `HumanTaskRuntime_CompleteAsync_DoesNotPublishEvent_When_SaveConcurrencyFails` | Fake store throws `RuntimeConcurrencyException` on `SaveAsync`. Event bus NOT called. Exception propagated to caller. |
| 12 | `HumanTaskRuntime_CompleteAsync_Rejects_AlreadyCompleted` | Confirm existing Phase 5 test still passes after store changes (store now clones on read — assertion must account for this). |

#### 9.3 Regression

- `CrestCreates.HumanTask.Tests` — all 16 existing tests pass
- `CrestCreates.Workflow.Tests` — all 51 existing tests pass
- `CrestCreates.Capability.Tests` — no regressions
- `CrestCreates.Metadata.Tests` — no regressions
- `CrestCreates.Draft.Tests` — no regressions

---

### 10. File Manifest

| Project | Action | File |
|---------|--------|------|
| Metadata.Abstractions | **NEW** | `RuntimeStoreException.cs` |
| Metadata.Abstractions | **NEW** | `RuntimeConcurrencyException.cs` |
| Metadata.Abstractions | **NEW** | `RuntimeEntityNotFoundException.cs` |
| Metadata.Abstractions | **NEW** | `IHasConcurrencyStamp.cs` |
| Workflow.Abstractions | **MODIFY** | `WorkflowInstance.cs` (+ConcurrencyStamp, +UpdatedAt, +Clone(), :IHasConcurrencyStamp) |
| Workflow.Abstractions | **MODIFY** | `WorkflowContinuationRequest.cs` (+comment, +alias) |
| HumanTask.Abstractions | **MODIFY** | `HumanTaskInstance.cs` (+ConcurrencyStamp, +UpdatedAt, +Clone(), :IHasConcurrencyStamp) |
| HumanTask.Abstractions | **MODIFY** | `IHumanTaskInstanceStore.cs` (+GetPendingByWorkflowAsync) |
| Workflow | **MODIFY** | `InMemoryWorkflowInstanceStore.cs` (atomic CAS, clone on save/read) |
| Workflow | **MODIFY** | `WorkflowContinuationService.cs` (null→no-op, concurrency guard) |
| HumanTask | **MODIFY** | `InMemoryHumanTaskInstanceStore.cs` (atomic CAS, clone, +GetPendingByWorkflowAsync) |
| HumanTask | **MODIFY** | `DefaultHumanTaskRuntime.cs` (RuntimeEntityNotFoundException, concurrency guard) |
| Workflow.Tests | **NEW/MODIFY** | Store + continuation tests (6 tests) |
| HumanTask.Tests | **NEW/MODIFY** | Store + runtime tests (6 tests) |

---

### 11. Acceptance Criteria

```bash
dotnet build    # zero errors
dotnet test     # all tests pass
```

- `CrestCreates.HumanTask.Tests`: 16 existing + 6 new = 22 tests pass
- `CrestCreates.Workflow.Tests`: 51 existing + 6 new = 57 tests pass
- `CrestCreates.Capability.Tests`: no regressions
- `CrestCreates.Metadata.Tests`: no regressions
- `CrestCreates.Draft.Tests`: no regressions

---

### 12. Prohibited Items (Phase 5b)

- NO EF Core, SqlSugar, Dapper, Mongo, Redis
- NO file persistence, database migrations
- NO Outbox, UnitOfWork, distributed transactions, distributed locks
- NO Saga, Workflow Retry/Branch/Transition/Compensation/SubWorkflow
- NO HumanTask Claim/Delegate/Escalation/SLA/Timeout/Reminder
- NO Descriptor Topology Engine, Package/Snapshot, Dynamic API
- NO IServiceProvider in WorkflowExecutionContext
- NO Store as business state machine
- NO HumanTaskCompletedEvent with WorkflowInstanceId/WorkflowStepId
- NO JSON serialize/deserialize for clone
- NO reflection-based clone
- NO force-rename of `WorkflowContinuationRequest.HumanTaskId`
- NO generic Repository<T> abstraction
- NO DraftRecord / CapabilityExecutionRecord / DeadLetterMessage concurrency stamps
