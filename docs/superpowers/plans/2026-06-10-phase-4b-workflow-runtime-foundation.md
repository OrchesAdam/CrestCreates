# Phase 4b — Workflow Runtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor `WorkflowEngine` from monolithic executor into a clean delegate-based architecture with `IWorkflowStepExecutor` registry, dedicated `IWorkflowInstanceStore`, and bootstrap validation.

**Architecture:** Internal refactoring — `IWorkflowEngine` remains the sole public contract (with `ResumeAsync` removed). The engine delegates step execution to `IWorkflowStepExecutorRegistry` which resolves `CapabilityStepExecutor` or `HumanTaskStepExecutor` by `InteractionTarget` subtype. All forbidden execution paths (SubWorkflow, Retry, Compensate, Transitions, Checkpoint) are removed; unsupported metadata is rejected at bootstrap.

**Tech Stack:** .NET 10, C#, xUnit, FluentAssertions

---

## File Map

| File | Action | Project |
|------|--------|---------|
| `StepExecutionStatus.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `StepExecutionResult.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowStepExecutor.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `WorkflowExecutionContext.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowStepExecutorRegistry.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowInstanceStore.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `UnsupportedWorkflowTargetException.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `WorkflowValidationException.cs` | Create | `CrestCreates.Workflow.Abstractions` |
| `WorkflowStepResult.cs` | Modify | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowEngine.cs` | Modify | `CrestCreates.Workflow.Abstractions` |
| `CapabilityStepExecutor.cs` | Create | `CrestCreates.Workflow` |
| `HumanTaskStepExecutor.cs` | Create | `CrestCreates.Workflow` |
| `DefaultStepExecutorRegistry.cs` | Create | `CrestCreates.Workflow` |
| `InMemoryWorkflowInstanceStore.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowCompatibilityValidator.cs` | Create | `CrestCreates.Workflow` |
| `WorkflowEngine.cs` | Modify | `CrestCreates.Workflow` |
| `WorkflowServiceCollectionExtensions.cs` | Modify | `CrestCreates.Workflow` |
| `MetadataBootstrapper.cs` | Modify | `CrestCreates.Metadata` |
| `WorkflowEngineTests.cs` | Modify | `CrestCreates.Workflow.Tests` |
| `WorkflowCompatibilityValidatorTests.cs` | Create | `CrestCreates.Workflow.Tests` |

---

### Task 1: Create StepExecutionStatus enum

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/StepExecutionStatus.cs`

- [ ] **Step 1: Create the enum**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/StepExecutionStatus.cs
namespace CrestCreates.Workflow.Abstractions;

public enum StepExecutionStatus
{
    Completed,
    Suspended,
    Failed
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/StepExecutionStatus.cs
git commit -m "feat: add StepExecutionStatus enum — Completed, Suspended, Failed"
```

---

### Task 2: Create StepExecutionResult record

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/StepExecutionResult.cs`

- [ ] **Step 1: Create the record**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/StepExecutionResult.cs
namespace CrestCreates.Workflow.Abstractions;

public sealed record StepExecutionResult(
    StepExecutionStatus Status,
    object? Output = null,
    IReadOnlyDictionary<string, object?>? Variables = null);
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/StepExecutionResult.cs
git commit -m "feat: add StepExecutionResult record — status, output, variable deltas"
```

---

### Task 3: Create IWorkflowStepExecutor interface

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStepExecutor.cs`

- [ ] **Step 1: Create the interface**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStepExecutor.cs
namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Executes a single workflow step. The executor:
/// - MUST return StepExecutionResult(Failed) for known business failures.
/// - MUST throw only for infrastructure/programming errors.
/// - MUST NOT access persistence or modify WorkflowInstance state.
/// </summary>
public interface IWorkflowStepExecutor
{
    Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken ct);
}
```

- [ ] **Step 2: Build to verify compilation**

Depends on `WorkflowExecutionContext` — this won't compile yet. We'll build after Task 4.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStepExecutor.cs
git commit -m "feat: add IWorkflowStepExecutor interface"
```

---

### Task 4: Create WorkflowExecutionContext class

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowExecutionContext.cs`

- [ ] **Step 1: Create the class**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/WorkflowExecutionContext.cs
namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Pure state transfer object. No IServiceProvider, no CancellationToken,
/// no persistence references. CancellationToken travels via ExecuteAsync(..., ct).
/// </summary>
public sealed class WorkflowExecutionContext
{
    public WorkflowDescriptor Workflow { get; }
    public WorkflowInstance Instance { get; }
    public WorkflowStep Step { get; }

    public WorkflowExecutionContext(
        WorkflowDescriptor workflow,
        WorkflowInstance instance,
        WorkflowStep step)
    {
        Workflow = workflow;
        Instance = instance;
        Step = step;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowExecutionContext.cs
git commit -m "feat: add WorkflowExecutionContext — pure state object, no service locator"
```

---

### Task 5: Create IWorkflowStepExecutorRegistry interface

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStepExecutorRegistry.cs`

- [ ] **Step 1: Create the interface**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStepExecutorRegistry.cs
namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Resolves the executor for the given target.
/// Throws UnsupportedWorkflowTargetException if no executor is registered.
/// WorkflowCompatibilityValidator must guarantee this never fails at runtime.
/// Registry is precomputed at startup — immutable.
/// </summary>
public interface IWorkflowStepExecutorRegistry
{
    IWorkflowStepExecutor Resolve(InteractionTarget target);
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowStepExecutorRegistry.cs
git commit -m "feat: add IWorkflowStepExecutorRegistry interface"
```

---

### Task 6: Create IWorkflowInstanceStore interface

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs`

- [ ] **Step 1: Create the interface**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs
namespace CrestCreates.Workflow.Abstractions;

/// <summary>
/// Upsert semantics. No INSERT/UPDATE database semantics in the abstraction.
/// </summary>
public interface IWorkflowInstanceStore
{
    Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default);
    Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default);
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs
git commit -m "feat: add IWorkflowInstanceStore interface — upsert semantics"
```

---

### Task 7: Create exception types

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/UnsupportedWorkflowTargetException.cs`
- Create: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowValidationException.cs`

- [ ] **Step 1: Create UnsupportedWorkflowTargetException**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/UnsupportedWorkflowTargetException.cs
namespace CrestCreates.Workflow.Abstractions;

public sealed class UnsupportedWorkflowTargetException : Exception
{
    public Type TargetType { get; }

    public UnsupportedWorkflowTargetException(Type targetType)
        : base($"No executor registered for target type '{targetType.Name}'.")
    {
        TargetType = targetType;
    }
}
```

- [ ] **Step 2: Create WorkflowValidationException**

```csharp
// framework/src/CrestCreates.Workflow.Abstractions/WorkflowValidationException.cs
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowValidationException : Exception
{
    public WorkflowValidationException(string message) : base(message) { }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/UnsupportedWorkflowTargetException.cs \
        framework/src/CrestCreates.Workflow.Abstractions/WorkflowValidationException.cs
git commit -m "feat: add UnsupportedWorkflowTargetException and WorkflowValidationException"
```

---

### Task 8: Upgrade WorkflowStepResult

**Files:**
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/WorkflowStepResult.cs`

- [ ] **Step 1: Read the existing file**

```bash
cat framework/src/CrestCreates.Workflow.Abstractions/WorkflowStepResult.cs
```

Current content:
```csharp
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowStepResult
{
    public string StepId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public object? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}
```

- [ ] **Step 2: Replace `bool IsSuccess` with `StepExecutionStatus Status`, add `DateTimeOffset ExecutedAt`**

Replace the entire file content:

```csharp
namespace CrestCreates.Workflow.Abstractions;

public sealed class WorkflowStepResult
{
    public string StepId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public StepExecutionStatus Status { get; init; }
    public object? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset ExecutedAt { get; init; }
    public TimeSpan Duration { get; init; }
}
```

- [ ] **Step 3: Build to verify. Expect compile errors in WorkflowEngine.cs where `IsSuccess` is referenced.**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded (only Workflow.Abstractions project). The Workflow.csproj will have compile errors because `WorkflowEngine` references `IsSuccess` — this is expected and will be fixed in Task 16.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/WorkflowStepResult.cs
git commit -m "refactor: upgrade WorkflowStepResult — IsSuccess→StepExecutionStatus, add ExecutedAt"
```

---

### Task 9: Revise IWorkflowEngine

**Files:**
- Modify: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowEngine.cs`

- [ ] **Step 1: Read existing interface**

Current content:
```csharp
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowEngine
{
    Task<WorkflowInstance> ExecuteAsync(
        string workflowName,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default);

    Task<WorkflowInstance> ResumeAsync(
        string instanceId,
        CancellationToken ct = default);
}
```

- [ ] **Step 2: Remove ResumeAsync, rename workflowName → workflowId, add TODO comment**

Replace the entire file content:

```csharp
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowEngine
{
    /// <summary>
    /// TODO: Phase 5 — migrate to VersionedDescriptorRef&lt;WorkflowDescriptor&gt;
    /// for unambiguous version targeting.
    /// </summary>
    Task<WorkflowInstance> ExecuteAsync(
        string workflowId,
        Dictionary<string, object?>? inputVariables = null,
        CancellationToken ct = default);
}
```

- [ ] **Step 3: Build to verify (expect Workflow implementation projects to fail)**

Run: `dotnet build framework/src/CrestCreates.Workflow.Abstractions/CrestCreates.Workflow.Abstractions.csproj`
Expected: Build succeeded. Implementation projects will fail — expected.

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowEngine.cs
git commit -m "refactor: IWorkflowEngine — remove ResumeAsync, rename workflowName→workflowId"
```

---

### Task 10: Write Validator rejection tests (TDD)

**Files:**
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowCompatibilityValidatorTests.cs`

- [ ] **Step 1: Create test file with Case 4 (SubWorkflow rejection)**

```csharp
// framework/test/CrestCreates.Workflow.Tests/WorkflowCompatibilityValidatorTests.cs
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowCompatibilityValidatorTests
{
    private static WorkflowDescriptor CreateDescriptorWithStep(InteractionTarget target,
        StepErrorBehavior onError = StepErrorBehavior.Fail,
        IReadOnlyList<string>? transitions = null)
    {
        return new WorkflowDescriptor
        {
            Id = "wf_test", Name = "test.wf", Version = 1,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Test Step",
                    Target = target,
                    OnError = onError,
                    Transitions = transitions ?? Array.Empty<string>()
                }
            }
        };
    }

    [Fact]
    public void Validate_SubWorkflowTarget_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new SubWorkflowTarget
            {
                SubWorkflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_sub", 1)
            });

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*SubWorkflowTarget*");
    }

    [Fact]
    public void Validate_RetryErrorBehavior_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            },
            onError: StepErrorBehavior.Retry);

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*StepErrorBehavior.Retry*");
    }

    [Fact]
    public void Validate_CompensateErrorBehavior_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            },
            onError: StepErrorBehavior.Compensate);

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*StepErrorBehavior.Compensate*");
    }

    [Fact]
    public void Validate_Transitions_Throws()
    {
        var descriptor = CreateDescriptorWithStep(
            new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            },
            transitions: new List<string> { "step_02" });

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().Throw<WorkflowValidationException>()
            .WithMessage("*transition*");
    }

    [Fact]
    public void Validate_ValidDescriptor_DoesNotThrow()
    {
        var descriptor = CreateDescriptorWithStep(
            new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
            },
            onError: StepErrorBehavior.Skip);

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_FailErrorBehavior_DoesNotThrow()
    {
        var descriptor = CreateDescriptorWithStep(
            new CapabilityTarget
            {
                Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
            },
            onError: StepErrorBehavior.Fail);

        var validator = new WorkflowCompatibilityValidator();
        var act = () => validator.Validate(descriptor);

        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (WorkflowCompatibilityValidator doesn't exist yet)**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: Compilation error — `WorkflowCompatibilityValidator` not found. This is TDD: test first.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowCompatibilityValidatorTests.cs
git commit -m "test: add WorkflowCompatibilityValidator rejection tests (Cases 4-6)"
```

---

### Task 11: Implement WorkflowCompatibilityValidator

**Files:**
- Create: `framework/src/CrestCreates.Workflow/WorkflowCompatibilityValidator.cs`

- [ ] **Step 1: Implement the validator**

```csharp
// framework/src/CrestCreates.Workflow/WorkflowCompatibilityValidator.cs
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

/// <summary>
/// Bootstrap validation only. Validates that a WorkflowDescriptor
/// contains only constructs supported by the current runtime phase.
/// Must be called during application startup, not during execution.
/// </summary>
public sealed class WorkflowCompatibilityValidator
{
    public void Validate(WorkflowDescriptor descriptor)
    {
        foreach (var step in descriptor.Steps)
        {
            ValidateTarget(step.Target);
            ValidateErrorBehavior(step.OnError);
            ValidateTransitions(step.Transitions);
        }
    }

    private static void ValidateTarget(InteractionTarget target)
    {
        if (target is SubWorkflowTarget)
            throw new WorkflowValidationException(
                "SubWorkflowTarget is not supported in Phase 4b.");
    }

    private static void ValidateErrorBehavior(StepErrorBehavior behavior)
    {
        if (behavior is StepErrorBehavior.Retry or StepErrorBehavior.Compensate)
            throw new WorkflowValidationException(
                $"StepErrorBehavior.{behavior} is not supported in Phase 4b.");
    }

    private static void ValidateTransitions(IReadOnlyList<string> transitions)
    {
        if (transitions.Count > 0)
            throw new WorkflowValidationException(
                "Workflow step transitions are not supported in Phase 4b.");
    }
}
```

- [ ] **Step 2: Run validator tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj --filter "FullyQualifiedName~WorkflowCompatibilityValidatorTests"`
Expected: All 6 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowCompatibilityValidator.cs
git commit -m "feat: implement WorkflowCompatibilityValidator — bootstrap validation for Phase 4b"
```

---

### Task 12: Implement CapabilityStepExecutor

**Files:**
- Create: `framework/src/CrestCreates.Workflow/CapabilityStepExecutor.cs`

- [ ] **Step 1: Implement the executor**

```csharp
// framework/src/CrestCreates.Workflow/CapabilityStepExecutor.cs
using CrestCreates.Capability.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class CapabilityStepExecutor : IWorkflowStepExecutor
{
    private readonly ICapabilityPipeline _pipeline;

    public CapabilityStepExecutor(ICapabilityPipeline pipeline)
        => _pipeline = pipeline;

    public async Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        var target = (CapabilityTarget)context.Step.Target;
        var result = await _pipeline.ExecuteAsync(
            $"capability:{target.Capability.Id}",
            input: context.Instance.Variables,
            ct: ct);

        var variables = result.IsSuccess && result.Output is Dictionary<string, object?> vars
            ? vars
            : null;

        return new StepExecutionResult(
            result.IsSuccess ? StepExecutionStatus.Completed : StepExecutionStatus.Failed,
            result.Output,
            variables);
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build may fail due to `WorkflowEngine` referencing `ResumeAsync` and `IsSuccess` — expected, will be fixed in engine refactor.

CapabilityStepExecutor itself should compile fine.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/CapabilityStepExecutor.cs
git commit -m "feat: add CapabilityStepExecutor — delegates to ICapabilityPipeline"
```

---

### Task 13: Implement HumanTaskStepExecutor

**Files:**
- Create: `framework/src/CrestCreates.Workflow/HumanTaskStepExecutor.cs`

- [ ] **Step 1: Implement the executor**

```csharp
// framework/src/CrestCreates.Workflow/HumanTaskStepExecutor.cs
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class HumanTaskStepExecutor : IWorkflowStepExecutor
{
    public Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context, CancellationToken ct)
    {
        // Placeholder: produces Suspended result.
        // Phase 5/6 HumanTask Runtime will replace with actual task creation.
        return Task.FromResult(
            new StepExecutionResult(StepExecutionStatus.Suspended));
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build may still fail on WorkflowEngine — expected.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/HumanTaskStepExecutor.cs
git commit -m "feat: add HumanTaskStepExecutor — placeholder, returns Suspended"
```

---

### Task 14: Implement DefaultStepExecutorRegistry

**Files:**
- Create: `framework/src/CrestCreates.Workflow/DefaultStepExecutorRegistry.cs`

- [ ] **Step 1: Implement the registry**

```csharp
// framework/src/CrestCreates.Workflow/DefaultStepExecutorRegistry.cs
using System.Collections.Frozen;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class DefaultStepExecutorRegistry : IWorkflowStepExecutorRegistry
{
    private readonly FrozenDictionary<Type, IWorkflowStepExecutor> _executors;

    public DefaultStepExecutorRegistry(
        CapabilityStepExecutor capabilityExecutor,
        HumanTaskStepExecutor humanTaskExecutor)
    {
        _executors = new Dictionary<Type, IWorkflowStepExecutor>
        {
            [typeof(CapabilityTarget)] = capabilityExecutor,
            [typeof(HumanTaskTarget)] = humanTaskExecutor
        }.ToFrozenDictionary();
    }

    public IWorkflowStepExecutor Resolve(InteractionTarget target)
    {
        if (_executors.TryGetValue(target.GetType(), out var executor))
            return executor;

        throw new UnsupportedWorkflowTargetException(target.GetType());
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build may still fail on WorkflowEngine — expected.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/DefaultStepExecutorRegistry.cs
git commit -m "feat: add DefaultStepExecutorRegistry — FrozenDictionary-backed"
```

---

### Task 15: Implement InMemoryWorkflowInstanceStore

**Files:**
- Create: `framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs`

- [ ] **Step 1: Implement the store**

```csharp
// framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs
using System.Collections.Concurrent;
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
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build may still fail on WorkflowEngine — expected.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs
git commit -m "feat: add InMemoryWorkflowInstanceStore — ConcurrentDictionary-backed"
```

---

### Task 16: Refactor WorkflowEngine

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/WorkflowEngine.cs`

This is the core task. The existing `WorkflowEngine.cs` (~383 lines) must be rewritten to the new architecture.

- [ ] **Step 1: Read current file for reference**

Current lines to note:
- Lines 1-26: usings + constructor (`IWorkflowRegistry`, `ICapabilityPipeline?`, `IDraftStore?`)
- Lines 28-51: `ExecuteAsync` — resolve descriptor, create instance, call `ExecuteStepsAsync`
- Lines 53-80: `ResumeAsync` — **REMOVE entirely**
- Lines 82-90: `CheckpointState` class — **REMOVE**
- Lines 92-187: `ExecuteStepsAsync` — **REWRITE**
- Lines 189-216: `ExecuteStepAsync` — **REPLACE with registry-based dispatch**
- Lines 218-256: `ExecuteCapabilityTarget` — **EXTRACTED to CapabilityStepExecutor**
- Lines 258-308: `ExecuteSubWorkflowTarget` — **REMOVE**
- Lines 310-332: `HandleStepError` — **SIMPLIFY (keep only Skip)**
- Lines 334-352: `SuspendInstance` — **EXTRACTED to HumanTaskStepExecutor**
- Lines 354-382: `CheckpointAsync` — **REMOVE**

- [ ] **Step 2: Write the new WorkflowEngine**

Replace the entire file:

```csharp
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowStepExecutorRegistry _executorRegistry;
    private readonly IWorkflowInstanceStore _store;

    public WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowStepExecutorRegistry executorRegistry,
        IWorkflowInstanceStore store)
    {
        _registry = registry;
        _executorRegistry = executorRegistry;
        _store = store;
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

        return await ExecuteStepsAsync(instance, descriptor, ct).ConfigureAwait(false);
    }

    private async Task<WorkflowInstance> ExecuteStepsAsync(
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        CancellationToken ct)
    {
        var steps = descriptor.Steps;
        instance.Status = WorkflowInstanceStatus.Running;

        while (instance.StepIndex < steps.Count)
        {
            ct.ThrowIfCancellationRequested();

            var step = steps[instance.StepIndex];
            instance.CurrentStepId = step.Id;

            var startedAt = DateTimeOffset.UtcNow;

            // Resolve executor via registry — no target-type branching in engine
            var executor = _executorRegistry.Resolve(step.Target);
            var context = new WorkflowExecutionContext(descriptor, instance, step);

            StepExecutionResult stepResult;
            try
            {
                stepResult = await executor.ExecuteAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Infrastructure/programming error — record as Failed
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
                instance.Status = WorkflowInstanceStatus.Failed;
                instance.ErrorMessage = ex.Message;
                instance.CompletedAt = DateTimeOffset.UtcNow;
                await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                return instance;
            }

            // Engine applies variable changes — executor is pure
            if (stepResult.Variables != null)
            {
                foreach (var kv in stepResult.Variables)
                    instance.Variables[kv.Key] = kv.Value;
            }

            // Record history
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

            // State transitions based on executor result
            switch (stepResult.Status)
            {
                case StepExecutionStatus.Completed:
                    instance.StepIndex++;
                    continue;

                case StepExecutionStatus.Suspended:
                    instance.Status = WorkflowInstanceStatus.Suspended;
                    instance.CurrentStepId = null;
                    instance.CompletedAt = DateTimeOffset.UtcNow;
                    await _store.SaveAsync(instance, ct).ConfigureAwait(false);
                    return instance;

                case StepExecutionStatus.Failed:
                    // StepErrorBehavior.Skip: record failure, continue
                    if (step.OnError == StepErrorBehavior.Skip)
                    {
                        instance.StepIndex++;
                        continue;
                    }
                    // StepErrorBehavior.Fail (default): stop execution
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

- [ ] **Step 3: Verify the using directives are correct**

The new code uses:
- `CrestCreates.Workflow.Abstractions` — provides `WorkflowDescriptor`, `WorkflowInstance`, `WorkflowStep`, `IWorkflowEngine`, `IWorkflowRegistry`, `IWorkflowStepExecutorRegistry`, `IWorkflowInstanceStore`, `WorkflowExecutionContext`, `StepExecutionResult`, `StepExecutionStatus`, `WorkflowStepResult`, `WorkflowInstanceStatus`, `VersionedDescriptorRef`, `InteractionTarget`, `CapabilityTarget`, `HumanTaskTarget`, `StepErrorBehavior`
- `CrestCreates.Metadata.Abstractions` — provides `VersionedDescriptorRef<T>` — but this is already in the namespace since `Workflow.Abstractions` uses it.

The single `using CrestCreates.Workflow.Abstractions;` is sufficient.

- [ ] **Step 4: Build — should now succeed since all new types exist**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Check LSP diagnostics**

Run diagnostics on the file to ensure no errors/warnings.

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowEngine.cs
git commit -m "refactor: rewrite WorkflowEngine — registry-based dispatch, IWorkflowInstanceStore, pure executors"
```

---

### Task 17: Update DI registration

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs`

- [ ] **Step 1: Replace DI registration**

Current content:
```csharp
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Workflow;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.TryAddSingleton<IWorkflowEngine, WorkflowEngine>();
        return services;
    }
}
```

Replace with:

```csharp
using CrestCreates.Workflow.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CrestCreates.Workflow;

public static class WorkflowServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
    {
        services.TryAddSingleton<IWorkflowEngine, WorkflowEngine>();
        services.TryAddSingleton<IWorkflowInstanceStore, InMemoryWorkflowInstanceStore>();
        services.TryAddSingleton<CapabilityStepExecutor>();
        services.TryAddSingleton<HumanTaskStepExecutor>();
        services.TryAddSingleton<IWorkflowStepExecutorRegistry, DefaultStepExecutorRegistry>();
        services.TryAddSingleton<WorkflowCompatibilityValidator>();
        return services;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs
git commit -m "refactor: update DI — register executor registry, store, validators"
```

---

### Task 18: Add post-build hook to MetadataBootstrapper

**Files:**
- Modify: `framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs`

`CrestCreates.Metadata.csproj` references `CrestCreates.Workflow.Abstractions` but NOT `CrestCreates.Workflow`. Since `WorkflowCompatibilityValidator` lives in `CrestCreates.Workflow`, we cannot add a hard dependency without creating a circular or layering violation. Instead, add an optional post-build callback parameter.

- [ ] **Step 1: Read existing Bootstrapper**

Current content (24 lines). The signatures reference `ISchemaRegistry`, `IFormRegistry`, `IHumanTaskRegistry`, `IWorkflowRegistry`, `IEventRegistry`.

- [ ] **Step 2: Add `onWorkflowBuilt` callback parameter**

```csharp
// framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs
using CrestCreates.Event.Abstractions;
using CrestCreates.Form.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Schema.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Metadata;

public static class MetadataBootstrapper
{
    public static void BuildAll(
        ISchemaRegistry schemaRegistry,
        IFormRegistry formRegistry,
        IHumanTaskRegistry humanTaskRegistry,
        IWorkflowRegistry workflowRegistry,
        IEventRegistry eventRegistry,
        Action<IReadOnlyList<WorkflowDescriptor>>? onWorkflowBuilt = null)
    {
        schemaRegistry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        formRegistry.Build(DescriptorProviderRegistry.GetProviders<FormDescriptor>());
        humanTaskRegistry.Build(DescriptorProviderRegistry.GetProviders<HumanTaskDescriptor>());
        workflowRegistry.Build(DescriptorProviderRegistry.GetProviders<WorkflowDescriptor>());
        eventRegistry.Build(DescriptorProviderRegistry.GetProviders<GeneratedEventDescriptor>());

        // Post-build: workflow compatibility validation (Phase 4b).
        // Validator registration alone does not activate validation.
        // Consumer must pass a callback that invokes WorkflowCompatibilityValidator.
        onWorkflowBuilt?.Invoke(workflowRegistry.GetAll());
    }
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj`
Expected: Build succeeded (no new project references added).

- [ ] **Step 4: Commit**

```bash
git add framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs
git commit -m "feat: add onWorkflowBuilt callback to MetadataBootstrapper.BuildAll"
```

---

### Task 19: Build entire framework to confirm no compile errors

**Files:**
- All framework projects

- [ ] **Step 1: Build the full solution**

Run: `dotnet build framework/CrestCreates.slnx 2>&1 || dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
Expected: Build succeeded. All new types compile. Engine refactored and compiles. No `IsSuccess`, `ResumeAsync`, or `IDraftStore` references remain.

- [ ] **Step 2: Check for any remaining references to removed types**

Run: `grep -r "ResumeAsync\|IDraftStore\|CheckpointState\|HandleStepError\|ExecuteSubWorkflowTarget\|\.IsSuccess" framework/src/CrestCreates.Workflow/ --include="*.cs"`
Expected: No results in `WorkflowEngine.cs`. May find `IsSuccess` on `CapabilityExecutionResult` in CapabilityStepExecutor — that's correct (pipeline result, not step result).

- [ ] **Step 3: Commit (if any changes from build fixes)**

Only if build fixes were needed: `git add -A && git commit -m "fix: resolve compile errors from engine refactor"`

---

### Task 20: Write runtime integration tests (Cases 1-3, 7)

**Files:**
- Create: `framework/test/CrestCreates.Workflow.Tests/WorkflowRuntimeTests.cs`

These tests verify the full engine loop with real executors. They need a mock `ICapabilityPipeline` to control Capability behavior.

- [ ] **Step 1: Create test infrastructure — MockCapabilityPipeline**

```csharp
// framework/test/CrestCreates.Workflow.Tests/WorkflowRuntimeTests.cs
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowRuntimeTests
{
    // Helper: create a registry with given descriptors
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

    // Helper: create the engine with a controllable pipeline
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

    // Controllable pipeline mock
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

- [ ] **Step 2: Case 1 — Linear Capability workflow → Completed**

```csharp
    [Fact]
    public async Task ExecuteAsync_TwoCapabilitySteps_CompletesSuccessfully()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "linear.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Step A",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } },
                new() { Id = "step_02", Name = "Step B",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1) } }
            }
        });

        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults.Should().HaveCount(2);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Completed);
        instance.StepResults[1].Status.Should().Be(StepExecutionStatus.Completed);
    }
```

- [ ] **Step 3: Case 2 — Capability + HumanTask → Suspended**

```csharp
    [Fact]
    public async Task ExecuteAsync_CapabilityThenHumanTask_Suspends()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "suspend.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Cap Step",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } },
                new() { Id = "step_02", Name = "Human Step",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
            }
        });

        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepResults.Should().HaveCount(2);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Completed);
        instance.StepResults[1].Status.Should().Be(StepExecutionStatus.Suspended);
        instance.StepIndex.Should().Be(1);
    }
```

- [ ] **Step 4: Case 3 — Capability fails → Failed**

```csharp
    [Fact]
    public async Task ExecuteAsync_CapabilityFails_StopsWithFailed()
    {
        var failingPipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Failure("ERR", "capability error", TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "fail.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Cap A",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } },
                new() { Id = "step_02", Name = "Cap B",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_b", 1) } }
            }
        });

        var engine = CreateEngine(registry, pipeline: failingPipeline);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Failed);
    }
```

- [ ] **Step 5: Case 7 — Skip continues past failure**

```csharp
    [Fact]
    public async Task ExecuteAsync_SkipOnError_ContinuesAfterFailure()
    {
        var failingPipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Failure("ERR", "skip me", TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "skip.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Failing Step",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) },
                    OnError = StepErrorBehavior.Skip },
                new() { Id = "step_02", Name = "Human Step",
                    Target = new HumanTaskTarget { HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1) } }
            }
        });

        var engine = CreateEngine(registry, pipeline: failingPipeline);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepResults.Should().HaveCount(2);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Failed);
        instance.StepResults[1].Status.Should().Be(StepExecutionStatus.Suspended);
    }
```

- [ ] **Step 6: Additional — exception from executor handled as infrastructure failure**

```csharp
    [Fact]
    public async Task ExecuteAsync_ExecutorThrows_RecordsAsFailed()
    {
        var throwingPipeline = new MockThrowingPipeline();
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "throw.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new() { Id = "step_01", Name = "Boom",
                    Target = new CapabilityTarget { Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_a", 1) } }
            }
        });

        var engine = CreateEngine(registry, pipeline: throwingPipeline);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Failed);
        instance.StepResults[0].ErrorMessage.Should().Be("infrastructure boom");
        instance.ErrorMessage.Should().Be("infrastructure boom");
    }

    private class MockThrowingPipeline : ICapabilityPipeline
    {
        public Task<CapabilityExecutionResult> ExecuteAsync(
            string capabilityIdOrName, object? input = null,
            Action<CapabilityExecutionContext>? configureContext = null,
            CancellationToken ct = default)
            => throw new InvalidOperationException("infrastructure boom");
    }
```

- [ ] **Step 7: Run tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj --filter "FullyQualifiedName~WorkflowRuntimeTests"`
Expected: 6 tests PASS (5 runtime + infrastructure throw).

- [ ] **Step 8: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowRuntimeTests.cs
git commit -m "test: add WorkflowRuntime integration tests (Cases 1-3, 7 + exception)"
```

---

### Task 21: Fix/remove broken existing tests

**Files:**
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs`

The existing tests reference removed features. These tests must be purged.

- [ ] **Step 1: Identify tests to remove**

Tests that test REMOVED features and must be deleted:
- `ResumeAsync_NoDraftStore_Throws` — ResumeAsync removed
- `ResumeAsync_NoCheckpoint_Throws` — ResumeAsync removed
- `ResumeAsync_ValidCheckpoint_ContinuesExecution` — ResumeAsync removed
- `ResumeThenExecute_HasCorrectInstanceId` — ResumeAsync removed
- `ResumeAsync_AfterSuspend_ContinuesFromNextStep` — ResumeAsync removed
- `ExecuteAsync_SubWorkflow_ExecutesRecursively` — SubWorkflow removed
- `ExecuteAsync_StepError_Retry_HasMaxRetryGuard` — Retry removed
- `ExecuteAsync_StepTransition_FollowsSpecifiedStep` — Transitions removed
- `ExecuteAsync_CapabilityTarget_NoPipeline_ReturnsFailure` — ICapabilityPipeline no longer nullable on engine (moved to CapabilityStepExecutor)
- `ExecuteAsync_UnknownTarget_ReturnsFailure` — Unknown target now throws via registry (UnsupportedWorkflowTargetException) instead of returning failure result

Tests that still pass as-is:
- `ExecuteAsync_WorkflowNotFound_Throws`
- `ExecuteAsync_EmptyWorkflow_CompletesImmediately`
- `ExecuteAsync_HumanTaskTarget_SucceedsAsPassthrough` — needs update: HumanTask now returns Suspended, not Completed
- `ExecuteAsync_Variables_PassedAsInput`
- `ExecuteAsync_StepError_Skip_ContinuesToNext` — needs update: uses Capability then HumanTask, needs MockCapabilityPipeline
- `ExecuteAsync_StepError_Fail_StopsExecution` — needs update: uses nullable pipeline
- `ExecuteAsync_HumanTaskTarget_SuspendsInstance` — needs update: rename, verify Suspended status
- `ExecuteAsync_StepsAfterHumanTask_NotExecuted` — needs update

- [ ] **Step 2: Remove broken tests, update preserved ones**

Replace the entire `WorkflowEngineTests.cs` with:

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using CrestCreates.Workflow.Abstractions;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Workflow.Tests;

public class WorkflowEngineTests
{
    private static WorkflowDescriptor CreateWorkflow(string id, string name, int version,
        params WorkflowStep[] steps)
    {
        return new WorkflowDescriptor
        {
            Id = id, Name = name, Version = version, State = DescriptorState.Active,
            Steps = steps.ToList()
        };
    }

    private static WorkflowRegistry CreateRegistry(params WorkflowDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<WorkflowDescriptor>(
            Array.Empty<IRegistryValidator<WorkflowDescriptor>>());
        var registry = new WorkflowRegistry(engine);
        var provider = new TestWorkflowProvider(descriptors.ToList());
        registry.Build([provider]);
        return registry;
    }

    private class TestWorkflowProvider : IDescriptorProvider<WorkflowDescriptor>
    {
        private readonly List<WorkflowDescriptor> _descriptors;
        public TestWorkflowProvider(List<WorkflowDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<WorkflowDescriptor> GetDescriptors() => _descriptors;
    }

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

    [Fact]
    public async Task ExecuteAsync_WorkflowNotFound_Throws()
    {
        var registry = CreateRegistry();
        var engine = CreateEngine(registry);
        await engine.Invoking(e => e.ExecuteAsync("nonexistent"))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExecuteAsync_EmptyWorkflow_CompletesImmediately()
    {
        var registry = CreateRegistry(CreateWorkflow("wf_01", "empty.wf", 1));
        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
        instance.StepResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_HumanTaskTarget_SuspendsInstance()
    {
        var registry = CreateRegistry(CreateWorkflow("wf_01", "suspend.wf", 1,
            new WorkflowStep
            {
                Id = "step_01", Name = "Human Step",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                }
            }));
        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Suspended);
    }

    [Fact]
    public async Task ExecuteAsync_StepsAfterHumanTask_NotExecuted()
    {
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "suspend2.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Human Step",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                },
                new()
                {
                    Id = "step_02", Name = "Never Executed",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
                    }
                }
            }
        });
        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepResults.Should().HaveCount(1);
        instance.StepResults[0].StepId.Should().Be("step_01");
    }

    [Fact]
    public async Task ExecuteAsync_Variables_PassedAsInput()
    {
        var registry = CreateRegistry(CreateWorkflow("wf_01", "vars.wf", 1));
        var engine = CreateEngine(registry);

        var instance = await engine.ExecuteAsync("wf_01",
            new Dictionary<string, object?> { ["key1"] = "val1", ["key2"] = 42 });

        instance.Variables["key1"].Should().Be("val1");
        instance.Variables["key2"].Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsync_StepError_Skip_ContinuesAfterFailure()
    {
        var failingPipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Failure("ERR", "skip me", TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "skip.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Skipped Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
                    },
                    OnError = StepErrorBehavior.Skip
                },
                new()
                {
                    Id = "step_02", Name = "Good Step",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                }
            }
        });
        var engine = CreateEngine(registry, pipeline: failingPipeline);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
        instance.StepResults.Should().HaveCount(2);
        instance.StepResults[0].Status.Should().Be(StepExecutionStatus.Failed);
        instance.StepResults[1].Status.Should().Be(StepExecutionStatus.Suspended);
    }

    [Fact]
    public async Task ExecuteAsync_StepError_Fail_StopsExecution()
    {
        var failingPipeline = new MockCapabilityPipeline(
            CapabilityExecutionResult.Failure("ERR", "fail", TimeSpan.Zero));
        var registry = CreateRegistry(new WorkflowDescriptor
        {
            Id = "wf_01", Name = "fail.wf", Version = 1, State = DescriptorState.Active,
            Steps = new List<WorkflowStep>
            {
                new()
                {
                    Id = "step_01", Name = "Failing Step",
                    Target = new CapabilityTarget
                    {
                        Capability = new VersionedDescriptorRef<IVersionedDescriptor>("cap_01", 1)
                    },
                    OnError = StepErrorBehavior.Fail
                },
                new()
                {
                    Id = "step_02", Name = "Never Reached",
                    Target = new HumanTaskTarget
                    {
                        HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                    }
                }
            }
        });
        var engine = CreateEngine(registry, pipeline: failingPipeline);

        var instance = await engine.ExecuteAsync("wf_01");

        instance.Status.Should().Be(WorkflowInstanceStatus.Failed);
        instance.StepResults.Should().HaveCount(1);
    }
}
```

- [ ] **Step 2: Run all Workflow tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: ALL tests PASS — both existing (updated) and new tests.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs
git commit -m "test: update WorkflowEngineTests — remove Resume/SubWorkflow/Retry/Transition tests"
```

---

### Task 22: Run full framework test suite

**Files:**
- All test projects

- [ ] **Step 1: Run all framework tests**

Run: `dotnet test framework/test/`
Expected: All tests pass. No regressions from Phase 4b changes.

- [ ] **Step 2: If any other projects reference removed types, fix them**

Check `WorkflowEventConsumer.cs` — it references `IWorkflowEngine` but only stores/uses it for future resume. Should compile fine since interface still exists (just without ResumeAsync).

- [ ] **Step 3: Commit (if changes needed)**

---

### Task 23: Final verification — build + test

**Files:**
- All

- [ ] **Step 1: Full restore + build**

```bash
dotnet restore && dotnet build
```
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 2: Full test run**

```bash
dotnet test
```
Expected: All tests pass.

- [ ] **Step 3: LSP diagnostics on changed files**

Check:
- `WorkflowEngine.cs` — no errors
- `CapabilityStepExecutor.cs` — no errors
- `WorkflowCompatibilityValidator.cs` — no errors
- Test files — no errors

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "Phase 4b: Workflow Runtime Foundation — complete"
```<｜end▁of▁thinking｜>Now let me check the Metadata project references before writing the bootstrapper task.

<｜｜DSML｜｜tool_calls>
<｜｜DSML｜｜invoke name="read">
<｜｜DSML｜｜parameter name="filePath" string="true">/home/orches/workspace/CrestCreates/framework/src/CrestCreates.Metadata/CrestCreates.Metadata.csproj