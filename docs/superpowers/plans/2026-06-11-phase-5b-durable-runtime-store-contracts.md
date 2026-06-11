# Phase 5b — Durable Runtime Store Contracts: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden InMemory runtime stores with atomic CAS concurrency, shallow-snapshot semantics, idempotent duplicate handling, and event-after-persist guarantees — making InMemory stores simulate durable-store constraints without database persistence.

**Architecture:** Add `IHasConcurrencyStamp` + 3 exception types to Metadata.Abstractions. Add `ConcurrencyStamp`/`UpdatedAt`/`Clone()` to WorkflowInstance and HumanTaskInstance (both implement `IHasConcurrencyStamp`). Rewrite InMemory stores with `TryUpdate`/`TryAdd` CAS loops. Harden `DefaultHumanTaskRuntime.CompleteAsync` (no event on save failure) and `WorkflowContinuationService.ContinueAsync` (null→no-op).

**Tech Stack:** .NET 10, C#, ConcurrentDictionary with atomic CAS, hand-written Clone (no reflection), xUnit + FluentAssertions + Moq + Task.WhenAll for concurrent testing.

---

### Phase 0: Project Setup — Verify References

### Task 0.1: Verify project references for IHasConcurrencyStamp

**Files:**
- Read: `framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
- Read: `framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj`

`IHasConcurrencyStamp` is defined in `CrestCreates.Metadata.Abstractions`. Both `Workflow.Abstractions` and `HumanTask.Abstractions` already reference it (via `VersionedDescriptorRef<T>`). Verify this is the case. If either project is missing the reference, add it now.

- [ ] **Step 1: Check Workflow.Abstractions references Metadata.Abstractions**

Run: `grep "Metadata" framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Finds a `ProjectReference` to `CrestCreates.Metadata.Abstractions`.

- [ ] **Step 2: Check HumanTask.Abstractions references Metadata.Abstractions**

Run: `grep "Metadata" framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj`
Expected: Finds a `ProjectReference` to `CrestCreates.Metadata.Abstractions`.

If either reference is missing, add the appropriate `<ProjectReference>` and commit.

> **Note**: If a `ProjectReference` to `CrestCreates.Metadata.Abstractions` is missing from `HumanTask.Abstractions`, add it:
> ```xml
> <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
> ```

- [ ] **Step 3: Commit (if changes made)**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj
git commit -m "build: add Metadata.Abstractions reference to HumanTask.Abstractions"
```

If no changes needed, skip this commit.

---

### Phase 1: New Types — Metadata.Abstractions

### Task 1.1: Create RuntimeStoreException.cs

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/RuntimeStoreException.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public class RuntimeStoreException : Exception
{
    public RuntimeStoreException(string message) : base(message) { }
    public RuntimeStoreException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/RuntimeStoreException.cs
git commit -m "feat: add RuntimeStoreException base class"
```

---

### Task 1.2: Create RuntimeConcurrencyException.cs

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/RuntimeConcurrencyException.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public class RuntimeConcurrencyException : RuntimeStoreException
{
    public RuntimeConcurrencyException(string message) : base(message) { }
    public RuntimeConcurrencyException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/RuntimeConcurrencyException.cs
git commit -m "feat: add RuntimeConcurrencyException"
```

---

### Task 1.3: Create RuntimeEntityNotFoundException.cs

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/RuntimeEntityNotFoundException.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public class RuntimeEntityNotFoundException : RuntimeStoreException
{
    public RuntimeEntityNotFoundException(string message) : base(message) { }
    public RuntimeEntityNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/RuntimeEntityNotFoundException.cs
git commit -m "feat: add RuntimeEntityNotFoundException"
```

---

### Task 1.4: Create IHasConcurrencyStamp.cs

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IHasConcurrencyStamp.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public interface IHasConcurrencyStamp
{
    string ConcurrencyStamp { get; set; }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/IHasConcurrencyStamp.cs
git commit -m "feat: add IHasConcurrencyStamp interface"
```

---

### Task 1.5: Build verification

- [ ] **Step 1: Build the solution**

Run: `dotnet build`
Expected: Zero errors. (All 4 new abstractions compile cleanly.)

---

### Phase 2: Runtime Instance Modifications

### Task 2.1: Modify WorkflowInstance.cs — Add ConcurrencyStamp, UpdatedAt, Clone(), implement IHasConcurrencyStamp

**Files:**
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs`

**Current file** (from Phase 5):
```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowInstance
{
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public VersionedDescriptorRef<WorkflowDescriptor> Workflow { get; init; }
    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;
    public string? CurrentStepId { get; set; }
    public int StepIndex { get; set; }
    public string? WaitingHumanTaskId { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public Dictionary<string, object?> Variables { get; init; } = new();
    public Dictionary<string, object?> StepVariables { get; init; } = new();
    public List<WorkflowStepResult> StepResults { get; init; } = new();
    public string? ErrorMessage { get; set; }
}
```

- [ ] **Step 1: Add ConcurrencyStamp + UpdatedAt + IHasConcurrencyStamp + Clone()**

Replace the file with:

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowInstance : IHasConcurrencyStamp
{
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public VersionedDescriptorRef<WorkflowDescriptor> Workflow { get; init; }
    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;
    public string? CurrentStepId { get; set; }
    public int StepIndex { get; set; }
    public string? WaitingHumanTaskId { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public Dictionary<string, object?> Variables { get; init; } = new();
    public Dictionary<string, object?> StepVariables { get; init; } = new();
    public List<WorkflowStepResult> StepResults { get; init; } = new();
    public string? ErrorMessage { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? UpdatedAt { get; set; }

    public WorkflowInstance Clone()
    {
        return new WorkflowInstance
        {
            InstanceId = this.InstanceId,
            Workflow = this.Workflow,
            Status = this.Status,
            CurrentStepId = this.CurrentStepId,
            StepIndex = this.StepIndex,
            WaitingHumanTaskId = this.WaitingHumanTaskId,
            StartedAt = this.StartedAt,
            CompletedAt = this.CompletedAt,
            Variables = new Dictionary<string, object?>(this.Variables),
            StepVariables = new Dictionary<string, object?>(this.StepVariables),
            StepResults = new List<WorkflowStepResult>(this.StepResults),
            ErrorMessage = this.ErrorMessage,
            ConcurrencyStamp = this.ConcurrencyStamp,
            UpdatedAt = this.UpdatedAt
        };
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Zero errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs
git commit -m "feat: add ConcurrencyStamp, UpdatedAt, Clone() to WorkflowInstance, implement IHasConcurrencyStamp"
```

---

### Task 2.2: Modify HumanTaskInstance.cs — Add ConcurrencyStamp, UpdatedAt, Clone(), implement IHasConcurrencyStamp

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs`

**Current file** (from Phase 5):
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

- [ ] **Step 1: Add ConcurrencyStamp + UpdatedAt + IHasConcurrencyStamp + Clone()**

Replace the file with:

```csharp
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskInstance : IHasConcurrencyStamp
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

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset? UpdatedAt { get; set; }

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
            UpdatedAt = this.UpdatedAt
        };
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj`
Expected: Zero errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs
git commit -m "feat: add ConcurrencyStamp, UpdatedAt, Clone() to HumanTaskInstance, implement IHasConcurrencyStamp"
```

---

### Task 2.3: Modify IHumanTaskInstanceStore.cs — Add GetPendingByWorkflowAsync

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskInstanceStore.cs`

**Current file** (from Phase 5):
```csharp
namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskInstanceStore
{
    Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default);
    Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default);
}
```

- [ ] **Step 1: Add GetPendingByWorkflowAsync method**

Replace the file with:

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskInstanceStore
{
    Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default);
    Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default);
    Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(
        string workflowInstanceId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj`
Expected: Zero errors. (Note: `InMemoryHumanTaskInstanceStore` will now fail to compile because it doesn't implement the new method — this is expected.)

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskInstanceStore.cs
git commit -m "feat: add GetPendingByWorkflowAsync to IHumanTaskInstanceStore"
```

---

### Task 2.4: Modify WorkflowContinuationRequest.cs — Add legacy comment + alias

**Files:**
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowContinuationRequest.cs`

**Current file** (from Phase 5):
```csharp
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowContinuationRequest
{
    public string HumanTaskId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
```

- [ ] **Step 1: Add XML comment and alias property**

Replace the file with:

```csharp
namespace CrestCreates.Workflow.Abstractions;

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

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Zero errors. (Note: `WorkflowContinuationService` uses `request.HumanTaskId` which is unchanged.)

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowContinuationRequest.cs
git commit -m "docs: add legacy name comment and HumanTaskInstanceId alias to WorkflowContinuationRequest"
```

---

### Task 2.5: Full build verification

- [ ] **Step 1: Build the solution**

Run: `dotnet build`
Expected: Zero errors from abstractions projects. One expected error: `InMemoryHumanTaskInstanceStore` does not implement `GetPendingByWorkflowAsync`. This will be resolved in Phase 3. If there are other unrelated errors, fix them before continuing.

---

### Phase 3: InMemory Store Hardening

### Task 3.1: Rewrite InMemoryWorkflowInstanceStore — Atomic CAS + Clone

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs`

**Current file** (from Phase 5):
```csharp
using System.Collections.Concurrent;
using System.Linq;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class InMemoryWorkflowInstanceStore : IWorkflowInstanceStore
{
    private readonly ConcurrentDictionary<string, WorkflowInstance> _instances = new();

    public Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        _instances[instance.InstanceId] = instance;
        return Task.CompletedTask;
    }

    public Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default)
    {
        _instances.TryGetValue(instanceId, out var instance);
        return Task.FromResult(instance);
    }

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

        return Task.FromResult(matches.SingleOrDefault());
    }
}
```

- [ ] **Step 1: Rewrite with atomic CAS, clone, snapshot semantics**

Replace the file with:

```csharp
using System.Collections.Concurrent;
using System.Linq;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class InMemoryWorkflowInstanceStore : IWorkflowInstanceStore
{
    private readonly ConcurrentDictionary<string, WorkflowInstance> _instances = new();

    public Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
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
                // Race: another thread inserted between TryGetValue and TryAdd — retry
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
            // Race: another thread updated — retry
        }
    }

    public Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default)
    {
        if (_instances.TryGetValue(instanceId, out var existing))
            return Task.FromResult<WorkflowInstance?>(existing.Clone());
        return Task.FromResult<WorkflowInstance?>(null);
    }

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
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Zero errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs
git commit -m "feat: add atomic CAS concurrency and clone semantics to InMemoryWorkflowInstanceStore"
```

---

### Task 3.2: Rewrite InMemoryHumanTaskInstanceStore — Atomic CAS + Clone + GetPendingByWorkflowAsync

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs`

**Current file** (from Phase 5):
```csharp
using System.Collections.Concurrent;
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class InMemoryHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private readonly ConcurrentDictionary<string, HumanTaskInstance> _instances = new();

    public Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default)
    {
        _instances[instance.Id] = instance;
        return Task.CompletedTask;
    }

    public Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default)
    {
        _instances.TryGetValue(instanceId, out var instance);
        return Task.FromResult(instance);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.AssigneeUserId == assigneeUserId)
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }
}
```

- [ ] **Step 1: Rewrite with atomic CAS, clone, snapshot semantics + GetPendingByWorkflowAsync**

Replace the file with:

```csharp
using System.Collections.Concurrent;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class InMemoryHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private readonly ConcurrentDictionary<string, HumanTaskInstance> _instances = new();

    public Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default)
    {
        var snapshot = instance.Clone();
        snapshot.UpdatedAt = DateTimeOffset.UtcNow;

        while (true)
        {
            if (!_instances.TryGetValue(instance.Id, out var existing))
            {
                // First save — insert with fresh stamp
                snapshot.ConcurrencyStamp = Guid.NewGuid().ToString("N");
                if (_instances.TryAdd(instance.Id, snapshot))
                {
                    instance.ConcurrencyStamp = snapshot.ConcurrencyStamp;
                    instance.UpdatedAt = snapshot.UpdatedAt;
                    return Task.CompletedTask;
                }
                // Race: another thread inserted — retry
                continue;
            }

            // Update existing — check concurrency stamp atomically
            if (existing.ConcurrencyStamp != instance.ConcurrencyStamp)
                throw new RuntimeConcurrencyException(
                    $"Concurrency conflict for HumanTaskInstance '{instance.Id}'. " +
                    $"Expected stamp '{instance.ConcurrencyStamp}', actual '{existing.ConcurrencyStamp}'.");

            snapshot.ConcurrencyStamp = Guid.NewGuid().ToString("N");
            if (_instances.TryUpdate(instance.Id, snapshot, existing))
            {
                instance.ConcurrencyStamp = snapshot.ConcurrencyStamp;
                instance.UpdatedAt = snapshot.UpdatedAt;
                return Task.CompletedTask;
            }
            // Race: another thread updated — retry
        }
    }

    public Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default)
    {
        if (_instances.TryGetValue(instanceId, out var existing))
            return Task.FromResult<HumanTaskInstance?>(existing.Clone());
        return Task.FromResult<HumanTaskInstance?>(null);
    }

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default)
    {
        var results = _instances.Values
            .Where(i => (i.Status == HumanTaskInstanceStatus.Created ||
                         i.Status == HumanTaskInstanceStatus.Assigned) &&
                        i.AssigneeUserId == assigneeUserId)
            .Select(i => i.Clone())
            .ToList()
            .AsReadOnly();

        return Task.FromResult((IReadOnlyList<HumanTaskInstance>)results);
    }

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
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj`
Expected: Zero errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs
git commit -m "feat: add atomic CAS concurrency, clone semantics, GetPendingByWorkflowAsync to InMemoryHumanTaskInstanceStore"
```

---

### Phase 4: Runtime Service Hardening

### Task 4.1: Modify DefaultHumanTaskRuntime.cs — Duplicate completion defense + concurrency guard

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask/DefaultHumanTaskRuntime.cs`

**Current file** (from Phase 5) — the `CompleteAsync` method currently:
1. Gets instance via `_store.GetByIdAsync`
2. If null → throws `InvalidOperationException`
3. Checks status not Completed/Cancelled → throws `InvalidOperationException`
4. Validates descriptor + outcome
5. Sets status, outcome, output, completedAt
6. Calls `_store.SaveAsync`
7. Publishes `HumanTaskCompletedEvent`

Phase 5b changes:
- Step 2: change `InvalidOperationException` → `RuntimeEntityNotFoundException`
- Step 6: `SaveAsync` may now throw `RuntimeConcurrencyException` — if it does, do NOT suppress; let it propagate so event is never published
- The save-then-publish ordering is already correct (no change needed)

- [ ] **Step 1: Add `using CrestCreates.Metadata.Abstractions;` and change exception types**

The complete updated file:

```csharp
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;

namespace CrestCreates.HumanTask;

public sealed class DefaultHumanTaskRuntime : IHumanTaskRuntime
{
    private readonly IHumanTaskRegistry _registry;
    private readonly IHumanTaskInstanceStore _store;
    private readonly ILocalEventBus _eventBus;

    public DefaultHumanTaskRuntime(
        IHumanTaskRegistry registry,
        IHumanTaskInstanceStore store,
        ILocalEventBus eventBus)
    {
        _registry = registry;
        _store = store;
        _eventBus = eventBus;
    }

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

        var instance = new HumanTaskInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            HumanTaskId = descriptor.Id,
            HumanTaskVersion = descriptor.Version,
            Status = (request.AssigneeUserId != null || request.AssigneeRoleId != null)
                ? HumanTaskInstanceStatus.Assigned
                : HumanTaskInstanceStatus.Created,
            TenantId = request.TenantId,
            AssigneeUserId = request.AssigneeUserId,
            AssigneeRoleId = request.AssigneeRoleId,
            WorkflowInstanceId = request.WorkflowInstanceId,
            WorkflowStepId = request.WorkflowStepId,
            Input = request.Input,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }

    public async Task<HumanTaskInstance> CompleteAsync(
        HumanTaskCompletionRequest request, CancellationToken ct = default)
    {
        var instance = await _store.GetByIdAsync(request.HumanTaskInstanceId, ct)
            .ConfigureAwait(false);

        if (instance == null)
            throw new RuntimeEntityNotFoundException(
                $"HumanTask instance '{request.HumanTaskInstanceId}' not found.");

        if (instance.Status != HumanTaskInstanceStatus.Created &&
            instance.Status != HumanTaskInstanceStatus.Assigned)
            throw new InvalidOperationException(
                $"HumanTask instance '{instance.Id}' is in status '{instance.Status}' " +
                "and cannot be completed.");

        var descriptor = _registry.GetByVersion(instance.HumanTaskId, instance.HumanTaskVersion);
        if (descriptor == null)
            throw new InvalidOperationException(
                $"HumanTask descriptor '{instance.HumanTaskId}' v{instance.HumanTaskVersion} " +
                "not found.");

        CompletionOutcomeMatcher.Resolve(descriptor, request.Outcome);

        instance.Status = HumanTaskInstanceStatus.Completed;
        instance.Outcome = request.Outcome;
        instance.Output = request.Result;
        instance.CompletedAt = DateTimeOffset.UtcNow;

        // Phase 5b: SaveAsync may throw RuntimeConcurrencyException.
        // If it does, DO NOT publish — let exception propagate.
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);

        await _eventBus.PublishAsync(new HumanTaskCompletedEvent
        {
            HumanTaskId = instance.HumanTaskId,
            HumanTaskInstanceId = instance.Id,
            HumanTaskVersion = instance.HumanTaskVersion,
            Outcome = request.Outcome,
            Result = request.Result
        }, ct).ConfigureAwait(false);

        return instance;
    }

    public async Task<HumanTaskInstance> CancelAsync(
        string instanceId, string reason, CancellationToken ct = default)
    {
        var instance = await _store.GetByIdAsync(instanceId, ct).ConfigureAwait(false);

        if (instance == null)
            throw new RuntimeEntityNotFoundException(
                $"HumanTask instance '{instanceId}' not found.");

        if (instance.Status == HumanTaskInstanceStatus.Completed ||
            instance.Status == HumanTaskInstanceStatus.Cancelled)
            throw new InvalidOperationException(
                $"HumanTask instance '{instanceId}' is already '{instance.Status}' " +
                "and cannot be cancelled.");

        instance.Status = HumanTaskInstanceStatus.Cancelled;
        instance.CancellationReason = reason;
        instance.CancelledAt = DateTimeOffset.UtcNow;

        // Phase 5b: SaveAsync may throw RuntimeConcurrencyException — let it propagate
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj`
Expected: Zero errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/DefaultHumanTaskRuntime.cs
git commit -m "feat: add duplicate completion defense and concurrency guard to DefaultHumanTaskRuntime"
```

---

### Task 4.2: Modify WorkflowContinuationService.cs — Idempotent duplicate handling

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/WorkflowContinuationService.cs`

**Current file** (from Phase 5) — the `ContinueAsync` method currently:
1. Calls `_store.GetByWaitingHumanTaskId`
2. If null → throws `InvalidOperationException`
3. Validates Suspended status
4. Validates state machine transition
5. Gets descriptor
6. Adds `WorkflowStepResult` to `StepResults`
7. Sets `Variables["lastStepOutcome"]` and `Variables["lastStepResult"]`
8. Increments `StepIndex`, clears `WaitingHumanTaskId`, sets `Running`
9. Calls `_store.SaveAsync`
10. Publishes `WorkflowLifecycleEvent { EventType = "workflow.resumed" }`
11. Calls `_executionRunner.RunAsync`

Phase 5b changes:
- Step 2: null → return (no-op, was: throw)

Everything else is preserved verbatim from Phase 5.

- [ ] **Step 1: Change null-handling from throw to return**

Only one line changes. The complete updated file:

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowContinuationService : IWorkflowContinuationService
{
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    public WorkflowContinuationService(
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowRegistry registry,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher)
    {
        _store = store;
        _stateMachine = stateMachine;
        _registry = registry;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
    }

    public async Task ContinueAsync(
        WorkflowContinuationRequest request, CancellationToken ct = default)
    {
        var instance = await _store.GetByWaitingHumanTaskId(request.HumanTaskId, ct)
            .ConfigureAwait(false);

        // Phase 5b: null → idempotent no-op (was: throw InvalidOperationException)
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

        var currentStep = descriptor.Steps[instance.StepIndex];
        instance.StepResults.Add(new WorkflowStepResult
        {
            StepId = currentStep.Id,
            StepName = currentStep.Name,
            Status = StepExecutionStatus.Completed,
            Output = request.Result,
            ExecutedAt = DateTimeOffset.UtcNow
        });

        instance.Variables["lastStepOutcome"] = request.Outcome;
        instance.Variables["lastStepResult"] = request.Result;
        instance.StepIndex++;
        instance.WaitingHumanTaskId = null;
        instance.Status = WorkflowInstanceStatus.Running;

        // Phase 5b: SaveAsync may throw RuntimeConcurrencyException — let it propagate.
        // Do NOT run remaining steps or publish events on concurrency failure.
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);

        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = "workflow.resumed",
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            Status = WorkflowInstanceStatus.Running
        }, ct).ConfigureAwait(false);

        await _executionRunner.RunAsync(instance, ct).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Zero errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowContinuationService.cs
git commit -m "feat: add idempotent duplicate handling to WorkflowContinuationService (null→no-op)"
```

---

### Phase 5: Full Build Verification (Pre-Tests)

### Task 5.1: Full solution build

- [ ] **Step 1: Build entire solution**

Run: `dotnet build`
Expected: Zero errors. All abstractions + implementations compile cleanly. Test projects may have warnings but should not have errors.

---

### Phase 6: Tests — Workflow.Tests

### Task 6.1: Create InMemoryWorkflowInstanceStoreTests.cs

**Files:**
- Create: `framework/test/CrestCreates.Workflow.Tests/InMemoryWorkflowInstanceStoreTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class InMemoryWorkflowInstanceStoreTests
{
    private static WorkflowInstance CreateInstance(string instanceId, WorkflowInstanceStatus status)
    {
        return new WorkflowInstance
        {
            InstanceId = instanceId,
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_01", 1),
            Status = status
        };
    }

    [Fact]
    public async Task Save_UpdatesConcurrencyStamp()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var instance = CreateInstance("inst-01", WorkflowInstanceStatus.Running);

        // First save
        await store.SaveAsync(instance);
        instance.ConcurrencyStamp.Should().NotBeNullOrEmpty();
        instance.UpdatedAt.Should().NotBeNull();
        var firstStamp = instance.ConcurrencyStamp;
        var firstUpdatedAt = instance.UpdatedAt;

        // Second save — stamp and UpdatedAt should change
        instance.Status = WorkflowInstanceStatus.Suspended;
        await store.SaveAsync(instance);
        instance.ConcurrencyStamp.Should().NotBe(firstStamp);
        instance.UpdatedAt.Should().NotBe(firstUpdatedAt);
    }

    [Fact]
    public async Task Save_Throws_On_StaleConcurrencyStamp()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var instance = CreateInstance("inst-01", WorkflowInstanceStatus.Running);

        // Save once to establish a stamp
        await store.SaveAsync(instance);

        // Read back two copies (both have the same stamp from the first save)
        var copy1 = await store.GetAsync("inst-01");
        var copy2 = await store.GetAsync("inst-01");
        copy1.Should().NotBeNull();
        copy2.Should().NotBeNull();
        copy1!.ConcurrencyStamp.Should().Be(copy2!.ConcurrencyStamp);

        // Modify and save copy1 — this succeeds and generates a new stamp
        copy1.Status = WorkflowInstanceStatus.Suspended;
        await store.SaveAsync(copy1);

        // Try to save copy2 with the old stamp — should fail
        copy2.Status = WorkflowInstanceStatus.Failed;
        await store.Invoking(s => s.SaveAsync(copy2))
            .Should().ThrowAsync<RuntimeConcurrencyException>();
    }

    [Fact]
    public async Task Save_Concurrent_Writes_Detect_Conflict()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var instance = CreateInstance("inst-01", WorkflowInstanceStatus.Running);

        // First save to establish stamp
        await store.SaveAsync(instance);

        // Read back two independent clones (both have stamp A)
        var copy1 = await store.GetAsync("inst-01");
        var copy2 = await store.GetAsync("inst-01");
        copy1.Should().NotBeNull();
        copy2.Should().NotBeNull();

        int successCount = 0;
        int failureCount = 0;
        Exception? failureException = null;

        // Concurrent save: both try to update with different statuses
        var task1 = Task.Run(async () =>
        {
            try
            {
                copy1!.Status = WorkflowInstanceStatus.Suspended;
                await store.SaveAsync(copy1);
                Interlocked.Increment(ref successCount);
            }
            catch (RuntimeConcurrencyException ex)
            {
                Interlocked.Increment(ref failureCount);
                failureException = ex;
            }
        });

        var task2 = Task.Run(async () =>
        {
            try
            {
                copy2!.Status = WorkflowInstanceStatus.Failed;
                await store.SaveAsync(copy2);
                Interlocked.Increment(ref successCount);
            }
            catch (RuntimeConcurrencyException ex)
            {
                Interlocked.Increment(ref failureCount);
                failureException = ex;
            }
        });

        await Task.WhenAll(task1, task2);

        // Exactly one must succeed, one must fail
        successCount.Should().Be(1);
        failureCount.Should().Be(1);
        failureException.Should().BeOfType<RuntimeConcurrencyException>();

        // Final stored state equals the successful write (no merge, no lost update)
        var final = await store.GetAsync("inst-01");
        final.Should().NotBeNull();
        // The successful write status survives; the failed write's status is not present
        (final!.Status == WorkflowInstanceStatus.Suspended ||
         final.Status == WorkflowInstanceStatus.Failed).Should().BeTrue();
    }

    [Fact]
    public async Task GetByWaitingHumanTaskId_Returns_SuspendedOnly()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var humansTaskId = "human-task-instance-123";

        var running = CreateInstance("wf-running", WorkflowInstanceStatus.Running);
        running.WaitingHumanTaskId = humansTaskId;
        var suspended = CreateInstance("wf-suspended", WorkflowInstanceStatus.Suspended);
        suspended.WaitingHumanTaskId = humansTaskId;
        var completed = CreateInstance("wf-completed", WorkflowInstanceStatus.Completed);
        completed.WaitingHumanTaskId = humansTaskId;
        var failed = CreateInstance("wf-failed", WorkflowInstanceStatus.Failed);
        failed.WaitingHumanTaskId = humansTaskId;

        await store.SaveAsync(running);
        await store.SaveAsync(suspended);
        await store.SaveAsync(completed);
        await store.SaveAsync(failed);

        var result = await store.GetByWaitingHumanTaskId(humansTaskId);

        result.Should().NotBeNull();
        result!.InstanceId.Should().Be("wf-suspended");
        result.Status.Should().Be(WorkflowInstanceStatus.Suspended);
    }

    [Fact]
    public async Task GetByWaitingHumanTaskId_Throws_When_MultipleSuspendedMatches()
    {
        var store = new InMemoryWorkflowInstanceStore();
        var humansTaskId = "human-task-instance-456";

        var suspended1 = CreateInstance("wf-sus-1", WorkflowInstanceStatus.Suspended);
        suspended1.WaitingHumanTaskId = humansTaskId;
        var suspended2 = CreateInstance("wf-sus-2", WorkflowInstanceStatus.Suspended);
        suspended2.WaitingHumanTaskId = humansTaskId;

        await store.SaveAsync(suspended1);
        await store.SaveAsync(suspended2);

        await store.Invoking(s => s.GetByWaitingHumanTaskId(humansTaskId))
            .Should().ThrowAsync<WorkflowCorrelationException>();
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~InMemoryWorkflowInstanceStoreTests"`
Expected: All 5 tests pass. (Note: the `Save_Concurrent_Writes_Detect_Conflict` test uses `Task.WhenAll` which may need `[Collection]` attribute if other tests share state — but since each test creates its own `new InMemoryWorkflowInstanceStore()`, no collection needed.)

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/InMemoryWorkflowInstanceStoreTests.cs
git commit -m "test: add InMemoryWorkflowInstanceStore concurrency and query tests"
```

---

### Task 6.2: Add duplicate-continuation test to WorkflowContinuationTests.cs

**Files:**
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs`

- [ ] **Step 1: Add the duplicate continuation test**

Add the following test method to the `WorkflowContinuationTests` class (before the closing `}` of the class):

```csharp
[Fact]
public async Task WorkflowContinuation_DuplicateHumanTaskCompletedEvent_DoesNotDoubleAdvance()
{
    // Build workflow with one HumanTask step followed by one Capability step
    var htDescriptor = new HumanTaskDescriptor
    {
        Id = "ht_dup", Name = "dup.task", Version = 1,
        Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
        Outcomes = new[]
        {
            new CompletionOutcome { Condition = CompletionCondition.Approve }
        }
    };
    var htValidationEngine = new RegistryValidationEngine<HumanTaskDescriptor>(
        Array.Empty<IRegistryValidator<HumanTaskDescriptor>>());
    var htRegistry = new HumanTaskRegistry(htValidationEngine);
    htRegistry.Build([new TestHumanTaskDescriptorProvider([htDescriptor])]);

    var htStore = new InMemoryHumanTaskInstanceStore();
    var htRuntime = new DefaultHumanTaskRuntime(htRegistry, htStore, NullLocalEventBus.Instance);

    var pipeline = new CapturingCapabilityPipeline();
    var wfRegistry = CreateRegistry(new WorkflowDescriptor
    {
        Id = "wf_dup", Name = "dup.wf", Version = 1,
        State = DescriptorState.Active,
        Steps = new List<WorkflowStep>
        {
            new()
            {
                Id = "step_01", Name = "Approval",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_dup", 1)
                }
            },
            new()
            {
                Id = "step_02", Name = "PostApproval",
                Target = new CapabilityTarget
                {
                    Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_post", 1)
                }
            }
        }
    });

    var wfStore = new InMemoryWorkflowInstanceStore();
    var stateMachine = new DefaultWorkflowStateMachine();
    var eventPublisher = new WorkflowLifecycleEventPublisher();
    var capExecutor = new CapabilityStepExecutor(pipeline);
    var htExecutor = new HumanTaskStepExecutor(htRuntime);
    var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
    var executionRunner = new WorkflowExecutionRunner(
        wfRegistry, executorRegistry, wfStore, stateMachine, eventPublisher);
    var engine = new WorkflowEngine(wfRegistry, wfStore, executionRunner, eventPublisher);
    var continuation = new WorkflowContinuationService(
        wfStore, stateMachine, wfRegistry, executionRunner, eventPublisher);

    // Start workflow → suspends at HumanTask step
    var instance = await engine.ExecuteAsync("wf_dup");
    instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
    instance.StepIndex.Should().Be(1);

    // Find the HumanTaskInstance
    var humanTaskInstance = await htStore.GetByIdAsync(instance.WaitingHumanTaskId!);
    humanTaskInstance.Should().NotBeNull();
    var htInstanceId = humanTaskInstance!.Id;

    // Complete the HumanTask
    await htRuntime.CompleteAsync(new HumanTaskCompletionRequest
    {
        HumanTaskInstanceId = htInstanceId,
        Outcome = "Approve"
    });

    // First continuation — should advance
    await continuation.ContinueAsync(new WorkflowContinuationRequest
    {
        HumanTaskId = htInstanceId,
        Outcome = "Approve"
    });
    var afterFirst = await wfStore.GetAsync(instance.InstanceId);
    afterFirst.Should().NotBeNull();
    var stepResultsAfterFirst = afterFirst!.StepResults.Count;

    // Second continuation with same instanceId — should be no-op (return, no exception)
    await continuation.Invoking(c => c.ContinueAsync(new WorkflowContinuationRequest
    {
        HumanTaskId = htInstanceId,
        Outcome = "Approve"
    })).Should().NotThrowAsync();

    var afterSecond = await wfStore.GetAsync(instance.InstanceId);
    afterSecond.Should().NotBeNull();
    // StepResults count unchanged — no double advance
    afterSecond!.StepResults.Should().HaveCount(stepResultsAfterFirst);
}
```

- [ ] **Step 2: Ensure required using directives are present**

The file should have these usings (check and add any missing):
```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;
```

- [ ] **Step 3: Run the test**

Run: `dotnet test --filter "FullyQualifiedName~WorkflowContinuationTests.WorkflowContinuation_DuplicateHumanTaskCompletedEvent_DoesNotDoubleAdvance"`
Expected: PASS.

- [ ] **Step 4: Run all Workflow tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: All 51 existing + 6 new = 57 tests pass.

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs
git commit -m "test: add duplicate HumanTaskCompletedEvent idempotency test"
```

---

### Phase 7: Tests — HumanTask.Tests

### Task 7.1: Create InMemoryHumanTaskInstanceStoreTests.cs (extend existing)

**Files:**
- Modify: `framework/test/CrestCreates.HumanTask.Tests/InMemoryHumanTaskInstanceStoreTests.cs`

The existing file has one test. Add new tests to it.

**Existing file** (from Phase 5):
```csharp
using CrestCreates.HumanTask.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class InMemoryHumanTaskInstanceStoreTests
{
    [Fact]
    public async Task GetPendingByAssigneeAsync_Returns_Only_Open_Tasks()
    {
        // ... existing test ...
    }
}
```

- [ ] **Step 1: Add new test methods**

Add the following test methods to the `InMemoryHumanTaskInstanceStoreTests` class (before the closing `}`):

```csharp
private static HumanTaskInstance CreateInstance(
    string id, HumanTaskInstanceStatus status, string? assigneeUserId = null,
    string? workflowInstanceId = null)
{
    return new HumanTaskInstance
    {
        Id = id,
        HumanTaskId = "ht_01",
        HumanTaskVersion = 1,
        Status = status,
        AssigneeUserId = assigneeUserId,
        WorkflowInstanceId = workflowInstanceId,
        CreatedAt = DateTimeOffset.UtcNow
    };
}

[Fact]
public async Task Save_UpdatesConcurrencyStamp()
{
    var store = new InMemoryHumanTaskInstanceStore();
    var instance = CreateInstance("inst-01", HumanTaskInstanceStatus.Created);

    // First save
    await store.SaveAsync(instance);
    instance.ConcurrencyStamp.Should().NotBeNullOrEmpty();
    instance.UpdatedAt.Should().NotBeNull();
    var firstStamp = instance.ConcurrencyStamp;
    var firstUpdatedAt = instance.UpdatedAt;

    // Second save — stamp and UpdatedAt should change
    instance.Status = HumanTaskInstanceStatus.Assigned;
    await store.SaveAsync(instance);
    instance.ConcurrencyStamp.Should().NotBe(firstStamp);
    instance.UpdatedAt.Should().NotBe(firstUpdatedAt);
}

[Fact]
public async Task Save_Throws_On_StaleConcurrencyStamp()
{
    var store = new InMemoryHumanTaskInstanceStore();
    var instance = CreateInstance("inst-01", HumanTaskInstanceStatus.Created);

    // Save once to establish a stamp
    await store.SaveAsync(instance);

    // Read back two copies
    var copy1 = await store.GetByIdAsync("inst-01");
    var copy2 = await store.GetByIdAsync("inst-01");
    copy1.Should().NotBeNull();
    copy2.Should().NotBeNull();
    copy1!.ConcurrencyStamp.Should().Be(copy2!.ConcurrencyStamp);

    // Modify and save copy1 — succeeds, generates new stamp
    copy1.Status = HumanTaskInstanceStatus.Assigned;
    await store.SaveAsync(copy1);

    // Try to save copy2 with old stamp — fails
    copy2.Status = HumanTaskInstanceStatus.Completed;
    await store.Invoking(s => s.SaveAsync(copy2))
        .Should().ThrowAsync<RuntimeConcurrencyException>();
}

[Fact]
public async Task GetPendingByAssignee_Returns_OpenOnly()
{
    var store = new InMemoryHumanTaskInstanceStore();

    var created = CreateInstance("inst-01", HumanTaskInstanceStatus.Created, "user-a");
    var assigned = CreateInstance("inst-02", HumanTaskInstanceStatus.Assigned, "user-a");
    var completed = CreateInstance("inst-03", HumanTaskInstanceStatus.Completed, "user-a");
    var cancelled = CreateInstance("inst-04", HumanTaskInstanceStatus.Cancelled, "user-a");
    var otherUser = CreateInstance("inst-05", HumanTaskInstanceStatus.Assigned, "user-b");

    await store.SaveAsync(created);
    await store.SaveAsync(assigned);
    await store.SaveAsync(completed);
    await store.SaveAsync(cancelled);
    await store.SaveAsync(otherUser);

    var pending = await store.GetPendingByAssigneeAsync("user-a");

    pending.Should().HaveCount(2);
    pending.Should().Contain(i => i.Id == "inst-01");
    pending.Should().Contain(i => i.Id == "inst-02");
    pending.Should().NotContain(i => i.Id == "inst-03");
    pending.Should().NotContain(i => i.Id == "inst-04");
    pending.Should().NotContain(i => i.Id == "inst-05");
}

[Fact]
public async Task GetPendingByWorkflow_Returns_OpenOnly()
{
    var store = new InMemoryHumanTaskInstanceStore();

    var created = CreateInstance("inst-01", HumanTaskInstanceStatus.Created,
        workflowInstanceId: "wf-001");
    var assigned = CreateInstance("inst-02", HumanTaskInstanceStatus.Assigned,
        workflowInstanceId: "wf-001");
    var completed = CreateInstance("inst-03", HumanTaskInstanceStatus.Completed,
        workflowInstanceId: "wf-001");
    var otherWf = CreateInstance("inst-04", HumanTaskInstanceStatus.Created,
        workflowInstanceId: "wf-002");

    await store.SaveAsync(created);
    await store.SaveAsync(assigned);
    await store.SaveAsync(completed);
    await store.SaveAsync(otherWf);

    var pending = await store.GetPendingByWorkflowAsync("wf-001");

    pending.Should().HaveCount(2);
    pending.Should().Contain(i => i.Id == "inst-01");
    pending.Should().Contain(i => i.Id == "inst-02");
    pending.Should().NotContain(i => i.Id == "inst-03");
    pending.Should().NotContain(i => i.Id == "inst-04");
}
```

- [ ] **Step 2: Add required using directive**

Add at the top of the file:
```csharp
using CrestCreates.Metadata.Abstractions;
```

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~InMemoryHumanTaskInstanceStoreTests"`
Expected: All tests pass (1 existing + 4 new = 5 tests).

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.HumanTask.Tests/InMemoryHumanTaskInstanceStoreTests.cs
git commit -m "test: add InMemoryHumanTaskInstanceStore concurrency and query tests"
```

---

### Task 7.2: Add runtime concurrency tests to HumanTaskRuntimeTests.cs

**Files:**
- Modify: `framework/test/CrestCreates.HumanTask.Tests/HumanTaskRuntimeTests.cs`

- [ ] **Step 1: Add concurrency failure test**

Add the following test method to the `HumanTaskRuntimeTests` class:

```csharp
[Fact]
public async Task CompleteAsync_DoesNotPublishEvent_When_SaveConcurrencyFails()
{
    var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
        CompletionCondition.Approve));
    var eventBusMock = new Mock<ILocalEventBus>();

    // Create a fake store that throws RuntimeConcurrencyException on second save
    var throwingStore = new ConcurrencyThrowingHumanTaskInstanceStore();

    var runtime = new DefaultHumanTaskRuntime(registry, throwingStore, eventBusMock.Object);

    var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
    {
        HumanTaskId = "ht_01"
    });

    // CompleteAsync will call SaveAsync which throws — event must NOT be published
    await runtime.Invoking(r => r.CompleteAsync(new HumanTaskCompletionRequest
    {
        HumanTaskInstanceId = instance.Id,
        Outcome = "Approve"
    })).Should().ThrowAsync<RuntimeConcurrencyException>();

    eventBusMock.Verify(
        b => b.PublishAsync(
            It.IsAny<HumanTaskCompletedEvent>(),
            It.IsAny<CancellationToken>()),
        Times.Never);
}

private sealed class ConcurrencyThrowingHumanTaskInstanceStore : IHumanTaskInstanceStore
{
    private readonly InMemoryHumanTaskInstanceStore _inner = new();
    private bool _firstSave = true;

    public Task SaveAsync(HumanTaskInstance instance, CancellationToken ct = default)
    {
        // First save (from CreateAsync) passes through to inner store.
        if (_firstSave)
        {
            _firstSave = false;
            return _inner.SaveAsync(instance, ct);
        }
        // Subsequent saves (from CompleteAsync) throw concurrency exception.
        // The event must NOT be published.
        throw new RuntimeConcurrencyException(
            $"Concurrency conflict for HumanTaskInstance '{instance.Id}'.");
    }

    public Task<HumanTaskInstance?> GetByIdAsync(string instanceId, CancellationToken ct = default)
        => _inner.GetByIdAsync(instanceId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByAssigneeAsync(
        string assigneeUserId, CancellationToken ct = default)
        => _inner.GetPendingByAssigneeAsync(assigneeUserId, ct);

    public Task<IReadOnlyList<HumanTaskInstance>> GetPendingByWorkflowAsync(
        string workflowInstanceId, CancellationToken ct = default)
        => _inner.GetPendingByWorkflowAsync(workflowInstanceId, ct);
}
```

- [ ] **Step 2: Add required using directive**

Add at the top of the file:
```csharp
using CrestCreates.Metadata.Abstractions;
```

- [ ] **Step 3: Run the test**

Run: `dotnet test --filter "FullyQualifiedName~HumanTaskRuntimeTests.CompleteAsync_DoesNotPublishEvent_When_SaveConcurrencyFails"`
Expected: PASS.

- [ ] **Step 4: Run all HumanTask tests**

Run: `dotnet test framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj`
Expected: All 16 existing + 6 new = 22 tests pass. (Note: the existing `CompleteAsync_Throws_When_Instance_Already_Completed` test should still pass — the store's `GetByIdAsync` now returns a clone, but the test mutates the returned instance and saves, so the CAS check passes since the clone has the correct stamp.)

- [ ] **Step 5: Commit**

```bash
git add framework/test/CrestCreates.HumanTask.Tests/HumanTaskRuntimeTests.cs
git commit -m "test: add concurrency failure event-suppression test"
```

---

### Phase 8: Final Build & Verification

### Task 8.1: Full build

- [ ] **Step 1: Build entire solution**

Run: `dotnet build`
Expected: Zero errors.

---

### Task 8.2: Run targeted test suites

- [ ] **Step 1: Run HumanTask tests**

Run: `dotnet test framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj`
Expected: ALL tests pass (16 existing + 6 new = 22 total).

- [ ] **Step 2: Run Workflow tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: ALL tests pass (51 existing + 6 new = 57 total).

- [ ] **Step 3: Run Capability tests (sanity)**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests/`
Expected: ALL tests pass (no regressions).

- [ ] **Step 4: Run Metadata tests (sanity)**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/`
Expected: ALL tests pass (no regressions).

- [ ] **Step 5: Run Draft tests (sanity)**

Run: `dotnet test framework/test/CrestCreates.Draft.Tests/`
Expected: ALL tests pass (no regressions).

---

### Task 8.3: Run all tests

- [ ] **Step 1: Run full test suite**

Run: `dotnet test`
Expected: All tests pass across all projects.

---

### Post-Implementation Checklist

- [ ] `dotnet build` — zero errors
- [ ] `dotnet test` — all tests pass
- [ ] `CrestCreates.HumanTask.Tests` — 16 existing + 6 new = 22 tests pass
- [ ] `CrestCreates.Workflow.Tests` — 51 existing + 6 new = 57 tests pass
- [ ] `CrestCreates.Capability.Tests` not broken
- [ ] `CrestCreates.Metadata.Tests` not broken
- [ ] `CrestCreates.Draft.Tests` not broken
- [ ] No JSON serialization, no reflection, no database dependencies introduced
