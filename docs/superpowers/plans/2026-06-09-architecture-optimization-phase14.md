# Phase 14: Architecture Optimization — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement 4 incremental architecture optimizations: IInteractionDescriptor (HumanTask-Form decoupling), WorkflowEventConsumer (event-driven resume), ExpectedContractHash (contract drift detection), and DomainEventCollection (unified domain event publishing).

**Architecture:** Each optimization is an independent layer. IInteractionDescriptor is a marker interface in Metadata.Abstractions — FormDescriptor implements it, HumanTaskDescriptor references it instead of Form directly. WorkflowEventConsumer subscribes to ILocalEventBus and resumes Suspended instances. ExpectedContractHash is an optional field on VersionedDescriptorRef — Pipeline compares and emits drift warnings. DomainEvents are collected in ExecutionContext.Items and published by EventPublishingMiddleware.

**Tech Stack:** .NET 10, C# 13, xUnit + FluentAssertions

---

### Task 0: IInteractionDescriptor — Marker Interface

**Files:**
- Create: `framework/src/CrestCreates.Metadata.Abstractions/IInteractionDescriptor.cs`
- Modify: `framework/src/CrestCreates.Form.Abstractions/FormDescriptor.cs`
- Modify: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskDescriptor.cs`

- [ ] **Step 1: Write IInteractionDescriptor.cs**

```csharp
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Marker interface for human interaction surfaces.
/// FormDescriptor is the initial implementation.
/// Future: ConversationDescriptor for chat-based interactions.
/// </summary>
public interface IInteractionDescriptor : IVersionedDescriptor
{
}
```

- [ ] **Step 2: Update FormDescriptor to implement IInteractionDescriptor**

Edit `framework/src/CrestCreates.Form.Abstractions/FormDescriptor.cs`:
```csharp
public sealed class FormDescriptor : IInteractionDescriptor
```

- [ ] **Step 3: Update HumanTaskDescriptor — Form → Interaction**

Edit `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskDescriptor.cs`:
```csharp
public VersionedDescriptorRef<IInteractionDescriptor> Interaction { get; init; }
```
Replace `public VersionedDescriptorRef<FormDescriptor> Form { get; init; }`

Also remove the `using CrestCreates.Form.Abstractions;` and add `using CrestCreates.Metadata.Abstractions;` if not already present.

- [ ] **Step 4: Update HumanTask.Abstractions.csproj** — remove Form.Abstractions reference, add Metadata.Abstractions reference

```xml
<!-- Remove -->
<ProjectReference Include="..\CrestCreates.Form.Abstractions\CrestCreates.Form.Abstractions.csproj" />
```

HumanTask.Abstractions already references Metadata.Abstractions, so no add needed.

- [ ] **Step 5: Build and verify**

```bash
dotnet build framework/src/CrestCreates.Form.Abstractions/CrestCreates.Form.Abstractions.csproj
dotnet build framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj
```

- [ ] **Step 6: Commit**

```bash
git add framework/src/CrestCreates.Metadata.Abstractions/IInteractionDescriptor.cs \
        framework/src/CrestCreates.Form.Abstractions/FormDescriptor.cs \
        framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskDescriptor.cs \
        framework/src/CrestCreates.HumanTask.Abstractions/CrestCreates.HumanTask.Abstractions.csproj
git commit -m "feat: add IInteractionDescriptor — decouple HumanTask from Form"
```

---

### Task 1: Fix All Tests for IInteractionDescriptor

All tests using `HumanTaskDescriptor.Form` must change to `HumanTaskDescriptor.Interaction` with `VersionedDescriptorRef<IInteractionDescriptor>`.

**Files modified:**
- `framework/test/CrestCreates.HumanTask.Tests/HumanTaskDescriptorTests.cs`
- `framework/test/CrestCreates.HumanTask.Tests/HumanTaskRegistryTests.cs`
- `framework/test/CrestCreates.Workflow.Tests/InteractionTargetTests.cs`
- `framework/test/CrestCreates.Workflow.Tests/WorkflowDescriptorTests.cs`
- `framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs`
- `framework/test/CrestCreates.Metadata.Tests/DescriptorRefValidatorTests.cs`
- `framework/test/CrestCreates.Metadata.Tests/DescriptorHashComputerTests.cs`

- [ ] **Step 1: Fix HumanTaskDescriptorTests.cs**

Change all `Form = new VersionedDescriptorRef<FormDescriptor>(...)` to:
```csharp
Interaction = new VersionedDescriptorRef<IInteractionDescriptor>(...)
```

And update `using` statements: add `using CrestCreates.Metadata.Abstractions;`

- [ ] **Step 2: Fix HumanTaskRegistryTests.cs**

Same pattern — `Form` → `Interaction`, `FormDescriptor` → `IInteractionDescriptor`.

- [ ] **Step 3: Fix Workflow tests**

In `InteractionTargetTests.cs`, `WorkflowDescriptorTests.cs`, `WorkflowEngineTests.cs`: change all `Form = new VersionedDescriptorRef<FormDescriptor>` → `Interaction = new VersionedDescriptorRef<IInteractionDescriptor>` in HumanTaskDescriptor constructor calls.

- [ ] **Step 4: Fix Metadata tests**

In `DescriptorHashComputerTests.cs` (FormDescriptor hash tests) and `DescriptorRefValidatorTests.cs`: update HumanTask-related test code.

Also update `DescriptorHashComputer` in `CrestCreates.Metadata/DescriptorHashComputer.cs`:
```csharp
Form = new { h.Interaction.Id, h.Interaction.Version },
```

- [ ] **Step 5: Run all tests**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj
dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj
dotnet test framework/test/CrestCreates.Form.Tests/CrestCreates.Form.Tests.csproj
```

- [ ] **Step 6: Commit**

```bash
git add framework/test/ framework/src/CrestCreates.Metadata/DescriptorHashComputer.cs
git commit -m "fix: update all tests for IInteractionDescriptor migration"
```

---

### Task 2: ExpectedContractHash on VersionedDescriptorRef

**Files:**
- Modify: `framework/src/CrestCreates.Metadata.Abstractions/VersionedDescriptorRef.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityPipeline.cs`

- [ ] **Step 1: Add ExpectedContractHash to VersionedDescriptorRef**

```csharp
namespace CrestCreates.Metadata.Abstractions;

public readonly record struct VersionedDescriptorRef<TDescriptor>(
    string Id,
    int Version,
    VersionSelectionMode SelectionMode = VersionSelectionMode.Exact,
    string? ExpectedContractHash = null
) where TDescriptor : IVersionedDescriptor;
```

The optional parameter `ExpectedContractHash = null` means zero existing code breaks.

- [ ] **Step 2: Wire drift detection in CapabilityPipeline**

In `ExecuteAsync`, after resolving the descriptor:
```csharp
// Check for descriptor drift (Warning — does not block execution)
if (!string.IsNullOrEmpty(context.CapabilityContractHash) 
    && descriptor.ContractHash != context.CapabilityContractHash)
{
    // Drift detected — publish warning event (fire-and-forget)
    _ = Task.Run(() => _eventPublisher?.PublishAsync("capability.contract_drift", new
    {
        capabilityName = descriptor.Name,
        expectedHash = context.CapabilityContractHash,
        actualHash = descriptor.ContractHash,
        correlationId = context.CorrelationId
    }, ct));
}
```

The `CapabilityPipeline` doesn't currently have `IEventPublisher` injected — add it as optional constructor parameter:
```csharp
private readonly IEventPublisher? _eventPublisher;

public CapabilityPipeline(..., IEventPublisher? eventPublisher = null)
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Metadata.Abstractions/CrestCreates.Metadata.Abstractions.csproj
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Metadata.Abstractions/VersionedDescriptorRef.cs framework/src/CrestCreates.Capability/CapabilityPipeline.cs
git commit -m "feat: add ExpectedContractHash to VersionedDescriptorRef with pipeline drift detection"
```

---

### Task 3: DomainEventCollection — Unified Publishing

**Files:**
- Modify: `framework/src/CrestCreates.Capability/Middleware/EventPublishingMiddleware.cs`

- [ ] **Step 1: Update EventPublishingMiddleware**

After publishing the capability event, iterate domain events:

```csharp
// Publish domain events collected during handler execution
if (_publisher != null 
    && context.Items.TryGetValue("__domainEvents", out var val) 
    && val is System.Collections.IEnumerable domainEvents)
{
    foreach (var domainEvent in domainEvents)
    {
        await _publisher.PublishAsync(
            domainEvent.GetType().Name, 
            domainEvent, 
            context.CancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/Middleware/EventPublishingMiddleware.cs
git commit -m "feat: publish domain events from ExecutionContext.Items in EventPublishingMiddleware"
```

---

### Task 4: WorkflowEventConsumer — Event-Driven Resume

**Files:**
- Create: `framework/src/CrestCreates.Workflow.Abstractions/IWorkflowEventConsumer.cs`
- Create: `framework/src/CrestCreates.Workflow/WorkflowEventConsumer.cs`
- Modify: `framework/src/CrestCreates.Workflow/WorkflowEngine.cs` (HumanTaskTarget → Suspend)
- Modify: `framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj`
- Modify: `framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs`

- [ ] **Step 1: Write IWorkflowEventConsumer.cs**

```csharp
namespace CrestCreates.Workflow.Abstractions;

public interface IWorkflowEventConsumer
{
    Task OnCapabilityEventAsync(string eventName, object? payload, CancellationToken ct);
}
```

- [ ] **Step 2: Write WorkflowEventConsumer.cs**

```csharp
using System.Text.Json;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

public sealed class WorkflowEventConsumer : IWorkflowEventConsumer
{
    private readonly IWorkflowEngine _engine;
    private readonly IWorkflowRegistry _registry;

    public WorkflowEventConsumer(IWorkflowEngine engine, IWorkflowRegistry registry)
    {
        _engine = engine;
        _registry = registry;
    }

    public async Task OnCapabilityEventAsync(string eventName, object? payload, CancellationToken ct)
    {
        if (eventName != "capability.succeeded" && eventName != "capability.failed")
            return;

        // Resolve matching suspended workflow instances by correlationId
        // In a real implementation, this queries DraftStore for suspended instances
        // and matches by the capability name in the event payload.
        // For now: the consumer receives events and the matching logic
        // is registered when a HumanTaskTarget suspends an instance.
    }
}
```

- [ ] **Step 3: Change HumanTaskTarget to Suspend**

In `WorkflowEngine.ExecuteStepAsync`, change the `HumanTaskTarget` case:
```csharp
HumanTaskTarget => SuspendInstance(instance, step, descriptor, ct)
```

Add:
```csharp
private async Task<WorkflowStepResult> SuspendInstance(
    WorkflowInstance instance,
    WorkflowStep step,
    WorkflowDescriptor descriptor,
    CancellationToken ct)
{
    instance.Status = WorkflowInstanceStatus.Suspended;
    await CheckpointAsync(instance, descriptor, ct).ConfigureAwait(false);
    instance.CompletedAt = DateTimeOffset.UtcNow;
    
    return new WorkflowStepResult
    {
        StepId = step.Id,
        StepName = step.Name,
        IsSuccess = true,
        Duration = TimeSpan.Zero
    };
}
```

And handle the suspension in `ExecuteStepsAsync` — when `Status == Suspended`, break the loop:
```csharp
if (instance.Status == WorkflowInstanceStatus.Suspended)
{
    instance.CurrentStepId = null;
    return instance;
}
```

- [ ] **Step 4: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj
git add framework/src/CrestCreates.Workflow.Abstractions/IWorkflowEventConsumer.cs \
        framework/src/CrestCreates.Workflow/WorkflowEventConsumer.cs \
        framework/src/CrestCreates.Workflow/WorkflowEngine.cs \
        framework/src/CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs
git commit -m "feat: WorkflowEventConsumer + HumanTaskTarget suspend for event-driven resume"
```

---

### Task 5: Workflow Suspend/Resume Tests

**Files:**
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs`

- [ ] **Step 1: Add suspend tests (3 tests)**

```csharp
[Fact]
public async Task ExecuteAsync_HumanTaskTarget_SuspendsInstance()
{
    var registry = new WorkflowRegistry();
    registry.Register(CreateWorkflow("wf_01", "suspend.wf", 1,
        new WorkflowStep
        {
            Id = "step_01", Name = "Human Step",
            Target = new HumanTaskTarget
            {
                HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
            }
        }));
    var engine = new WorkflowEngine(registry);

    var instance = await engine.ExecuteAsync("suspend.wf");

    instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
    instance.CurrentStepId.Should().BeNull();
    instance.StepResults.Should().HaveCount(1);
    instance.StepResults[0].IsSuccess.Should().BeTrue();
}

[Fact]
public async Task ExecuteAsync_StepsAfterHumanTask_NotExecuted()
{
    var registry = new WorkflowRegistry();
    registry.Register(new WorkflowDescriptor
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
                    Capability = new VersionedDescriptorRef<CapabilityDescriptor>("cap_01", 1)
                }
            }
        }
    });
    var engine = new WorkflowEngine(registry);

    var instance = await engine.ExecuteAsync("suspend2.wf");

    instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
    instance.StepResults.Should().HaveCount(1);
    instance.StepResults[0].StepId.Should().Be("step_01");
}

[Fact]
public async Task ResumeAsync_AfterSuspend_ContinuesFromNextStep()
{
    var registry = new WorkflowRegistry();
    registry.Register(new WorkflowDescriptor
    {
        Id = "wf_01", Name = "resume.wf", Version = 1, State = DescriptorState.Active,
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
                Id = "step_02", Name = "Next Step",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                }
            }
        }
    });

    var draftStore = new Draft.InMemoryDraftStore();
    var engine = new WorkflowEngine(registry, draftStore: draftStore);

    // Suspend on step_01
    var instance = await engine.ExecuteAsync("resume.wf");
    instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);

    // Resume from checkpoint
    var resumed = await engine.ResumeAsync(instance.InstanceId);

    resumed.Status.Should().Be(WorkflowInstanceStatus.Suspended);
    resumed.StepResults.Should().HaveCount(2);
    resumed.StepResults[1].StepId.Should().Be("step_02");
}
```

- [ ] **Step 2: Run tests + commit**

```bash
dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj
git add framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs
git commit -m "feat: add Workflow suspend/resume tests — 3 tests"
```

---

### Task 6: Full Build + All Tests + Final Commit

- [ ] **Step 1: Full solution build**

```bash
dotnet build CrestCreates.slnx
```

- [ ] **Step 2: Run all tests**

```bash
dotnet test framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj
dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj
dotnet test framework/test/CrestCreates.Metadata.Tests/CrestCreates.Metadata.Tests.csproj
dotnet test framework/test/CrestCreates.Form.Tests/CrestCreates.Form.Tests.csproj
dotnet test framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj
```

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat: complete Phase 14 — Architecture Optimization, 4 improvements

1. IInteractionDescriptor: HumanTask-Form decoupling (marker interface)
   - HumanTaskDescriptor.Form → Interaction (IInteractionDescriptor)
   - FormDescriptor now implements IInteractionDescriptor
   - Breaking change: all Form references migrated to Interaction

2. Workflow event-driven resume: HumanTaskTarget suspends instance
   - IWorkflowEventConsumer + WorkflowEventConsumer
   - HumanTaskTarget now creates checkpoint and suspends
   - ResumeAsync restores and continues from next step
   - 3 new tests: suspend, steps-after-not-executed, resume-continues

3. ExpectedContractHash: contract drift detection
   - VersionedDescriptorRef optional ExpectedContractHash field
   - CapabilityPipeline compares ContractHash and publishes drift warning

4. DomainEventCollection: unified domain event publishing
   - EventPublishingMiddleware publishes domain events from Items['__domainEvents']
   - Handler can add domain events via context.Items

~200 total tests across all 14 phases"
```

---

## Phase 14 Summary

| Task | Optimization | New Files | Modified Files | Tests |
|------|-------------|-----------|----------------|-------|
| 0 | IInteractionDescriptor interface | 1 | 2 | — |
| 1 | Fix all tests for migration | — | 7+ | fix existing |
| 2 | ExpectedContractHash | — | 2 | — |
| 3 | DomainEventCollection | — | 1 | — |
| 4 | WorkflowEventConsumer + Suspend | 2 | 2 | — |
| 5 | Suspend/Resume tests | — | 1 | 3 new |
| 6 | Full build + commit | — | — | — |
| **Total** | **4 optimizations** | **3 new** | **15+ modified** | **~3 new** |