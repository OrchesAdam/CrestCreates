# Phase 5 — HumanTask Runtime Foundation: Design Spec

**Date**: 2026-06-11
**Status**: Ready for Implementation
**Context**: Metadata Kernel v1.0, Phase 3, Phase 4, Phase 4a, Phase 4b, Phase 4c completed. Workflow runtime has suspend/resume closed loop. HumanTask module needs instance runtime.

---

## Goal

Implement HumanTask Runtime Foundation so that `HumanTaskStepExecutor` creates real `HumanTaskInstance` objects via `IHumanTaskRuntime`, and `IHumanTaskRuntime.CompleteAsync` publishes `HumanTaskCompletedEvent` to trigger the existing Workflow continuation loop.

## Architecture Principles

1. **Descriptor ≠ Instance**: `HumanTaskDescriptor` is pure metadata. `HumanTaskInstance` is runtime state.
2. **Domain event purity**: `HumanTaskCompletedEvent` carries no Workflow fields (`WorkflowInstanceId`, `WorkflowStepId`).
3. **Event-driven correlation**: Workflow correlates via `WorkflowInstance.WaitingHumanTaskId` = `HumanTaskInstance.Id`. No direct Workflow→HumanTask coupling.
4. **InMemory only**: No database persistence this phase. `ConcurrentDictionary`-backed store.
5. **No reflection, NativeAOT-friendly**.

## Scope Boundaries

### IN scope
- `HumanTaskInstance` model + status enum
- `HumanTaskCreationRequest`, `HumanTaskCompletionRequest` DTOs
- `IHumanTaskInstanceStore` (InMemory implementation)
- `IHumanTaskRuntime` with `CreateAsync`, `CompleteAsync`, `CancelAsync`
- `HumanTaskCompletedEvent` → add `HumanTaskInstanceId` + `HumanTaskVersion`
- `CompletionOutcomeMatcher` internal helper
- Modify `HumanTaskStepExecutor` to use `IHumanTaskRuntime`
- Modify `HumanTaskCompletedWorkflowSubscriber` to use `HumanTaskInstanceId`
- DI registration for HumanTask runtime services

### OUT of scope
- Database persistence (EF/FreeSql/SqlSugar/MongoDB)
- Claim, Delegate, Escalation, SLA, Timeout, Reminder
- Candidate users/groups, Form rendering, Dynamic API
- Workflow Branch/Transition/Retry/Compensation/SubWorkflow
- Public Workflow Resume API
- HumanTaskCreatedEvent, HumanTaskCancelledEvent (not existing, not added)

---

## Design

### 1. New Types in `CrestCreates.HumanTask.Abstractions`

#### 1.1 `HumanTaskInstance`

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskInstance
{
    public string Id { get; init; } = default!;
    public string HumanTaskId { get; init; } = default!;
    public int HumanTaskVersion { get; init; }
    public HumanTaskInstanceStatus Status { get; set; }
    public string? TenantId { get; init; }
    public string? AssigneeUserId { get; set; }
    public string? AssigneeRoleId { get; set; }
    public string? WorkflowInstanceId { get; init; }
    public string? WorkflowStepId { get; init; }
    public object? Input { get; init; }
    public object? Output { get; set; }
    public string? Outcome { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
}
```

- `Id` generated via `Guid.NewGuid().ToString("N")` — matches `WorkflowInstance.InstanceId` pattern.
- `WorkflowInstanceId`/`WorkflowStepId` are correlation fields on the instance, NOT on events.

#### 1.2 `HumanTaskInstanceStatus`

```csharp
public enum HumanTaskInstanceStatus
{
    Created,
    Assigned,
    Completed,
    Cancelled
}
```

No `Expired`, `Claimed`, `Delegated`, `Escalated`.

#### 1.3 `HumanTaskCreationRequest`

```csharp
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
}
```

#### 1.4 `HumanTaskCompletionRequest`

```csharp
public sealed class HumanTaskCompletionRequest
{
    public string HumanTaskInstanceId { get; init; } = default!;
    public string Outcome { get; init; } = default!;
    public object? Result { get; init; }
}
```

#### 1.5 `IHumanTaskInstanceStore`

```csharp
public interface IHumanTaskInstanceStore
{
    Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default);
    Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default);
}
```

- `SaveAsync` is upsert.
- `GetPendingByAssigneeAsync` returns `Created` or `Assigned` instances for the given `AssigneeUserId`.
- In normal Phase 5 creation flow, user-assigned tasks will be `Assigned`. `Created` status is included in the filter primarily to keep the store tolerant of externally constructed test instances where `AssigneeUserId` may be set but status remains `Created`.
- Store is pure persistence — no business validation.

#### 1.6 `IHumanTaskRuntime`

```csharp
public interface IHumanTaskRuntime
{
    Task<HumanTaskInstance> CreateAsync(HumanTaskCreationRequest request, CancellationToken ct = default);
    Task<HumanTaskInstance> CompleteAsync(HumanTaskCompletionRequest request, CancellationToken ct = default);
    Task<HumanTaskInstance> CancelAsync(string instanceId, string reason, CancellationToken ct = default);
}
```

#### 1.7 Modified: `HumanTaskCompletedEvent`

Add fields (keep existing `HumanTaskId`):

```csharp
public sealed class HumanTaskCompletedEvent : ILocalEvent
{
    public string HumanTaskId { get; init; } = string.Empty;       // descriptor ID (existing)
    public string HumanTaskInstanceId { get; init; } = string.Empty; // NEW
    public int HumanTaskVersion { get; init; }                      // NEW
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
```

---

### 2. Implementations in `CrestCreates.HumanTask`

#### 2.1 `InMemoryHumanTaskInstanceStore`

Pattern mirrors `InMemoryWorkflowInstanceStore`:
- `ConcurrentDictionary<string, HumanTaskInstance>` keyed by `instance.Id`.
- `SaveAsync`: direct upsert `_instances[instance.Id] = instance`.
- `GetByIdAsync`: `TryGetValue`.
- `GetPendingByAssigneeAsync`: filter `Status == Created || Status == Assigned` AND `AssigneeUserId == assigneeUserId`.
- No deep copy.

#### 2.2 `CompletionOutcomeMatcher` (internal static)

> **Descriptor property name**: Use the existing outcome collection property on `HumanTaskDescriptor`. The current code uses `Outcomes`. Do not rename the descriptor property in Phase 5. If the property is named `CompletionOutcomes` in the current codebase, use that name; otherwise use `Outcomes`.

```csharp
internal static class CompletionOutcomeMatcher
{
    public static bool Matches(CompletionOutcome outcome, string requestOutcome)
        => outcome.Condition.ToString().Equals(requestOutcome, StringComparison.OrdinalIgnoreCase);

    public static CompletionOutcome Resolve(HumanTaskDescriptor descriptor, string outcome)
    {
        var matches = descriptor.Outcomes  // use existing property name; do not rename
            .Where(o => Matches(o, outcome))
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"Outcome '{outcome}' not found in HumanTask '{descriptor.Id}' v{descriptor.Version}.");

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Multiple outcomes match '{outcome}' in HumanTask '{descriptor.Id}'. Identifier-based matching not yet supported.");

        var matched = matches[0];
        if (matched.Condition == CompletionCondition.CustomExpression)
            throw new NotSupportedException(
                "CustomExpression outcome evaluation is not supported in Phase 5.");

        return matched;
    }
}
```

#### 2.3 `DefaultHumanTaskRuntime`

Dependencies: `IHumanTaskRegistry`, `IHumanTaskInstanceStore`, `ILocalEventBus`.

- **CreateAsync**:
  1. Resolve descriptor: `request.Version != null` → `registry.GetByVersion()`, else `registry.GetById()` (returns latest active).
  2. Descriptor not found → throw `InvalidOperationException`.
  3. Create `HumanTaskInstance` with `Id = Guid.NewGuid().ToString("N")`, `HumanTaskVersion = descriptor.Version`, etc.
  4. Status: `Assigned` if `AssigneeUserId != null || AssigneeRoleId != null`, else `Created`.
  5. `CreatedAt = DateTimeOffset.UtcNow`.
  6. `SaveAsync` → return instance.
  7. No `HumanTaskCreatedEvent` published.

- **CompleteAsync**:
  1. Load instance via `store.GetByIdAsync(request.HumanTaskInstanceId)`.
  2. Not found → throw `InvalidOperationException`.
  3. Status not `Created` or `Assigned` → throw `InvalidOperationException`.
  4. Load descriptor via `registry.GetByVersion(instance.HumanTaskId, instance.HumanTaskVersion)`.
  5. Validate outcome via `CompletionOutcomeMatcher.Resolve(descriptor, request.Outcome)`.
  6. Set: `Status = Completed`, `Outcome = request.Outcome`, `Output = request.Result`, `CompletedAt = DateTimeOffset.UtcNow`.
  7. `SaveAsync`.
  8. Publish `HumanTaskCompletedEvent` with `HumanTaskInstanceId = instance.Id`, `HumanTaskId = instance.HumanTaskId`, `HumanTaskVersion = instance.HumanTaskVersion`, `Outcome = request.Outcome`, `Result = request.Result`.
  9. Return instance.

- **CancelAsync**:
  1. Load instance.
  2. Not found → throw `InvalidOperationException`.
  3. Status already `Completed` or `Cancelled` → throw `InvalidOperationException`.
  4. Set: `Status = Cancelled`, `CancellationReason = reason`, `CancelledAt = DateTimeOffset.UtcNow`.
  5. `SaveAsync` → return instance.
  6. No `CancelledEvent` published.

#### 2.4 DI Registration: `HumanTaskServiceCollectionExtensions`

```csharp
public static class HumanTaskServiceCollectionExtensions
{
    public static IServiceCollection AddHumanTaskRuntime(this IServiceCollection services)
    {
        services.TryAddSingleton<IHumanTaskInstanceStore, InMemoryHumanTaskInstanceStore>();
        services.TryAddSingleton<IHumanTaskRuntime, DefaultHumanTaskRuntime>();
        return services;
    }
}
```

> **Composition reminder**: Any test host or application host that registers Workflow runtime with `HumanTaskStepExecutor` (via `AddWorkflowEngine()`) must also call `AddHumanTaskRuntime()`. Otherwise DI will fail resolving `HumanTaskStepExecutor` because it now depends on `IHumanTaskRuntime`. If the project has a top-level `AddCrestCreatesRuntime()` or similar composition entry point, `AddHumanTaskRuntime()` must be included there.

#### 2.5 Project Reference

Add to `CrestCreates.HumanTask.csproj`:
```xml
<ProjectReference Include="..\CrestCreates.EventBus.Abstractions\CrestCreates.EventBus.Abstractions.csproj" />
```

> While `CrestCreates.HumanTask.Abstractions` already references `CrestCreates.EventBus.Abstractions` (needed for `ILocalEvent` on `HumanTaskCompletedEvent`), `CrestCreates.HumanTask` must explicitly reference it to resolve `ILocalEventBus` used by `DefaultHumanTaskRuntime`. Direct dependencies should be expressed explicitly. No reverse dependency (`HumanTask → Workflow`) is introduced.

---

### 3. Workflow Changes

#### 3.1 `HumanTaskStepExecutor`

> **Interface stability**: Keep the existing `IWorkflowStepExecutor.ExecuteAsync` signature. Do not change `IWorkflowStepExecutor` unless compilation requires a minimal adjustment. The current signature is `ExecuteAsync(WorkflowExecutionContext context, CancellationToken ct)` — access the step via `context.Step`.

Constructor injection of `IHumanTaskRuntime`:

```csharp
public sealed class HumanTaskStepExecutor : IWorkflowStepExecutor
{
    private readonly IHumanTaskRuntime _runtime;

    public HumanTaskStepExecutor(IHumanTaskRuntime runtime)
        => _runtime = runtime;

    public async Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        var target = (HumanTaskTarget)context.Step.Target;

        var instance = await _runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = target.HumanTask.Id,
            Version = target.HumanTask.Version,
            WorkflowInstanceId = context.Instance.InstanceId,
            WorkflowStepId = context.Step.Id,
            Input = context.Instance.Variables
        }, ct);

        return new StepExecutionResult(
            StepExecutionStatus.Suspended,
            WaitingHumanTaskId: instance.Id);
    }
}
```

#### 3.2 `HumanTaskCompletedWorkflowSubscriber`

Change `evt.HumanTaskId` → `evt.HumanTaskInstanceId`:

```csharp
public Task HandleAsync(HumanTaskCompletedEvent evt, CancellationToken ct)
{
    return _continuationService.ContinueAsync(
        new WorkflowContinuationRequest
        {
            HumanTaskId = evt.HumanTaskInstanceId,
            Outcome = evt.Outcome,
            Result = evt.Result
        }, ct);
}
```

Note: **`WorkflowContinuationRequest.HumanTaskId` is a legacy field name.** In Phase 5, the value assigned to it MUST be `HumanTaskCompletedEvent.HumanTaskInstanceId`, NOT `HumanTaskCompletedEvent.HumanTaskId`. No caller may pass `HumanTaskDescriptor.Id` into `WorkflowContinuationRequest.HumanTaskId`. The field name may be renamed to `HumanTaskInstanceId` in a future phase to eliminate ambiguity; do not rename it in Phase 5 to avoid cascading changes.

#### 3.3 No Other Workflow Changes

- `DefaultStepExecutorRegistry` already receives `HumanTaskStepExecutor` via DI → no change needed.
- `WorkflowServiceCollectionExtensions` already registers `HumanTaskStepExecutor` as Singleton → DI resolves `IHumanTaskRuntime` automatically.
- `IWorkflowInstanceStore.GetByWaitingHumanTaskId(string humanTaskId)` works as-is since `WaitingHumanTaskId` now stores the instance GUID.

---

### 4. Data Flow (End-to-End)

```
WorkflowEngine.ExecuteAsync("wf_01")
  → WorkflowExecutionRunner.RunAsync(instance)
    → ExecuteStepsAsync
      → step 0: CapabilityStepExecutor → Completed, stepIndex++
      → step 1: HumanTaskStepExecutor.ExecuteAsync(ctx)
          → IHumanTaskRuntime.CreateAsync(request)
              → IHumanTaskRegistry.GetById("ht_01") → descriptor v1
              → new HumanTaskInstance { Id = "abc123...", Status = Created, ... }
              → IHumanTaskInstanceStore.SaveAsync(instance) ✓
          ← StepExecutionResult(Suspended, WaitingHumanTaskId: "abc123...")
      → runner: instance.WaitingHumanTaskId = "abc123..."
      → runner: instance.Status = Suspended
      → runner: IWorkflowInstanceStore.SaveAsync(instance) ✓
      → runner: publish "workflow.suspended" event ✓
  ← instance.Status = Suspended

--- External actor completes the task ---

IHumanTaskRuntime.CompleteAsync(new HumanTaskCompletionRequest
    { HumanTaskInstanceId = "abc123...", Outcome = "Approve", Result = ... })
  → IHumanTaskInstanceStore.GetByIdAsync("abc123...") → instance
  → validate status (Created) ✓
  → IHumanTaskRegistry.GetByVersion("ht_01", 1) → descriptor
  → CompletionOutcomeMatcher.Resolve(descriptor, "Approve") → CompletionOutcome { Condition = Approve }
  → instance.Status = Completed, Outcome = "Approve", output = result
  → IHumanTaskInstanceStore.SaveAsync(instance) ✓
  → ILocalEventBus.PublishAsync(new HumanTaskCompletedEvent
      { HumanTaskInstanceId = "abc123...", HumanTaskId = "ht_01",
        HumanTaskVersion = 1, Outcome = "Approve", Result = ... })
      → dispatches to HumanTaskCompletedWorkflowSubscriber
          → WorkflowContinuationService.ContinueAsync(new WorkflowContinuationRequest
              { HumanTaskId = "abc123..." })
              → IWorkflowInstanceStore.GetByWaitingHumanTaskId("abc123...") → workflow instance
              → validate Suspended ✓
              → stepIndex++, WaitingHumanTaskId = null, Status = Running
              → IWorkflowInstanceStore.SaveAsync ✓
              → publish "workflow.resumed" ✓
              → IWorkflowExecutionRunner.RunAsync(instance) → continues to step 2..
  ← completed instance
```

---

### 5. Test Plan

#### 5.1 `CrestCreates.HumanTask.Tests` — New: `HumanTaskRuntimeTests.cs`

| # | Test | Assertions |
|---|------|-----------|
| 1 | `CreateAsync_Creates_Instance_From_Descriptor` | Instance.Id non-null, HumanTaskId correct, Version pinned, Status Created, Input correct, store contains instance |
| 2 | `CreateAsync_Throws_When_Descriptor_Not_Found` | Throws InvalidOperationException |
| 3 | `CompleteAsync_Completes_Instance_And_Publishes_Event` | Status=Completed, Outcome correct, Output correct, CompletedAt non-null, event published with correct fields |
| 4 | `CompleteAsync_Throws_When_Outcome_Invalid` | Throws InvalidOperationException for non-matching outcome |
| 5 | `CompleteAsync_Throws_When_Instance_Already_Completed` | Throws InvalidOperationException |
| 6 | `CancelAsync_Cancels_Instance` | Status=Cancelled, CancellationReason set, CancelledAt set |
| 7 | `GetPendingByAssigneeAsync_Returns_Only_Open_Tasks` | Only Created/Assigned; excludes Completed/Cancelled |

**Test infrastructure**: Mock `IHumanTaskRegistry`, `ILocalEventBus` (via Moq). Real `InMemoryHumanTaskInstanceStore`.

#### 5.2 `CrestCreates.Workflow.Tests` — New/Modified

| # | Test | Assertions |
|---|------|-----------|
| 8 | `HumanTaskStepExecutor_Creates_Instance_And_Returns_Suspended` | `CreateAsync` called once; `request.HumanTaskId` correct; `request.WorkflowInstanceId` correct; `request.WorkflowStepId` correct; `result.Status` = Suspended; `result.WaitingHumanTaskId` = returnedInstance.Id |
| 9 | `Workflow_HumanTask_EndToEnd_Complete_Task_Resumes_Workflow` | Full flow: start→suspend→complete→resumed→completed |

**Test infrastructure for #8** (executor unit test): Mock `IHumanTaskRuntime` returning a pre-built `HumanTaskInstance { Id = "inst-001" }`. Assert the mock was called with correct request fields. Assert the returned `StepExecutionResult`. **Do not assert InMemoryHumanTaskInstanceStore contents** — the mocked runtime does not write to the store.

**Test infrastructure for #9** (integration test): All real implementations wired together. HumanTaskRegistry built with actual descriptors. Real `DefaultHumanTaskRuntime` + `InMemoryHumanTaskInstanceStore`. Use a synchronous/dispatched local event bus (or directly invoke the subscriber). Assert workflow reaches Completed state. **Store persistence is covered by `DefaultHumanTaskRuntime` tests in `CrestCreates.HumanTask.Tests`.**

**Project reference**: `CrestCreates.Workflow.Tests.csproj` must add reference to `CrestCreates.HumanTask` (runtime module).

`CrestCreates.HumanTask.Tests.csproj` must add `Moq` package reference.

---

### 6. Acceptance Criteria

```bash
dotnet build    # zero errors
dotnet test     # all HumanTask.Tests + Workflow.Tests pass
```

- `CrestCreates.HumanTask.Tests`: all 7 new tests + 2 existing pass
- `CrestCreates.Workflow.Tests`: 2 new tests + all 8 existing pass (total 10)
- `CrestCreates.Capability.Tests` not broken
- `CrestCreates.Metadata.Tests` not broken

---

### 7. File Manifest

| Project | Action | File |
|---------|--------|------|
| HumanTask.Abstractions | **NEW** | `HumanTaskInstance.cs` |
| HumanTask.Abstractions | **NEW** | `HumanTaskInstanceStatus.cs` |
| HumanTask.Abstractions | **NEW** | `HumanTaskCreationRequest.cs` |
| HumanTask.Abstractions | **NEW** | `HumanTaskCompletionRequest.cs` |
| HumanTask.Abstractions | **NEW** | `IHumanTaskInstanceStore.cs` |
| HumanTask.Abstractions | **NEW** | `IHumanTaskRuntime.cs` |
| HumanTask.Abstractions | **MODIFY** | `HumanTaskCompletedEvent.cs` (+2 fields) |
| HumanTask | **NEW** | `InMemoryHumanTaskInstanceStore.cs` |
| HumanTask | **NEW** | `CompletionOutcomeMatcher.cs` |
| HumanTask | **NEW** | `DefaultHumanTaskRuntime.cs` |
| HumanTask | **NEW** | `HumanTaskServiceCollectionExtensions.cs` |
| HumanTask | **MODIFY** | `CrestCreates.HumanTask.csproj` (+EventBus ref) |
| Workflow | **MODIFY** | `HumanTaskStepExecutor.cs` (+IHumanTaskRuntime DI) |
| Workflow | **MODIFY** | `HumanTaskCompletedWorkflowSubscriber.cs` (HumanTaskId→HumanTaskInstanceId) |
| HumanTask.Tests | **NEW** | `HumanTaskRuntimeTests.cs` |
| HumanTask.Tests | **NEW** | `InMemoryHumanTaskInstanceStoreTests.cs` |
| HumanTask.Tests | **MODIFY** | `CrestCreates.HumanTask.Tests.csproj` (+Moq) |
| Workflow.Tests | **MODIFY** | `WorkflowContinuationTests.cs` (+2 tests) |
| Workflow.Tests | **MODIFY** | `CrestCreates.Workflow.Tests.csproj` (+HumanTask runtime ref) |
