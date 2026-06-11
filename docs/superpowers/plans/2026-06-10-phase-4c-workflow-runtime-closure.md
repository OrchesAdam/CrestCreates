# Phase 4c — Workflow Runtime Closure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the Workflow Runtime execution loop — HumanTask completion triggers automatic workflow continuation via `IWorkflowContinuationService` using `WorkflowContinuationRequest`, with lifecycle events and shared execution core.

**Architecture:** Extract `IWorkflowExecutionRunner` from engine's step loop (owns step loop + persistence + suspended/completed/failed events). Engine constructor is `internal` (factory-registered via DI). `WorkflowContinuationService` writes HumanTask `StepResult`, advances cursor, publishes `workflow.resumed`, then delegates to Runner. `HumanTaskCompletedWorkflowSubscriber` bridges `HumanTaskCompletedEvent` → `ContinueAsync`. No `ResumeAsync` reintroduced.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions

---

## File Map

| File | Action | Project |
|------|--------|---------|
| `IWorkflowStateMachine.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `InvalidWorkflowTransitionException.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `WorkflowLifecycleEvent.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowLifecycleEventPublisher.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `WorkflowContinuationRequest.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowContinuationService.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `WorkflowCorrelationException.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowInstanceStore.cs` | Modify | `CrestCreates.Workflow.Abstractions` |
| `WorkflowInstance.cs` | Modify | `CrestCreates.Workflow.Abstractions` |
| `StepExecutionResult.cs` | Modify | `CrestCreates.Workflow.Abstractions` |
| `HumanTaskCompletedEvent.cs` | Create | `CrestCreates.HumanTask.Abstractions` |
| `IWorkflowExecutionRunner.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowExecutionRunner.cs` | Create | `CrestCreates.Workflow` |
| `DefaultWorkflowStateMachine.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowLifecycleEventPublisher.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowContinuationService.cs` | Create | `CrestCreates.Workflow` |
| `HumanTaskCompletedWorkflowSubscriber.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowEngine.cs` | Modify | `CrestCreates.Workflow` |
| `InMemoryWorkflowInstanceStore.cs` | Modify | `CrestCreates.Workflow` |
| `HumanTaskStepExecutor.cs` | Modify | `CrestCreates.Workflow` |
| `WorkflowServiceCollectionExtensions.cs` | Modify | `CrestCreates.Workflow` |
| `WorkflowStateMachineTests.cs` | Create | `CrestCreates.Workflow.Tests` |
| `WorkflowContinuationTests.cs` | Create | `CrestCreates.Workflow.Tests` |
| `WorkflowEngineTests.cs` | Modify | `CrestCreates.Workflow.Tests` |
| `WorkflowRuntimeTests.cs` | Modify | `CrestCreates.Workflow.Tests` |

---

### Task 1: Create IWorkflowStateMachine + InvalidWorkflowTransitionException + DefaultWorkflowStateMachine

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStateMachine.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/InvalidWorkflowTransitionException.cs`
- Create: `framework/src/CrestCreates.Workflow/DefaultWorkflowStateMachine.cs`

- [ ] **Step 1: Create all three files**

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

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStateMachine.cs
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowStateMachine
{
    void ValidateTransition(WorkflowInstanceStatus from, WorkflowInstanceStatus to);
}
```

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

- [ ] **Step 2: Build and commit**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStateMachine.cs \
        framework/src/CrestCreates.Workflow.Abstractions/InvalidWorkflowTransitionException.cs \
        framework/src/CrestCreates.Workflow/DefaultWorkflowStateMachine.cs
git commit -m "feat: IWorkflowStateMachine + InvalidWorkflowTransitionException + DefaultWorkflowStateMachine"
```

---

### Task 2: Create WorkflowLifecycleEvent + publisher contracts + no-op publisher

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowLifecycleEvent.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowLifecycleEventPublisher.cs`
- Create: `framework/src/CrestCreates.Workflow/WorkflowLifecycleEventPublisher.cs`

- [ ] **Step 1: Create all three files**

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

### Task 3: Create WorkflowContinuationRequest, IWorkflowContinuationService, WorkflowCorrelationException, HumanTaskCompletedEvent

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowContinuationRequest.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowContinuationService.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowCorrelationException.cs`
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs`

- [ ] **Step 1: Create all four files**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/WorkflowContinuationRequest.cs
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowContinuationRequest
{
    public string HumanTaskId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
```

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowContinuationService.cs
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowContinuationService
{
    Task ContinueAsync(WorkflowContinuationRequest request, CancellationToken ct = default);
}
```

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/WorkflowCorrelationException.cs
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowCorrelationException : Exception
{
    public WorkflowCorrelationException(string message) : base(message) { }
}
```

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

- [ ] **Step 2: Build and commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowContinuationRequest.cs \
        framework/src/CrestCreates.Workflow.Abstractions/IWorkflowContinuationService.cs \
        framework/src/CrestCreates.Workflow.Abstractions/WorkflowCorrelationException.cs \
        framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs
git commit -m "feat: WorkflowContinuationRequest + IWorkflowContinuationService + WorkflowCorrelationException + HumanTaskCompletedEvent"
```

---

### Task 4: Extend existing types — WorkflowInstance, IWorkflowInstanceStore, StepExecutionResult

**Files:**
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs`
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs`
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/StepExecutionResult.cs`

- [ ] **Step 1: Add `WaitingHumanTaskId` to WorkflowInstance**

After `public int StepIndex { get; set; }` (line 11), add:
```csharp
    public string? WaitingHumanTaskId { get; set; }
```

- [ ] **Step 2: Add `GetByWaitingHumanTaskId` to IWorkflowInstanceStore**

After `GetAsync`, add:
```csharp
    Task<WorkflowInstance?> GetByWaitingHumanTaskId(
        string humanTaskId, CancellationToken ct = default);
```

- [ ] **Step 3: Add `WaitingHumanTaskId` parameter to StepExecutionResult**

Replace current content:
```csharp
public sealed record StepExecutionResult(
    StepExecutionStatus Status,
    object? Output = null,
    IReadOnlyDictionary<string, object?>? Variables = null,
    string? WaitingHumanTaskId = null);
```

- [ ] **Step 4: Build — InMemoryWorkflowInstanceStore will have compile error (expected)**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Abstractions project builds. Workflow.csproj errors on `InMemoryWorkflowInstanceStore` missing method — fixed in Task 10.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowInstance.cs \
        framework/src/CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs \
        framework/src/CrestCreates.Workflow.Abstractions/StepExecutionResult.cs
git commit -m "feat: add WaitingHumanTaskId to WorkflowInstance, store, and StepExecutionResult"
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

- [ ] **Step 2: Run tests — should PASS (DefaultWorkflowStateMachine already exists)**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj --filter "FullyQualifiedName~WorkflowStateMachineTests"`
Expected: All 7 PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowStateMachineTests.cs
git commit -m "test: add WorkflowStateMachine tests — 4 valid + 3 invalid"
```

---

### Task 6: Create IWorkflowExecutionRunner + WorkflowExecutionRunner

**Files:**
- Create: `framework/src/CrestCreates.Workflow/IWorkflowExecutionRunner.cs`
- Create: `framework/src/CrestCreates.Workflow/WorkflowExecutionRunner.cs`

Runner owns step loop + persistence + suspended/completed/failed events.

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

- [ ] **Step 2: Implement the Runner** (full step loop with state machine + event publishing)

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
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    public WorkflowExecutionRunner(
        IWorkflowRegistry registry,
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store,
        IWorkflowStateMachine stateMachine,
        IWorkflowLifecycleEventPublisher eventPublisher)
    {
        _registry = registry;
        _executorRegistry = executorRegistry;
        _store = store;
        _stateMachine = stateMachine;
        _eventPublisher = eventPublisher;
    }

    public async Task<WorkflowInstance> RunAsync(WorkflowInstance instance, CancellationToken ct)
    {
        var descriptor = _registry.GetById(instance.Workflow.Id);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{instance.Workflow.Id}' not found.");

        return await ExecuteStepsAsync(instance, descriptor, ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteStepsAsync(
        WorkflowInstance instance, WorkflowDescriptor descriptor, CancellationToken ct)
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
                await PublishEvent("workflow.failed", instance, descriptor.Id, ct).ConfigureAwait(false);
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
                    if (string.IsNullOrWhiteSpace(stepResult.WaitingHumanTaskId))
                        throw new InvalidOperationException(
                            "Suspended HumanTask step must provide WaitingHumanTaskId.");
                    instance.WaitingHumanTaskId = stepResult.WaitingHumanTaskId;
                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Suspended);
                    instance.Status = WorkflowInstanceStatus.Suspended;
                    instance.CurrentStepId = null;
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    await PublishEvent("workflow.suspended", instance, descriptor.Id, ct).ConfigureAwait(false);
                    return instance;

                case StepExecutionStatus.Failed:
                    if (step.OnError == StepErrorBehavior.Skip)
                    { instance.StepIndex++; continue; }
                    _stateMachine.ValidateTransition(instance.Status, WorkflowInstanceStatus.Failed);
                    instance.Status = WorkflowInstanceStatus.Failed;
                    instance.ErrorMessage = $"Step '{step.Id}' failed.";
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    await PublishEvent("workflow.failed", instance, descriptor.Id, ct).ConfigureAwait(false);
                    return instance;
            }
        }

        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = DateTimeOffset.UtcNow;
        instance.CurrentStepId = null;
        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        await PublishEvent("workflow.completed", instance, descriptor.Id, ct).ConfigureAwait(false);
        return instance;
    }

    private Task PublishEvent(string eventType, WorkflowInstance instance,
        string workflowId, CancellationToken ct)
    {
        return _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = eventType,
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = workflowId,
            Status = instance.Status
        }, ct);
    }
}
```

- [ ] **Step 3: Build** — runner compiles independently. Engine still has its own `ExecuteStepsAsync`, no conflict.

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow/IWorkflowExecutionRunner.cs \
        framework/src/CrestCreates.Workflow/WorkflowExecutionRunner.cs
git commit -m "feat: extract WorkflowExecutionRunner — owns step loop, persistence, suspended/completed/failed events"
```

---

### Task 7: Refactor WorkflowEngine — internal constructor, delegates to Runner

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/WorkflowEngine.cs`

- [ ] **Step 1: Replace WorkflowEngine entirely**

```csharp
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    internal WorkflowEngine(
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

        await _store.SaveAsync(instance, ct).ConfigureAwait(false);

        await _eventPublisher.PublishAsync(new WorkflowLifecycleEvent
        {
            EventType = "workflow.started",
            WorkflowInstanceId = instance.InstanceId,
            WorkflowId = descriptor.Id,
            Status = WorkflowInstanceStatus.Running
        }, ct).ConfigureAwait(false);

        return await _executionRunner.RunAsync(instance, ct).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Build** — engine delegates to Runner. Tests will have compile errors (`CreateEngine` uses old constructor).

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowEngine.cs
git commit -m "refactor: WorkflowEngine internal constructor, delegates to WorkflowExecutionRunner"
```

---

### Task 8: Update HumanTaskStepExecutor to return WaitingHumanTaskId

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/HumanTaskStepExecutor.cs`

- [ ] **Step 1: Update the executor**

```csharp
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class HumanTaskStepExecutor : IWorkflowStepExecutor
{
    public Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        var target = (HumanTaskTarget)context.Step.Target;
        return Task.FromResult(
            new StepExecutionResult(
                StepExecutionStatus.Suspended,
                WaitingHumanTaskId: target.HumanTask.Id));
    }
}
```

- [ ] **Step 2: Build and commit**

```bash
git add framework/src/CrestCreates.Workflow/HumanTaskStepExecutor.cs
git commit -m "feat: HumanTaskStepExecutor returns WaitingHumanTaskId in StepExecutionResult"
```

---

### Task 9: Implement WorkflowContinuationService + HumanTaskCompletedWorkflowSubscriber

**Files:**
- Create: `framework/src/CrestCreates.Workflow/WorkflowContinuationService.cs`
- Create: `framework/src/CrestCreates.Workflow/HumanTaskCompletedWorkflowSubscriber.cs`

- [ ] **Step 1: Implement WorkflowContinuationService**

```csharp
// framework/src/CrestCreates.Workflow/WorkflowContinuationService.cs
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
        if (instance == null)
            throw new InvalidOperationException(
                $"No suspended workflow instance waiting for HumanTask '{request.HumanTaskId}'.");

        if (instance.Status != WorkflowInstanceStatus.Suspended)
            throw new InvalidOperationException(
                $"Instance '{instance.InstanceId}' is not Suspended (status: {instance.Status}).");

        _stateMachine.ValidateTransition(
            WorkflowInstanceStatus.Suspended, WorkflowInstanceStatus.Running);

        var descriptor = _registry.GetById(instance.Workflow.Id);
        if (descriptor == null)
            throw new InvalidOperationException($"Workflow '{instance.Workflow.Id}' not found.");

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

- [ ] **Step 2: Implement HumanTaskCompletedWorkflowSubscriber**

```csharp
// framework/src/CrestCreates.Workflow/HumanTaskCompletedWorkflowSubscriber.cs
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

internal sealed class HumanTaskCompletedWorkflowSubscriber
    : ILocalEventHandler<HumanTaskCompletedEvent>
{
    private readonly IWorkflowContinuationService _continuationService;

    public HumanTaskCompletedWorkflowSubscriber(
        IWorkflowContinuationService continuationService)
    {
        _continuationService = continuationService;
    }

    public Task HandleAsync(HumanTaskCompletedEvent evt, CancellationToken ct)
    {
        return _continuationService.ContinueAsync(
            new WorkflowContinuationRequest
            {
                HumanTaskId = evt.HumanTaskId,
                Outcome = evt.Outcome,
                Result = evt.Result
            }, ct);
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded. If `EventBus.Abstractions` reference is missing, add: `<ProjectReference Include="..\CrestCreates.EventBus.Abstractions\CrestCreates.EventBus.Abstractions.csproj" />` to `Workflow.csproj`.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowContinuationService.cs \
        framework/src/CrestCreates.Workflow/HumanTaskCompletedWorkflowSubscriber.cs
git commit -m "feat: WorkflowContinuationService + HumanTaskCompletedWorkflowSubscriber"
```

---

### Task 10: Extend InMemoryWorkflowInstanceStore + Update DI

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs`
- Modify: `framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs`

- [ ] **Step 1: Add `GetByWaitingHumanTaskId` with uniqueness + suspended-only**

Add `using System.Linq;` and this method after `GetAsync`:
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

    return Task.FromResult(matches.SingleOrDefault());
}
```

- [ ] **Step 2: Replace DI registration**

```csharp
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Workflow;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.TryAddSingleton<IWorkflowInstanceStore, InMemoryWorkflowInstanceStore>();
        services.TryAddSingleton<CapabilityStepExecutor>();
        services.TryAddSingleton<HumanTaskStepExecutor>();
        services.TryAddSingleton<IWorkflowStepExecutorRegistry, DefaultStepExecutorRegistry>();
        services.TryAddSingleton<WorkflowCompatibilityValidator>();

        services.TryAddSingleton<IWorkflowStateMachine, DefaultWorkflowStateMachine>();
        services.TryAddSingleton<IWorkflowLifecycleEventPublisher, WorkflowLifecycleEventPublisher>();
        services.TryAddSingleton<IWorkflowExecutionRunner, WorkflowExecutionRunner>();
        services.TryAddSingleton<IWorkflowContinuationService, WorkflowContinuationService>();

        services.TryAddSingleton<IWorkflowEngine>(sp =>
            new WorkflowEngine(
                sp.GetRequiredService<IWorkflowRegistry>(),
                sp.GetRequiredService<IWorkflowInstanceStore>(),
                sp.GetRequiredService<IWorkflowExecutionRunner>(),
                sp.GetRequiredService<IWorkflowLifecycleEventPublisher>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            ILocalEventHandler<HumanTaskCompletedEvent>,
            HumanTaskCompletedWorkflowSubscriber>());

        return services;
    }
}
```

- [ ] **Step 3: Check EventBus.Abstractions reference in Workflow.csproj**

If missing, add: `<ProjectReference Include="..\CrestCreates.EventBus.Abstractions\CrestCreates.EventBus.Abstractions.csproj" />`

- [ ] **Step 4: Build**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs \
        framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs
git commit -m "feat: InMemoryWorkflowInstanceStore.GetByWaitingHumanTaskId + Phase 4c DI"
```

---

### Task 11: Update test helpers — CreateEngine for new constructors

**Files:**
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs`
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowRuntimeTests.cs`

- [ ] **Step 1: Replace `CreateEngine` in both test files**

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
    var executionRunner = new WorkflowExecutionRunner(
        registry, executorRegistry, store, stateMachine, eventPublisher);
    return new WorkflowEngine(registry, store, executionRunner, eventPublisher);
}
```

- [ ] **Step 2: Run existing tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: All 37 existing tests PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs \
        framework/test/CrestCreates.Workflow.Tests/WorkflowRuntimeTests.cs
git commit -m "test: update CreateEngine helpers for Phase 4c constructors"
```

---

### Task 12: Write WorkflowContinuation integration tests

**Files:**
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs`

- [ ] **Step 1: Create test infrastructure helpers**

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

    private static (WorkflowEngine engine, WorkflowContinuationService continuation,
        InMemoryWorkflowInstanceStore store) CreateServices(
        WorkflowRegistry registry, ICapabilityPipeline? pipeline = null)
    {
        var pipelineImpl = pipeline ?? new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(null, TimeSpan.Zero));
        var capExecutor = new CapabilityStepExecutor(pipelineImpl);
        var htExecutor = new HumanTaskStepExecutor();
        var executorRegistry = new DefaultStepExecutorRegistry(capExecutor, htExecutor);
        var store = new InMemoryWorkflowInstanceStore();
        var stateMachine = new DefaultWorkflowStateMachine();
        var eventPublisher = new WorkflowLifecycleEventPublisher();
        var executionRunner = new WorkflowExecutionRunner(
            registry, executorRegistry, store, stateMachine, eventPublisher);
        var engine = new WorkflowEngine(registry, store, executionRunner, eventPublisher);
        var continuation = new WorkflowContinuationService(
            store, stateMachine, registry, executionRunner, eventPublisher);
        return (engine, continuation, store);
    }
```

- [ ] **Step 2: Case 1 — Full closed loop**

```csharp
    [Fact]
    public async Task FullLoop_ExecuteSuspendContinue_CompletesSuccessfully()
    {
        var pipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Success(
                new Dictionary<string, object?> { ["output_key"] = "output_val" }, TimeSpan.Zero));
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
        var (engine, continuation, store) = CreateServices(registry, pipeline);

        var instance = await engine.ExecuteAsync("wf_01");
        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.WaitingHumanTaskId.Should().Be("ht_01");
        instance.StepIndex.Should().Be(1);
        instance.StepResults.Should().HaveCount(2);

        await continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = "ht_01", Outcome = "Approved", Result = new { Score = 95 } });

        var final = await store.GetAsync(instance.InstanceId);
        final!.Status.Should().Be(WorkflowInstanceStatus.Completed);
        final.WaitingHumanTaskId.Should().BeNull();
        final.StepResults.Should().HaveCount(3);
        final.StepResults[2].Status.Should().Be(StepExecutionStatus.Completed);
    }
```

- [ ] **Step 3: Case 2 — Double resume**

```csharp
    [Fact]
    public async Task ContinueAsync_DoubleResume_ThrowsOnSecondCall()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "double.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
            }
        });
        var (engine, continuation, _) = CreateServices(registry);

        await engine.ExecuteAsync("wf_01");
        await continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = "ht_01", Outcome = "ok" });

        await continuation.Invoking(c => c.ContinueAsync(new WorkflowContinuationRequest
                { HumanTaskId = "ht_01", Outcome = "ok" }))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No suspended workflow instance*");
    }
```

- [ ] **Step 4: Case 3 — Variables propagated + HumanTask StepResult written**

```csharp
    [Fact]
    public async Task ContinueAsync_VariablesAndStepResult_Propagated()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "vars.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Approval",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
            }
        });
        var (engine, continuation, store) = CreateServices(registry);

        var instance = await engine.ExecuteAsync("wf_01");
        await continuation.ContinueAsync(new WorkflowContinuationRequest
            { HumanTaskId = "ht_01", Outcome = "Approved", Result = new { Score = 95 } });

        var final = await store.GetAsync(instance.InstanceId);
        final!.Variables["lastStepOutcome"].Should().Be("Approved");
        final.StepResults.Should().HaveCount(1);
        final.StepResults[0].Status.Should().Be(StepExecutionStatus.Completed);
        final.StepResults[0].Output.Should().NotBeNull();
    }
```

- [ ] **Step 5: Run tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj --filter "FullyQualifiedName~WorkflowContinuationTests"`
Expected: All 3 PASS.

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs
git commit -m "test: WorkflowContinuation integration tests — full loop, double resume, variables + StepResult"
```

---

### Task 13: Run all tests + final verification

**Files:**
- All

- [ ] **Step 1: Run all Workflow tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: ALL tests PASS (~37 existing + 7 state machine + 3 continuation = ~47 total).

- [ ] **Step 2: Full build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "Phase 4c: Workflow Runtime Closure — complete"
```
