# Phase 4c — Workflow Runtime Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the Workflow Runtime execution loop — HumanTask completion triggers automatic workflow continuation via `IWorkflowContinuationService` with lifecycle events.

**Architecture:** Extract `IWorkflowExecutionRunner` from the engine's step loop as a shared execution core. Add `IWorkflowStateMachine` for transition validation, `IWorkflowLifecycleEventPublisher` for lifecycle events, and `IWorkflowContinuationService` for HumanTask→resume. `HumanTaskStepExecutor` stays pure. No `ResumeAsync` reintroduced.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions

---

## File Map

| File | Action | Project |
|------|--------|---------|
| `IWorkflowStateMachine.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `InvalidWorkflowTransitionException.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowLifecycleEventPublisher.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `WorkflowLifecycleEvent.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowContinuationService.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowInstanceStore.cs` | Modify | `CrestCreates.Workflow.Abstractions` |
| `WorkflowInstance.cs` | Modify | `CrestCreates.Workflow.Abstractions` |
| `HumanTaskCompletedEvent.cs` | Create | `CrestCreates.HumanTask.Abstractions` |
| `IWorkflowExecutionRunner.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowExecutionRunner.cs` | Create | `CrestCreates.Workflow` |
| `DefaultWorkflowStateMachine.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowLifecycleEventPublisher.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowContinuationService.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowEngine.cs` | Modify | `CrestCreates.Workflow` |
| `InMemoryWorkflowInstanceStore.cs` | Modify | `CrestCreates.Workflow` |
| `WorkflowServiceCollectionExtensions.cs` | Modify | `CrestCreates.Workflow` |
| `WorkflowStateMachineTests.cs` | Create | `CrestCreates.Workflow.Tests` |
| `WorkflowContinuationTests.cs` | Create | `CrestCreates.Workflow.Tests` |
| `WorkflowEngineTests.cs` | Modify | `CrestCreates.Workflow.Tests` |

---

### Task 1: Create IWorkflowStateMachine + InvalidWorkflowTransitionException

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStateMachine.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/InvalidWorkflowTransitionException.cs`

- [ ] **Step 1: Create exception type**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/InvalidWorkflowTransitionException.cs
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow.Abstractions;

public sealed class InvalidWorkflowTransitionException : Exception
{
    public WorkflowInstanceStatus From { get; }
    public WorkflowInstanceStatus To { get; }

    public InvalidWorkflowTransitionException(WorkflowInstanceStatus from, WorkflowInstanceStatus to)
        : base($"Invalid workflow state transition: {from} → {to}.")
    {
        From = from;
        To = to;
    }
}
```

- [ ] **Step 2: Create state machine interface**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStateMachine.cs
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowStateMachine
{
    void ValidateTransition(WorkflowInstanceStatus from, WorkflowInstanceStatus to);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStateMachine.cs \
        framework/src/CrestCreates.Workflow.Abstractions/InvalidWorkflowTransitionException.cs
git commit -m "feat: add IWorkflowStateMachine + InvalidWorkflowTransitionException"
```

---

### Task 2: Create WorkflowLifecycleEvent + IWorkflowLifecycleEventPublisher

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowLifecycleEvent.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowLifecycleEventPublisher.cs`

- [ ] **Step 1: Create event type**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/WorkflowLifecycleEvent.cs
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowLifecycleEvent
{
    public string EventType { get; init; } = string.Empty;
    public string WorkflowInstanceId { get; init; } = string.Empty;
    public string WorkflowId { get; init; } = string.Empty;
    public WorkflowInstanceStatus Status { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public object? Payload { get; init; }
}
```

- [ ] **Step 2: Create publisher interface**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowLifecycleEventPublisher.cs
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowLifecycleEventPublisher
{
    Task PublishAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct);
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowLifecycleEvent.cs \
        framework/src/CrestCreates.Workflow.Abstractions/IWorkflowLifecycleEventPublisher.cs
git commit -m "feat: add WorkflowLifecycleEvent + IWorkflowLifecycleEventPublisher"
```

---

### Task 3: Create IWorkflowContinuationService

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowContinuationService.cs`

- [ ] **Step 1: Create interface**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowContinuationService.cs
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowContinuationService
{
    Task ContinueAsync(
        string humanTaskId,
        object? result,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowContinuationService.cs
git commit -m "feat: add IWorkflowContinuationService interface"
```

---

### Task 4: Extend WorkflowInstance + IWorkflowInstanceStore

**Files:**
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs`
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs`

- [ ] **Step 1: Add WaitingHumanTaskId to WorkflowInstance**

Current file:
```csharp
public sealed class WorkflowInstance
{
    public string InstanceId { get; init; } = Guid.NewGuid().ToString("N");
    public VersionedDescriptorRef<WorkflowDescriptor> Workflow { get; init; }
    public WorkflowInstanceStatus Status { get; set; } = WorkflowInstanceStatus.Running;
    public string? CurrentStepId { get; set; }
    public int StepIndex { get; set; }
    // ... other fields
}
```

Add after `StepIndex`:
```csharp
    public string? WaitingHumanTaskId { get; set; }
```

- [ ] **Step 2: Add GetByWaitingHumanTaskId to IWorkflowInstanceStore**

Current file (Phase 4b):
```csharp
public interface IWorkflowInstanceStore
{
    Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default);
    Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default);
}
```

Add after `GetAsync`:
```csharp
    Task<WorkflowInstance?> GetByWaitingHumanTaskId(
        string humanTaskId,
        CancellationToken ct = default);
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs \
        framework/src/CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs
git commit -m "feat: add WaitingHumanTaskId to WorkflowInstance, GetByWaitingHumanTaskId to IWorkflowInstanceStore"
```

---

### Task 5: Create HumanTaskCompletedEvent

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs`

- [ ] **Step 1: Create event**

```csharp
// framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs
namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletedEvent
{
    public string HumanTaskId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs
git commit -m "feat: add HumanTaskCompletedEvent — HumanTask domain only, no Workflow fields"
```

---

### Task 6: Write state machine tests (TDD)

**Files:**
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowStateMachineTests.cs`

- [ ] **Step 1: Create test file**

```csharp
// framework/test/CrestCreates.Workflow.Tests/WorkflowStateMachineTests.cs
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowStateMachineTests
{
    private readonly IWorkflowStateMachine _machine = new DefaultWorkflowStateMachine();

    [Fact]
    public void ValidateTransition_RunningToSuspended_DoesNotThrow()
    {
        var act = () => _machine.ValidateTransition(
            WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Suspended);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTransition_RunningToCompleted_DoesNotThrow()
    {
        var act = () => _machine.ValidateTransition(
            WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Completed);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTransition_RunningToFailed_DoesNotThrow()
    {
        var act = () => _machine.ValidateTransition(
            WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Failed);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTransition_SuspendedToRunning_DoesNotThrow()
    {
        var act = () => _machine.ValidateTransition(
            WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateTransition_CompletedToRunning_Throws()
    {
        var act = () => _machine.ValidateTransition(
            WorkflowInstanceStatus.Completed, WorkflowInstanceStatus.Running);
        act.Should().Throw<InvalidWorkflowTransitionException>();
    }

    [Fact]
    public void ValidateTransition_SuspendedToSuspended_Throws()
    {
        var act = () => _machine.ValidateTransition(
            WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Suspended);
        act.Should().Throw<InvalidWorkflowTransitionException>();
    }

    [Fact]
    public void ValidateTransition_FailedToRunning_Throws()
    {
        var act = () => _machine.ValidateTransition(
            WorkflowInstanceStatus.Failed, WorkflowInstanceStatus.Running);
        act.Should().Throw<InvalidWorkflowTransitionException>();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (DefaultWorkflowStateMachine not implemented)**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj --filter "FullyQualifiedName~WorkflowStateMachineTests"`
Expected: Compilation error.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowStateMachineTests.cs
git commit -m "test: add WorkflowStateMachine tests (TDD)"
```

---

### Task 7: Implement DefaultWorkflowStateMachine

**Files:**
- Create: `framework/src/CrestCreates.Workflow/DefaultWorkflowStateMachine.cs`

- [ ] **Step 1: Implement the state machine**

```csharp
// framework/src/CrestCreates.Workflow/DefaultWorkflowStateMachine.cs
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class DefaultWorkflowStateMachine : IWorkflowStateMachine
{
    public void ValidateTransition(WorkflowInstanceStatus from, WorkflowInstanceStatus to)
    {
        var valid = (from, to) switch
        {
            (WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Suspended) => true,
            (WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Completed) => true,
            (WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Failed) => true,
            (WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running) => true,
            _ => false
        };

        if (!valid)
            throw new InvalidWorkflowTransitionException(from, to);
    }
}
```

- [ ] **Step 2: Run state machine tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj --filter "FullyQualifiedName~WorkflowStateMachineTests"`
Expected: All 7 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/DefaultWorkflowStateMachine.cs
git commit -m "feat: implement DefaultWorkflowStateMachine"
```

---

### Task 8: Create IWorkflowExecutionRunner + WorkflowExecutionRunner (extract from engine)

**Files:**
- Create: `framework/src/CrestCreates.Workflow/IWorkflowExecutionRunner.cs`
- Create: `framework/src/CrestCreates.Workflow/WorkflowExecutionRunner.cs`

The execution runner is the step loop extracted from the current `WorkflowEngine.ExecuteStepsAsync`. It takes an instance with `Status=Running` and executes steps via the executor registry. It handles all state transitions (Completed, Suspended, Failed, Skip) and persistence — but does NOT publish lifecycle events.

- [ ] **Step 1: Create internal interface**

```csharp
// framework/src/CrestCreates.Workflow/IWorkflowExecutionRunner.cs
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal interface IWorkflowExecutionRunner
{
    Task<WorkflowInstance> RunAsync(
        WorkflowInstance instance,
        CancellationToken ct);
}
```

- [ ] **Step 2: Implement the runner**

Read the current `WorkflowEngine.ExecuteStepsAsync` (lines 45-150 in current `WorkflowEngine.cs`). Extract it into `WorkflowExecutionRunner`:

```csharp
// framework/src/CrestCreates.Workflow/WorkflowExecutionRunner.cs
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowExecutionRunner : IWorkflowExecutionRunner
{
    private readonly IWorkflowStepExecutorRegistry _executorRegistry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;

    public WorkflowExecutionRunner(
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine)
    {
        _executorRegistry = executorRegistry;
        _store = store;
        _stateMachine = stateMachine;
    }

    public async Task<WorkflowInstance> RunAsync(
        WorkflowInstance instance,
        CancellationToken ct)
    {
        var descriptor = /* need WorkflowDescriptor here... */;
        // We pass the descriptor through a different mechanism.
        // See Task 10 — engine stores it on instance or passes contextually.
        return await ExecuteStepsAsync(instance, descriptor, ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteStepsAsync(
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        CancellationToken ct)
    {
        var steps = descriptor.Steps;

        while (instance.StepIndex < steps.Count)
        {
            // SAME step loop as current Phase 4b WorkflowEngine.ExecuteStepsAsync
            // but WITH stateMachine.ValidateTransition() calls at transition points
        }

        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.CurrentStepId = null;
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }
}
```

Wait — the runner needs the `WorkflowDescriptor` to iterate steps. Currently the engine stores it locally in `ExecuteStepsAsync`. The runner needs access to it. Solution: store `WorkflowId` on the instance (already present via `Workflow` ref), then resolve the descriptor inside the runner via `IWorkflowRegistry`.

Let me rewrite this properly. The runner needs `IWorkflowRegistry` to resolve the descriptor from `instance.Workflow.Id`:

```csharp
// framework/src/CrestCreates.Workflow/WorkflowExecutionRunner.cs
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowExecutionRunner : IWorkflowExecutionRunner
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowStepExecutorRegistry _executorRegistry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;

    public WorkflowExecutionRunner(
        IWorkflowRegistry registry,
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine)
    {
        _registry = registry;
        _executorRegistry = executorRegistry;
        _store = store;
        _stateMachine = stateMachine;
    }

    public async Task<WorkflowInstance> RunAsync(
        WorkflowInstance instance,
        CancellationToken ct)
    {
        var descriptor = _registry.GetById(instance.Workflow.Id);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{instance.Workflow.Id}' not found.");

        return await ExecuteStepsAsync(instance, descriptor, ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteStepsAsync(
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        CancellationToken ct)
    {
        var steps = descriptor.Steps;

        while (instance.StepIndex < steps.Count)
        {
            ct.ThrowIfCancellationRequested();

            var step = steps[instance.StepIndex];
            instance.CurrentStepId = step.Id;

            var startedAt = DateTimeOffset.UtcNow;

            var executor = _executorRegistry.Resolve(step.Target);
            var context = new WorkflowExecutionContext(descriptor, instance, step);

            StepExecutionResult stepResult;
            try
            {
                stepResult = await executor.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var history = new WorkflowStepResult
                {
                    StepId = step.Id,
                    StepName = step.Name,
                    Status = StepExecutionStatus.Failed,
                    ErrorMessage = ex.Message,
                    ExecutedAt = DateTimeOffset.UtcNow,
                    Duration = DateTimeOffset.UtcNow - startedAt
                };
                instance.StepResults.Add(history);

                _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Failed);
                instance.Status = WorkflowInstanceStatus.Failed;
                instance.ErrorMessage = ex.Message;
                instance.CompletedAt = DateTimeOffset.UtcNow;
                await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                return instance;
            }

            if (stepResult.Variables != null)
            {
                foreach (var kv in stepResult.Variables)
                    instance.Variables[kv.Key] = kv.Value;
            }

            var duration = DateTimeOffset.UtcNow - startedAt;
            var stepRecord = new WorkflowStepResult
            {
                StepId = step.Id,
                StepName = step.Name,
                Status = stepResult.Status,
                Output = stepResult.Output,
                ExecutedAt = DateTimeOffset.UtcNow,
                Duration = duration
            };
            instance.StepResults.Add(stepRecord);

            switch (stepResult.Status)
            {
                case StepExecutionStatus.Completed:
                    instance.StepIndex++;
                    continue;

                case StepExecutionStatus.Suspended:
                    // Set WaitingHumanTaskId from the step's HumanTask target
                    if (step.Target is HumanTaskTarget htTarget)
                        instance.WaitingHumanTaskId = htTarget.HumanTask.Id;

                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Suspended);
                    instance.Status = WorkflowInstanceStatus.Suspended;
                    instance.CurrentStepId = null;
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    return instance;

                case StepExecutionStatus.Failed:
                    if (step.OnError == StepErrorBehavior.Skip)
                    {
                        instance.StepIndex++;
                        continue;
                    }

                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Failed);
                    instance.Status = WorkflowInstanceStatus.Failed;
                    instance.ErrorMessage = $"Step '{step.Id}' failed.";
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    return instance;

                default:
                    throw new InvalidOperationException(
                        $"Unknown StepExecutionStatus: {stepResult.Status}");
            }
        }

        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.CurrentStepId = null;
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }
}
```

- [ ] **Step 3: Build to verify**

Now that the runner exists but `WorkflowEngine` still has its own `ExecuteStepsAsync`, there will be no conflicts yet. Build the Workflow project:

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded (runner compiles independently).

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow/IWorkflowExecutionRunner.cs \
        framework/src/CrestCreates.Workflow/WorkflowExecutionRunner.cs
git commit -m "feat: extract WorkflowExecutionRunner — shared step loop with state machine"
```

---

### Task 9: Refactor WorkflowEngine to use WorkflowExecutionRunner

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/WorkflowEngine.cs`

The engine now delegates to `IWorkflowExecutionRunner` instead of running its own `ExecuteStepsAsync`. The engine handles: descriptor resolution, instance creation, event publishing, top-level error handling. The runner handles: step iteration, executor dispatch, state transitions, persistence.

- [ ] **Step 1: Rewrite WorkflowEngine**

```csharp
// framework/src/CrestCreates.Workflow/WorkflowEngine.cs
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    public WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowInstanceStore store,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher)
    {
        _registry = registry;
        _store = store;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
    }

    public async Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default)
    {
        var descriptor = _registry.GetById(workflowId);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{workflowId}' not found.");

        var instance = new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(descriptor.Id, descriptor.Version)
        };

        if (inputVariables != null)
        {
            foreach (var kv in inputVariables)
                instance.Variables[kv.Key] = kv.Value;
        }

        // Publish workflow.started BEFORE execution
        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = "workflow.started",
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            Status = WorkflowInstanceStatus.Running
        }, ct).ConfigureAwait(false);

        // Delegate to shared execution runner
        instance = await _executionRunner.RunAsync(instance, ct).ConfigureAwait(false);

        // Publish terminal event based on final status
        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = instance.Status switch
            {
                WorkflowInstanceStatus.Completed => "workflow.completed",
                WorkflowInstanceStatus.Failed => "workflow.failed",
                WorkflowInstanceStatus.Suspended => "workflow.suspended",
                _ => instance.Status.ToString()
            },
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            Status = instance.Status
        }, ct).ConfigureAwait(false);

        // Always persist final state
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }
}
```

**Removed:** All of `ExecuteStepsAsync` (now in `WorkflowExecutionRunner`), `IWorkflowStepExecutorRegistry` dependency, `IWorkflowStateMachine` dependency (runner owns those).

**Wait — there's a problem with event ordering.** The engine publishes `workflow.suspended` AFTER the runner returns (which already saved the instance). The spec says: "HumanTask 步骤返回 Suspended 后、保存前". But with the runner doing the save, the engine can't publish between the step result and the save.

**Correction:** The runner should NOT save on suspend. Instead, it returns the instance with `Status=Suspended` and the engine publishes the event then saves. Let me reconsider.

Actually, looking at the spec again: the runner IS the execution core, and it persists state at each transition. If the engine publishes after the runner returns, the order is: `runner saves Suspended → engine publishes workflow.suspended`. This is fine — the event is "at least once" and the state is already persisted. The runtime doesn't require strict ordering between save and publish (no distributed transaction).

However, `workflow.resumed` is published by the ContinuationService (not the engine), so that ordering is separate.

Let me keep the runner as-is (it saves on each terminal state) and the engine publishes after the runner returns. This is consistent with "at-least-once" event semantics.

The `workflow.started` event IS published before execution — that's correct (runner hasn't started yet).

Let me finalize the engine code above. It's clean:

```csharp
public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    public WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowInstanceStore store,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher)
    {
        _registry = registry;
        _store = store;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
    }

    public async Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default)
    {
        var descriptor = _registry.GetById(workflowId);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{workflowId}' not found.");

        var instance = new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(descriptor.Id, descriptor.Version)
        };

        if (inputVariables != null)
        {
            foreach (var kv in inputVariables)
                instance.Variables[kv.Key] = kv.Value;
        }

        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = "workflow.started",
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            Status = WorkflowInstanceStatus.Running
        }, ct).ConfigureAwait(false);

        instance = await _executionRunner.RunAsync(instance, ct).ConfigureAwait(false);

        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = instance.Status switch
            {
                WorkflowInstanceStatus.Completed => "workflow.completed",
                WorkflowInstanceStatus.Failed => "workflow.failed",
                WorkflowInstanceStatus.Suspended => "workflow.suspended",
                _ => instance.Status.ToString()
            },
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            Status = instance.Status
        }, ct).ConfigureAwait(false);

        return instance;
    }
}
```

Actually, there's still an issue: the engine doesn't need to save at the end because the runner already saved when it returned a terminal status. The runner saves on: Suspended, Failed (non-Skip), Completed. So the engine's final save would be a double-save. Let me remove it — the runner handles all persistence.

OK the engine code is finalized above. Let me write it into the plan.

Actually, I should also fix the `workflow.failed` case for `ExecuteAsync` when the descriptor is not found. Currently it throws `InvalidOperationException`. The spec says `workflow.failed` should be published on "any failure exit". But the engine doesn't have an instance to publish for yet when the descriptor isn't found. Let me keep it as-is — if the descriptor isn't found, there's no workflow to publish a failed event for. The exception propagates to the caller.

But for internal failures (e.g., runner throws), the engine should catch and publish `workflow.failed`. Let me add a try/catch:

```csharp
try
{
    instance = await _executionRunner.RunAsync(instance, ct).ConfigureAwait(false);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
    {
        EventType = "workflow.failed",
        WorkflowInstanceId = instance.InstanceId,
        WorkflowId = descriptor.Id,
        Status = WorkflowInstanceStatus.Failed,
        Payload = new { Error = ex.Message }
    }, ct).ConfigureAwait(false);
    throw;
}
```

OK, this is getting complex for a plan. Let me just present the core engine rewrite and note the wrapping. Let me write the plan concisely.

Actually, I realize I should simplify. The runner already catches exceptions for individual steps. The only exception that would escape the runner is something catastrophic (like the store throwing during save). In that case, the engine's try/catch around `RunAsync` is the right place for `workflow.failed`.

Let me just put the key code in the plan and move on.<｜end▁of▁thinking｜>

<｜｜DSML｜｜tool_calls>
<｜｜DSML｜｜invoke name="write">
<｜｜DSML｜｜parameter name="content" string="true"># Phase 4c — Workflow Runtime Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the Workflow Runtime execution loop — HumanTask completion triggers automatic workflow continuation via `IWorkflowContinuationService` with lifecycle events and shared execution core.

**Architecture:** Extract `IWorkflowExecutionRunner` from engine's step loop (shared by `WorkflowEngine` and `WorkflowContinuationService`). Add `IWorkflowStateMachine` for transition validation, `IWorkflowLifecycleEventPublisher` for 5 lifecycle events. `HumanTaskStepExecutor` stays pure. No `ResumeAsync` reintroduced.

**Note:** The spec uses `CurrentStepIndex` but the actual code property is `StepIndex`. All code in this plan uses `StepIndex`.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions

---

## File Map

| File | Action | Project |
|------|--------|---------|
| `IWorkflowStateMachine.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `InvalidWorkflowTransitionException.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `WorkflowLifecycleEvent.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowLifecycleEventPublisher.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowContinuationService.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowInstanceStore.cs` | Modify | `CrestCreates.Workflow.Abstractions` |
| `WorkflowInstance.cs` | Modify | `CrestCreates.Workflow.Abstractions` |
| `HumanTaskCompletedEvent.cs` | Create | `CrestCreates.HumanTask.Abstractions` |
| `IWorkflowExecutionRunner.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowExecutionRunner.cs` | Create | `CrestCreates.Workflow` |
| `DefaultWorkflowStateMachine.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowLifecycleEventPublisher.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowContinuationService.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowEngine.cs` | Modify | `CrestCreates.Workflow` |
| `InMemoryWorkflowInstanceStore.cs` | Modify | `CrestCreates.Workflow` |
| `WorkflowServiceCollectionExtensions.cs` | Modify | `CrestCreates.Workflow` |
| `WorkflowStateMachineTests.cs` | Create | `CrestCreates.Workflow.Tests` |
| `WorkflowContinuationTests.cs` | Create | `CrestCreates.Workflow.Tests` |
| `WorkflowEngineTests.cs` | Modify | `CrestCreates.Workflow.Tests` |

---

### Task 1: Create IWorkflowStateMachine + InvalidWorkflowTransitionException + DefaultWorkflowStateMachine

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStateMachine.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/InvalidWorkflowTransitionException.cs`
- Create: `framework/src/CrestCreates.Workflow/DefaultWorkflowStateMachine.cs`

- [ ] **Step 1: Create exception type**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/InvalidWorkflowTransitionException.cs
namespace CrestCreates.Workflow.Abstractions;

public sealed class InvalidWorkflowTransitionException : Exception
{
    public WorkflowInstanceStatus From { get; }
    public WorkflowInstanceStatus To { get; }

    public InvalidWorkflowTransitionException(WorkflowInstanceStatus from, WorkflowInstanceStatus to)
        : base($"Invalid workflow state transition: {from} → {to}.")
    {
        From = from;
        To = to;
    }
}
```

- [ ] **Step 2: Create state machine interface**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStateMachine.cs
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowStateMachine
{
    void ValidateTransition(WorkflowInstanceStatus from, WorkflowInstanceStatus to);
}
```

- [ ] **Step 3: Create implementation**

```csharp
// framework/src/CrestCreates.Workflow/DefaultWorkflowStateMachine.cs
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class DefaultWorkflowStateMachine : IWorkflowStateMachine
{
    public void ValidateTransition(WorkflowInstanceStatus from, WorkflowInstanceStatus to)
    {
        var valid = (from, to) switch
        {
            (WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Suspended) => true,
            (WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Completed) => true,
            (WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Failed) => true,
            (WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running) => true,
            _ => false
        };

        if (!valid)
            throw new InvalidWorkflowTransitionException(from, to);
    }
}
```

- [ ] **Step 4: Build and commit**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStateMachine.cs \
        framework/src/CrestCreates.Workflow.Abstractions/InvalidWorkflowTransitionException.cs \
        framework/src/CrestCreates.Workflow/DefaultWorkflowStateMachine.cs
git commit -m "feat: IWorkflowStateMachine + InvalidWorkflowTransitionException + DefaultWorkflowStateMachine"
```

---

### Task 2: Create WorkflowLifecycleEvent + publisher contracts

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowLifecycleEvent.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowLifecycleEventPublisher.cs`
- Create: `framework/src/CrestCreates.Workflow/WorkflowLifecycleEventPublisher.cs`

- [ ] **Step 1: Create event type + interface + no-op implementation**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/WorkflowLifecycleEvent.cs
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowLifecycleEvent
{
    public string EventType { get; init; } = string.Empty;
    public string WorkflowInstanceId { get; init; } = string.Empty;
    public string WorkflowId { get; init; } = string.Empty;
    public WorkflowInstanceStatus Status { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public object? Payload { get; init; }
}
```

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowLifecycleEventPublisher.cs
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowLifecycleEventPublisher
{
    Task PublishAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct);
}
```

```csharp
// framework/src/CrestCreates.Workflow/WorkflowLifecycleEventPublisher.cs
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowLifecycleEventPublisher : IWorkflowLifecycleEventPublisher
{
    public Task PublishAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct)
    {
        // Phase 4c: no-op. Phase 5+ integrates with Event Runtime.
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Build and commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowLifecycleEvent.cs \
        framework/src/CrestCreates.Workflow.Abstractions/IWorkflowLifecycleEventPublisher.cs \
        framework/src/CrestCreates.Workflow/WorkflowLifecycleEventPublisher.cs
git commit -m "feat: WorkflowLifecycleEvent + IWorkflowLifecycleEventPublisher + no-op publisher"
```

---

### Task 3: Create IWorkflowContinuationService + HumanTaskCompletedEvent

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowContinuationService.cs`
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs`

- [ ] **Step 1: Create continuation service interface**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowContinuationService.cs
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowContinuationService
{
    Task ContinueAsync(
        string humanTaskId,
        object? result,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Create HumanTaskCompletedEvent**

```csharp
// framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs
namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletedEvent
{
    public string HumanTaskId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
```

- [ ] **Step 3: Build and commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowContinuationService.cs \
        framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs
git commit -m "feat: IWorkflowContinuationService + HumanTaskCompletedEvent"
```

---

### Task 4: Extend WorkflowInstance + IWorkflowInstanceStore

**Files:**
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs` (line 12, add after `StepIndex`)
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs` (add method)

- [ ] **Step 1: Add `WaitingHumanTaskId` to WorkflowInstance**

After `public int StepIndex { get; set; }` (line 11), add:
```csharp
    public string? WaitingHumanTaskId { get; set; }
```

- [ ] **Step 2: Add `GetByWaitingHumanTaskId` to IWorkflowInstanceStore**

After `GetAsync` method, add:
```csharp
    Task<WorkflowInstance?> GetByWaitingHumanTaskId(
        string humanTaskId,
        CancellationToken ct = default);
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded. `InMemoryWorkflowInstanceStore` will have a compile error — expected, fixed in Task 13.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs \
        framework/src/CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs
git commit -m "feat: add WaitingHumanTaskId to WorkflowInstance, GetByWaitingHumanTaskId to store"
```

---

### Task 5: Write state machine tests (TDD)

**Files:**
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowStateMachineTests.cs`

- [ ] **Step 1: Create test file**

```csharp
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowStateMachineTests
{
    private readonly IWorkflowStateMachine _machine = new DefaultWorkflowStateMachine();

    [Fact]
    public void ValidateTransition_RunningToSuspended_DoesNotThrow()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Suspended))
            .Should().NotThrow();

    [Fact]
    public void ValidateTransition_RunningToCompleted_DoesNotThrow()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Completed))
            .Should().NotThrow();

    [Fact]
    public void ValidateTransition_RunningToFailed_DoesNotThrow()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Running, WorkflowInstanceStatus.Failed))
            .Should().NotThrow();

    [Fact]
    public void ValidateTransition_SuspendedToRunning_DoesNotThrow()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running))
            .Should().NotThrow();

    [Fact]
    public void ValidateTransition_CompletedToRunning_Throws()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Completed, WorkflowInstanceStatus.Running))
            .Should().Throw<InvalidWorkflowTransitionException>();

    [Fact]
    public void ValidateTransition_SuspendedToSuspended_Throws()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Suspended))
            .Should().Throw<InvalidWorkflowTransitionException>();

    [Fact]
    public void ValidateTransition_FailedToRunning_Throws()
        => new Action(() => _machine.ValidateTransition(
            WorkflowInstanceStatus.Failed, WorkflowInstanceStatus.Running))
            .Should().Throw<InvalidWorkflowTransitionException>();
}
```

- [ ] **Step 2: Run state machine tests (should already pass — implemented in Task 1)**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj --filter "FullyQualifiedName~WorkflowStateMachineTests"`
Expected: All 7 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowStateMachineTests.cs
git commit -m "test: add WorkflowStateMachine tests — 4 valid + 3 invalid transitions"
```

---

### Task 6: Create WorkflowExecutionRunner (extract step loop)

**Files:**
- Create: `framework/src/CrestCreates.Workflow/IWorkflowExecutionRunner.cs`
- Create: `framework/src/CrestCreates.Workflow/WorkflowExecutionRunner.cs`

The runner is the step loop extracted from the current `WorkflowEngine.ExecuteStepsAsync`. It resolves the descriptor, iterates steps, dispatches via executor registry, and persists via store. It does NOT publish events.

- [ ] **Step 1: Create internal interface**

```csharp
// framework/src/CrestCreates.Workflow/IWorkflowExecutionRunner.cs
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal interface IWorkflowExecutionRunner
{
    Task<WorkflowInstance> RunAsync(WorkflowInstance instance, CancellationToken ct);
}
```

- [ ] **Step 2: Implement the runner**

Copy the current `WorkflowEngine.ExecuteStepsAsync` method body (lines 45-150 of current `WorkflowEngine.cs`), add state machine validation at transition points, and set `WaitingHumanTaskId` on suspend:

```csharp
// framework/src/CrestCreates.Workflow/WorkflowExecutionRunner.cs
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class WorkflowExecutionRunner : IWorkflowExecutionRunner
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowStepExecutorRegistry _executorRegistry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;

    public WorkflowExecutionRunner(
        IWorkflowRegistry registry,
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine)
    {
        _registry = registry;
        _executorRegistry = executorRegistry;
        _store = store;
        _stateMachine = stateMachine;
    }

    public async Task<WorkflowInstance> RunAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var descriptor = _registry.GetById(instance.Workflow.Id);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{instance.Workflow.Id}' not found.");

        return await ExecuteStepsAsync(instance, descriptor, ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteStepsAsync(
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        CancellationToken ct)
    {
        var steps = descriptor.Steps;

        while (instance.StepIndex < steps.Count)
        {
            ct.ThrowIfCancellationRequested();

            var step = steps[instance.StepIndex];
            instance.CurrentStepId = step.Id;
            var startedAt = DateTimeOffset.UtcNow;

            var executor = _executorRegistry.Resolve(step.Target);
            var context = new WorkflowExecutionContext(descriptor, instance, step);

            StepExecutionResult stepResult;
            try
            {
                stepResult = await executor.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                instance.StepResults.Add(new WorkflowStepResult
                {
                    StepId = step.Id, StepName = step.Name,
                    Status = StepExecutionStatus.Failed, ErrorMessage = ex.Message,
                    ExecutedAt = DateTimeOffset.UtcNow,
                    Duration = DateTimeOffset.UtcNow - startedAt
                });
                _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Failed);
                instance.Status = WorkflowInstanceStatus.Failed;
                instance.ErrorMessage = ex.Message;
                instance.CompletedAt = DateTimeOffset.UtcNow;
                await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                return instance;
            }

            if (stepResult.Variables != null)
                foreach (var kv in stepResult.Variables)
                    instance.Variables[kv.Key] = kv.Value;

            instance.StepResults.Add(new WorkflowStepResult
            {
                StepId = step.Id, StepName = step.Name,
                Status = stepResult.Status, Output = stepResult.Output,
                ExecutedAt = DateTimeOffset.UtcNow,
                Duration = DateTimeOffset.UtcNow - startedAt
            });

            switch (stepResult.Status)
            {
                case StepExecutionStatus.Completed:
                    instance.StepIndex++;
                    continue;

                case StepExecutionStatus.Suspended:
                    if (step.Target is HumanTaskTarget htTarget)
                        instance.WaitingHumanTaskId = htTarget.HumanTask.Id;
                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Suspended);
                    instance.Status = WorkflowInstanceStatus.Suspended;
                    instance.CurrentStepId = null;
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    return instance;

                case StepExecutionStatus.Failed:
                    if (step.OnError == StepErrorBehavior.Skip)
                    { instance.StepIndex++; continue; }
                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Failed);
                    instance.Status = WorkflowInstanceStatus.Failed;
                    instance.ErrorMessage = $"Step '{step.Id}' failed.";
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    return instance;
            }
        }

        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.CurrentStepId = null;
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded (runner compiles independently, no conflicts with existing engine).

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow/IWorkflowExecutionRunner.cs \
        framework/src/CrestCreates.Workflow/WorkflowExecutionRunner.cs
git commit -m "feat: extract WorkflowExecutionRunner — shared step loop with state machine + WaitingHumanTaskId"
```

---

### Task 7: Refactor WorkflowEngine to delegate to WorkflowExecutionRunner

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/WorkflowEngine.cs`

The engine now handles: descriptor resolution, instance creation, `workflow.started`/terminal event publishing, and error wrapping. The runner handles: step iteration, executor dispatch, state transitions, persistence (SaveAsync called by runner at transitions).

- [ ] **Step 1: Replace WorkflowEngine entirely**

```csharp
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    public WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher)
    {
        _registry = registry;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
    }

    public async Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default)
    {
        var descriptor = _registry.GetById(workflowId);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{workflowId}' not found.");

        var instance = new WorkflowInstance
        {
            Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(descriptor.Id, descriptor.Version)
        };

        if (inputVariables != null)
        {
            foreach (var kv in inputVariables)
                instance.Variables[kv.Key] = kv.Value;
        }

        // Publish started before execution
        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = "workflow.started",
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            Status = WorkflowInstanceStatus.Running
        }, ct).ConfigureAwait(false);

        // Delegate execution to shared runner
        try
        {
            instance = await _executionRunner.RunAsync(instance, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Catastrophic failure (e.g. store save threw)
            await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
            {
                EventType = "workflow.failed",
                WorkflowInstanceId = instance.InstanceId,
                WorkflowId = descriptor.Id,
                Status = WorkflowInstanceStatus.Failed,
                Payload = new { Error = ex.Message }
            }, ct).ConfigureAwait(false);
            throw;
        }

        // Publish terminal event based on final status
        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = instance.Status switch
            {
                WorkflowInstanceStatus.Completed => "workflow.completed",
                WorkflowInstanceStatus.Failed => "workflow.failed",
                WorkflowInstanceStatus.Suspended => "workflow.suspended",
                _ => instance.Status.ToString()
            },
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            Status = instance.Status
        }, ct).ConfigureAwait(false);

        return instance;
    }
}
```

- [ ] **Step 2: Build — now engine + runner coexist. Engine no longer has `ExecuteStepsAsync`.**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded. `WorkflowEngineTests` will have compile errors — `CreateEngine` helper needs updating. Fixed in Task 11.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowEngine.cs
git commit -m "refactor: WorkflowEngine delegates to WorkflowExecutionRunner + lifecycle events"
```

---

### Task 8: Implement WorkflowContinuationService

**Files:**
- Create: `framework/src/CrestCreates.Workflow/WorkflowContinuationService.cs`

- [ ] **Step 1: Implement the continuation service**

```csharp
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowContinuationService : IWorkflowContinuationService
{
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    public WorkflowContinuationService(
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher)
    {
        _store = store;
        _stateMachine = stateMachine;
        _executionRunner = executionRunner;
        _eventPublisher = eventPublisher;
    }

    public async Task ContinueAsync(
        string humanTaskId,
        object? result,
        CancellationToken ct = default)
    {
        var instance = await _store.GetByWaitingHumanTaskId(humanTaskId, ct)
            .ConfigureAwait(false);
        if (instance == null)
            throw new InvalidOperationException(
                $"No suspended workflow instance waiting for HumanTask '{humanTaskId}'.");

        _stateMachine.ValidateTransition(WorkflowInstanceStatus.Suspended,
            WorkflowInstanceStatus.Running);

        instance.Variables["stepResult"] = result;
        instance.StepIndex++;                         // skip past HumanTask step
        instance.WaitingHumanTaskId = null;
        instance.Status = WorkflowInstanceStatus.Running;
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);

        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = "workflow.resumed",
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = instance.Workflow.Id,
            Status = WorkflowInstanceStatus.Running
        }, ct).ConfigureAwait(false);

        instance = await _executionRunner.RunAsync(instance, ct).ConfigureAwait(false);

        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = instance.Status switch
            {
                WorkflowInstanceStatus.Completed => "workflow.completed",
                WorkflowInstanceStatus.Failed => "workflow.failed",
                _ => instance.Status.ToString()
            },
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = instance.Workflow.Id,
            Status = instance.Status
        }, ct).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowContinuationService.cs
git commit -m "feat: implement WorkflowContinuationService — load, validate, advance cursor, resume"
```

---

### Task 9: Extend InMemoryWorkflowInstanceStore

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs`

- [ ] **Step 1: Add `GetByWaitingHumanTaskId` implementation**

Add after `GetAsync`:
```csharp
public Task<WorkflowInstance?> GetByWaitingHumanTaskId(
    string humanTaskId, CancellationToken ct = default)
{
    var match = _instances.Values
        .FirstOrDefault(i => i.WaitingHumanTaskId == humanTaskId);
    return Task.FromResult(match);
}
```

Add `using System.Linq;` to the imports.

- [ ] **Step 2: Build**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs
git commit -m "feat: InMemoryWorkflowInstanceStore.GetByWaitingHumanTaskId"
```

---

### Task 10: Update DI registration

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs`

- [ ] **Step 1: Add Phase 4c registrations**

Current registrations (Phase 4b):
```csharp
services.TryAddSingleton<IWorkflowEngine, WorkflowEngine>();
services.TryAddSingleton<IWorkflowInstanceStore, InMemoryWorkflowInstanceStore>();
services.TryAddSingleton<CapabilityStepExecutor>();
services.TryAddSingleton<HumanTaskStepExecutor>();
services.TryAddSingleton<IWorkflowStepExecutorRegistry, DefaultStepExecutorRegistry>();
services.TryAddSingleton<WorkflowCompatibilityValidator>();
```

Add after `WorkflowCompatibilityValidator`:
```csharp
// Phase 4c
services.TryAddSingleton<IWorkflowStateMachine, DefaultWorkflowStateMachine>();
services.TryAddSingleton<IWorkflowLifecycleEventPublisher, WorkflowLifecycleEventPublisher>();
services.TryAddSingleton<IWorkflowExecutionRunner, WorkflowExecutionRunner>();
services.TryAddSingleton<IWorkflowContinuationService, WorkflowContinuationService>();
```

- [ ] **Step 2: Build**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs
git commit -m "refactor: register Phase 4c types in DI — stateMachine, eventPublisher, executionRunner, continuationService"
```

---

### Task 11: Fix existing test helper — update CreateEngine

**Files:**
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs`
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowRuntimeTests.cs`

The `CreateEngine` helper in both test files must be updated to construct `WorkflowEngine` with its new constructor signature.

- [ ] **Step 1: Update CreateEngine in WorkflowEngineTests.cs**

Replace the existing `CreateEngine` method:
```csharp
private static WorkflowEngine CreateEngine(
    WorkflowRegistry registry,
    ICapabilityPipeline? pipeline = null)
{
    var pipelineImpl = pipeline ?? new MockCapabilityPipeline(
        CapabilityExecutionResult.Success(null, TimeSpan.Zero));
    var capExecutor = new CapabilityStepExecutor(pipelineImpl);
    var htExecutor = new HumanTaskStepExecutor();
    var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
    var store = new InMemoryWorkflowInstanceStore();
    return new WorkflowEngine(registry, executorRegistry, store);
}
```

Replace with (constructor now takes `registry, executionRunner, eventPublisher`):
```csharp
private static WorkflowEngine CreateEngine(
    WorkflowRegistry registry,
    ICapabilityPipeline? pipeline = null)
{
    var pipelineImpl = pipeline ?? new MockCapabilityPipeline(
        CapabilityExecutionResult.Success(null, TimeSpan.Zero));
    var capExecutor = new CapabilityStepExecutor(pipelineImpl);
    var htExecutor = new HumanTaskStepExecutor();
    var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
    var store = new InMemoryWorkflowInstanceStore();
    var stateMachine = new DefaultWorkflowStateMachine();
    var eventPublisher = new WorkflowLifecycleEventPublisher();
    var executionRunner = new WorkflowExecutionRunner(registry, executorRegistry, store, stateMachine);
    return new WorkflowEngine(registry, executionRunner, eventPublisher);
}
```

- [ ] **Step 2: Apply the same change to WorkflowRuntimeTests.cs**

Replace the same `CreateEngine` helper with the updated version.

- [ ] **Step 3: Run existing tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: All 37 existing tests PASS (new registrations don't break anything).

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs \
        framework/test/CrestCreates.Workflow.Tests/WorkflowRuntimeTests.cs
git commit -m "test: update CreateEngine helpers for Phase 4c constructor (executionRunner + eventPublisher)"
```

---

### Task 12: Write WorkflowContinuation integration tests

**Files:**
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs`

- [ ] **Step 1: Create test file — Case 1 (full closed loop)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowContinuationTests
{
    private static WorkflowRegistry CreateRegistry(params WorkflowDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<WorkflowDescriptor>(
            Array.Empty<IRegistryValidator<WorkflowDescriptor>>());
        var registry = new WorkflowRegistry(engine);
        registry.Build([new TestWorkflowProvider(descriptors.ToList())]);
        return registry;
    }

    private class TestWorkflowProvider : IDescriptorProvider<WorkflowDescriptor>
    {
        private readonly List<WorkflowDescriptor> _descriptors;
        public TestWorkflowProvider(List<WorkflowDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<WorkflowDescriptor> GetDescriptors() => _descriptors;
    }

    private class MockCapabilityPipeline : ICapabilityPipeline
    {
        private readonly CapabilityExecutionResult _result;
        public MockCapabilityPipeline(CapabilityExecutionResult result) => _result = result;
        public Task<CapabilityExecutionResult> ExecuteAsync(
            string capabilityIdOrName, object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => Task.FromResult(_result);
    }
```

- [ ] **Step 2: Case 1 — Full closed loop (Capability → HumanTask → Continue → Capability → Completed)**

```csharp
    [Fact]
    public async Task FullLoop_ExecuteSuspendContinue_CompletesSuccessfully()
    {
        var pipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(
                new Dictionary<string, object?> { ["output_key"] = "output_val" },
                TimeSpan.Zero));

        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "loop.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Cap A",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } },
                new() { Id = "step_02", Name = "Approval",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } },
                new() { Id = "step_03", Name = "Cap B",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1) } }
            }
        });

        var store = new InMemoryWorkflowInstanceStore();
        var stateMachine = new DefaultWorkflowStateMachine();
        var eventPublisher = new WorkflowLifecycleEventPublisher();
        var capExecutor = new CapabilityStepExecutor(pipeline);
        var htExecutor = new HumanTaskStepExecutor();
        var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
        var executionRunner = new WorkflowExecutionRunner(registry, executorRegistry, store, stateMachine);
        var engine = new WorkflowEngine(registry, executionRunner, eventPublisher);
        var continuation = new WorkflowContinuationService(store, stateMachine, executionRunner, eventPublisher);

        // Execute: Step1 completes, Step2 suspends
        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.WaitingHumanTaskId.Should().Be("ht_01");
        instance.StepIndex.Should().Be(1);  // cursor at HumanTask step
        instance.StepResults.Should().HaveCount(2);

        // Continue: skip Step2, execute Step3
        await continuation.ContinueAsync("ht_01", new { Approved = true });

        var resumed = await store.GetAsync(instance.InstanceId);
        resumed!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        resumed.WaitingHumanTaskId.Should().BeNull();
        resumed.StepResults.Should().HaveCount(3);
        resumed.StepResults[2].Status.Should().Be(StepExecutionStatus.Completed);
    }
```

- [ ] **Step 3: Case 2 — Double resume**

```csharp
    [Fact]
    public async Task ContinueAsync_DoubleResume_ThrowsOnSecondCall()
    {
        var pipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "double.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
            }
        });

        var store = new InMemoryWorkflowInstanceStore();
        var stateMachine = new DefaultWorkflowStateMachine();
        var eventPublisher = new WorkflowLifecycleEventPublisher();
        var capExecutor = new CapabilityStepExecutor(pipeline);
        var htExecutor = new HumanTaskStepExecutor();
        var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
        var executionRunner = new WorkflowExecutionRunner(registry, executorRegistry, store, stateMachine);
        var engine = new WorkflowEngine(registry, executionRunner, eventPublisher);
        var continuation = new WorkflowContinuationService(store, stateMachine, executionRunner, eventPublisher);

        await engine.ExecuteAsync("wf_01");
        await continuation.ContinueAsync("ht_01", null);

        // Second call: WaitingHumanTaskId already cleared → null
        await continuation.Invoking(c => c.ContinueAsync("ht_01", null))
            .Should().ThrowAsync<InvalidOperationException>();
    }
```

- [ ] **Step 4: Case 3 — stepResult variable propagated**

```csharp
    [Fact]
    public async Task ContinueAsync_StepResultVariable_AvailableToDownstream()
    {
        var pipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "vars.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } },
                new() { Id = "step_02", Name = "Cap",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } }
            }
        });

        var store = new InMemoryWorkflowInstanceStore();
        var stateMachine = new DefaultWorkflowStateMachine();
        var eventPublisher = new WorkflowLifecycleEventPublisher();
        var capExecutor = new CapabilityStepExecutor(pipeline);
        var htExecutor = new HumanTaskStepExecutor();
        var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
        var executionRunner = new WorkflowExecutionRunner(registry, executorRegistry, store, stateMachine);
        var engine = new WorkflowEngine(registry, executionRunner, eventPublisher);
        var continuation = new WorkflowContinuationService(store, stateMachine, executionRunner, eventPublisher);

        await engine.ExecuteAsync("wf_01");
        await continuation.ContinueAsync("ht_01", new { Approved = true, Score = 95 });

        var instance = await store.GetByWaitingHumanTaskId("ht_01");
        instance.Should().BeNull(); // cleared
        var final = await store.GetAsync((await store.GetAsync(
            (await engine.ExecuteAsync("wf_01")).InstanceId))!.InstanceId);
        // Better: get the instance ID from the result
        var instanceId = (await engine.ExecuteAsync("wf_01")).InstanceId;
        // Already suspended. Use a fresh approach:
    }
```

Wait, this test is awkward because `ExecuteAsync` returns the suspended instance but then `ContinueAsync` on a different instance. Let me restructure:

```csharp
    [Fact]
    public async Task ContinueAsync_StepResultVariable_PropagatedToInstance()
    {
        var pipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "vars.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
            }
        });

        var store = new InMemoryWorkflowInstanceStore();
        var stateMachine = new DefaultWorkflowStateMachine();
        var eventPublisher = new WorkflowLifecycleEventPublisher();
        var capExecutor = new CapabilityStepExecutor(pipeline);
        var htExecutor = new HumanTaskStepExecutor();
        var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
        var executionRunner = new WorkflowExecutionRunner(registry, executorRegistry, store, stateMachine);
        var engine = new WorkflowEngine(registry, executionRunner, eventPublisher);
        var continuation = new WorkflowContinuationService(store, stateMachine, executionRunner, eventPublisher);

        var instance = await engine.ExecuteAsync("wf_01");
        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);

        await continuation.ContinueAsync("ht_01", new { Approved = true, Score = 95 });

        var final = await store.GetAsync(instance.InstanceId);
        final.Should().NotBeNull();
        final!.WaitingHumanTaskId.Should().BeNull();
        var stepResult = final.Variables["stepResult"];
        stepResult.Should().NotBeNull();
    }
```

- [ ] **Step 5: Case 4 — Invalid state transition during ContinueAsync**

```csharp
    [Fact]
    public async Task ContinueAsync_NotSuspended_ThrowsInvalidTransition()
    {
        var pipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "invalid.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Done",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } }
            }
        });

        var store = new InMemoryWorkflowInstanceStore();
        var stateMachine = new DefaultWorkflowStateMachine();
        var eventPublisher = new WorkflowLifecycleEventPublisher();
        var capExecutor = new CapabilityStepExecutor(pipeline);
        var htExecutor = new HumanTaskStepExecutor();
        var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
        var executionRunner = new WorkflowExecutionRunner(registry, executorRegistry, store, stateMachine);
        var engine = new WorkflowEngine(registry, executionRunner, eventPublisher);
        var continuation = new WorkflowContinuationService(store, stateMachine, executionRunner, eventPublisher);

        var instance = await engine.ExecuteAsync("wf_01");
        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);

        // Manually set WaitingHumanTaskId to simulate a race condition, then try to continue
        instance.WaitingHumanTaskId = "ht_01";
        instance.Status = WorkflowInstanceStatus.Completed; // not Suspended!
        await store.SaveAsync(instance);

        await continuation.Invoking(c => c.ContinueAsync("ht_01", null))
            .Should().ThrowAsync<InvalidWorkflowTransitionException>();
    }
```

Actually this test is fragile — it depends on manually manipulating the instance state. Let me simplify: test that `GetByWaitingHumanTaskId` returns null for a completed instance:

```csharp
    [Fact]
    public async Task ContinueAsync_NonExistentHumanTaskId_Throws()
    {
        // No instance has WaitingHumanTaskId = "nonexistent"
        var store = new InMemoryWorkflowInstanceStore();
        var stateMachine = new DefaultWorkflowStateMachine();
        var eventPublisher = new WorkflowLifecycleEventPublisher();
        var continuation = new WorkflowContinuationService(store, stateMachine, null!, eventPublisher);

        await continuation.Invoking(c => c.ContinueAsync("nonexistent", null))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No suspended workflow instance*");
    }
```

This is cleaner. Let me include this instead.

- [ ] **Step 6: Run tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj --filter "FullyQualifiedName~WorkflowContinuationTests"`
Expected: All continuation tests PASS.

- [ ] **Step 7: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs
git commit -m "test: WorkflowContinuation integration tests — full loop, double resume, stepResult, not found"
```

---

### Task 13: Run all tests + final verification

**Files:**
- All

- [ ] **Step 1: Run all Workflow tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: ALL tests PASS (existing 37 + new continuation tests).

- [ ] **Step 2: Run full framework tests**

Run: `dotnet build && dotnet test framework/test/CrestCreates.Workflow.Tests/`
Expected: No regressions.

- [ ] **Step 3: LSP diagnostics**

Check `WorkflowEngine.cs`, `WorkflowContinuationService.cs`, `WorkflowExecutionRunner.cs` — no errors.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "Phase 4c: Workflow Runtime Closure — complete"
```
