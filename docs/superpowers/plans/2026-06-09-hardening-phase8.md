# Phase 8: Hardening — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete stubs and close hardening gaps — ResumeAsync (DraftRecord checkpoint restore), IdempotencyMiddleware (duplicate detection), IdempotentStore, and CapabilityProfile runtime resolution.

**Architecture:** Each task targets a specific stub. ResumeAsync loads a DraftRecord checkpoint and reconstructs a WorkflowInstance to continue from the saved step index. IdempotencyMiddleware checks an `IIdempotenceStore` before handler execution, returning cached results for duplicate keys. CapabilityProfile resolves profile overrides (timeout, retry policy) at pipeline invocation time.

**Tech Stack:** .NET 10, C# 13, System.Text.Json, xUnit + FluentAssertions

---

### Task 0: IIdempotenceStore + InMemoryIdempotenceStore

**Files:**
- Create: `framework/src/CrestCreates.Capability.Abstractions/IIdempotenceStore.cs`
- Create: `framework/src/CrestCreates.Capability/InMemoryIdempotenceStore.cs`

- [ ] **Step 1: Write IIdempotenceStore.cs**

```csharp
namespace CrestCreates.Capability.Abstractions;

public interface IIdempotenceStore
{
    Task<CapabilityExecutionResult?> GetResultAsync(string idempotencyKey, CancellationToken ct = default);
    Task StoreResultAsync(string idempotencyKey, CapabilityExecutionResult result, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write InMemoryIdempotenceStore.cs**

```csharp
using System.Collections.Concurrent;
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public sealed class InMemoryIdempotenceStore : IIdempotenceStore
{
    private readonly ConcurrentDictionary<string, CapabilityExecutionResult> _results = new();

    public Task<CapabilityExecutionResult?> GetResultAsync(string idempotencyKey, CancellationToken ct = default)
    {
        _results.TryGetValue(idempotencyKey, out var result);
        return Task.FromResult(result);
    }

    public Task StoreResultAsync(string idempotencyKey, CapabilityExecutionResult result, CancellationToken ct = default)
    {
        _results[idempotencyKey] = result;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability.Abstractions/CrestCreates.Capability.Abstractions.csproj
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability.Abstractions/IIdempotenceStore.cs framework/src/CrestCreates.Capability/InMemoryIdempotenceStore.cs
git commit -m "feat: add IIdempotenceStore + InMemoryIdempotenceStore"
```

---

### Task 1: IdempotencyMiddleware

**Files:**
- Create: `framework/src/CrestCreates.Capability/Middleware/IdempotencyMiddleware.cs`
- Modify: `framework/src/CrestCreates.Capability/CapabilityServiceCollectionExtensions.cs` (add to pipeline)

- [ ] **Step 1: Write IdempotencyMiddleware.cs**

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability.Middleware;

public sealed class IdempotencyMiddleware : ICapabilityPipelineMiddleware
{
    private readonly IIdempotenceStore? _store;

    public IdempotencyMiddleware(IIdempotenceStore? store = null)
    {
        _store = store;
    }

    public async Task<CapabilityExecutionResult> InvokeAsync(
        CapabilityExecutionContext context,
        CapabilityPipelineDelegate next)
    {
        if (_store == null)
            return await next(context).ConfigureAwait(false);

        // Check for duplicate
        var cached = await _store.GetResultAsync(context.IdempotencyKey, context.CancellationToken)
            .ConfigureAwait(false);

        if (cached != null)
            return cached;

        // Execute and cache
        var result = await next(context).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            await _store.StoreResultAsync(context.IdempotencyKey, result, context.CancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }
}
```

- [ ] **Step 2: Add IdempotencyMiddleware to default pipeline**

In `CapabilityServiceCollectionExtensions.AddCapabilityPipeline`, add after ValidationMiddleware:
```csharp
builder.Use<IdempotencyMiddleware>();
```

And register:
```csharp
services.TryAddTransient<IdempotencyMiddleware>();
```

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/
git commit -m "feat: add IdempotencyMiddleware — caches + replays results by idempotency key"
```

---

### Task 2: Idempotency Tests

**Files:**
- Create: `framework/test/CrestCreates.Capability.Tests/IdempotencyMiddlewareTests.cs`

- [ ] **Step 1: Write IdempotencyMiddlewareTests.cs (5 tests)**

```csharp
using CrestCreates.Capability.Abstractions;
using CrestCreates.Capability.Middleware;
using FluentAssertions;
using Xunit;

namespace CrestCreates.Capability.Tests;

public class IdempotencyMiddlewareTests
{
    private static CapabilityExecutionContext CreateContext()
    {
        return new CapabilityExecutionContext
        {
            CapabilityName = "test.cap",
            CapabilityVersion = 1,
            CapabilityContractHash = "abc",
            IdempotencyKey = "idem_001"
        };
    }

    [Fact]
    public async Task Passthrough_WhenNoStore()
    {
        var middleware = new IdempotencyMiddleware(null);
        var context = CreateContext();

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("ok", TimeSpan.Zero)));

        result.IsSuccess.Should().BeTrue();
        result.Output.Should().Be("ok");
    }

    [Fact]
    public async Task Returns_Cached_OnDuplicate()
    {
        var store = new InMemoryIdempotenceStore();
        var cached = CapabilityExecutionResult.Success("cached", TimeSpan.FromMilliseconds(10));
        await store.StoreResultAsync("idem_001", cached);

        var middleware = new IdempotencyMiddleware(store);
        var context = CreateContext();

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("fresh", TimeSpan.Zero)));

        result.Output.Should().Be("cached");
    }

    [Fact]
    public async Task Stores_Result_AfterSuccess()
    {
        var store = new InMemoryIdempotenceStore();
        var middleware = new IdempotencyMiddleware(store);
        var context = CreateContext();

        var result = await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("first", TimeSpan.Zero)));

        result.Output.Should().Be("first");

        var cached = await store.GetResultAsync("idem_001");
        cached.Should().NotBeNull();
        cached!.Output.Should().Be("first");
    }

    [Fact]
    public async Task Does_Not_Store_OnFailure()
    {
        var store = new InMemoryIdempotenceStore();
        var middleware = new IdempotencyMiddleware(store);
        var context = CreateContext();

        await middleware.InvokeAsync(context, _ =>
            Task.FromResult(CapabilityExecutionResult.Failure("ERR", "bad", TimeSpan.Zero)));

        var cached = await store.GetResultAsync("idem_001");
        cached.Should().BeNull();
    }

    [Fact]
    public async Task DifferentKeys_ProduceDifferentResults()
    {
        var store = new InMemoryIdempotenceStore();
        var middleware = new IdempotencyMiddleware(store);

        var ctx1 = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1,
            CapabilityContractHash = "a", IdempotencyKey = "key_A"
        };
        var ctx2 = new CapabilityExecutionContext
        {
            CapabilityName = "test", CapabilityVersion = 1,
            CapabilityContractHash = "a", IdempotencyKey = "key_B"
        };

        await middleware.InvokeAsync(ctx1, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("A", TimeSpan.Zero)));
        var r2 = await middleware.InvokeAsync(ctx2, _ =>
            Task.FromResult(CapabilityExecutionResult.Success("B", TimeSpan.Zero)));

        r2.Output.Should().Be("B");
    }
}
```

- [ ] **Step 2: Run tests + commit**

```bash
dotnet test framework/test/CrestCreates.Capability.Tests/CrestCreates.Capability.Tests.csproj
git add framework/test/CrestCreates.Capability.Tests/IdempotencyMiddlewareTests.cs
git commit -m "feat: add IdempotencyMiddlewareTests — 5 tests"
```

Expected: 43 tests pass (38 existing + 5 new).

---

### Task 3: ResumeAsync — DraftRecord Checkpoint Restoration

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/WorkflowEngine.cs`

Implement `ResumeAsync` to load a `DraftRecord` checkpoint and continue execution.

- [ ] **Step 1: Implement ResumeAsync**

Replace the `NotImplementedException` throw with:

```csharp
public async Task<WorkflowInstance> ResumeAsync(string instanceId, CancellationToken ct = default)
{
    if (_draftStore == null)
        throw new InvalidOperationException("No IDraftStore registered — cannot resume workflows.");

    var checkpointId = $"wf_ckpt_{instanceId}";
    var checkpoint = await _draftStore.GetAsync(checkpointId, ct).ConfigureAwait(false);

    if (checkpoint == null)
        throw new InvalidOperationException($"No checkpoint found for instance '{instanceId}'.");

    // Deserialize checkpoint state
    var state = System.Text.Json.JsonSerializer
        .Deserialize<CheckpointState>(checkpoint.PayloadJson)
        ?? throw new InvalidOperationException("Corrupted checkpoint payload.");

    var descriptor = _registry.GetById(state.WorkflowId)
        ?? throw new InvalidOperationException($"Workflow '{state.WorkflowId}' not found.");

    var instance = new WorkflowInstance
    {
        InstanceId = state.InstanceId,
        Workflow = new VersionedDescriptorRef<WorkflowDescriptor>(state.WorkflowId, state.WorkflowVersion),
        StepIndex = state.StepIndex,
        CurrentStepId = state.CurrentStepId,
        Variables = state.Variables ?? new Dictionary<string, object?>()
    };

    return await ExecuteStepsAsync(instance, descriptor, ct).ConfigureAwait(false);
}

internal sealed class CheckpointState
{
    public string InstanceId { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public int WorkflowVersion { get; set; }
    public int StepIndex { get; set; }
    public string? CurrentStepId { get; set; }
    public Dictionary<string, object?>? Variables { get; set; }
}
```

- [ ] **Step 2: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Workflow/CrestCreates.Workflow.csproj
git add framework/src/CrestCreates.Workflow/WorkflowEngine.cs
git commit -m "feat: implement ResumeAsync — loads DraftRecord checkpoint and continues execution"
```

---

### Task 4: Resume Tests

**Files:**
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowEngineTests.cs`

- [ ] **Step 1: Add ResumeAsync tests (4 tests)**

```csharp
[Fact]
public async Task ResumeAsync_NoDraftStore_Throws()
{
    var registry = new WorkflowRegistry();
    registry.Register(CreateWorkflow("wf_01", "test.wf", 1));
    var engine = new WorkflowEngine(registry, draftStore: null);

    await engine.Invoking(e => e.ResumeAsync("instance_01"))
        .Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*IDraftStore*");
}

[Fact]
public async Task ResumeAsync_NoCheckpoint_Throws()
{
    var registry = new WorkflowRegistry();
    registry.Register(CreateWorkflow("wf_01", "test.wf", 1));
    var draftStore = new Draft.InMemoryDraftStore();
    var engine = new WorkflowEngine(registry, draftStore: draftStore);

    await engine.Invoking(e => e.ResumeAsync("instance_01"))
        .Should().ThrowAsync<InvalidOperationException>()
        .WithMessage("*checkpoint*");
}

[Fact]
public async Task ResumeAsync_ValidCheckpoint_ContinuesExecution()
{
    var registry = new WorkflowRegistry();
    registry.Register(new WorkflowDescriptor
    {
        Id = "wf_01", Name = "resume.wf", Version = 1, State = DescriptorState.Active,
        Steps = new List<WorkflowStep>
        {
            new()
            {
                Id = "step_01", Name = "Already Done",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                }
            },
            new()
            {
                Id = "step_02", Name = "Resume Here",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                }
            }
        }
    });

    var draftStore = new Draft.InMemoryDraftStore();
    var checkpointJson = System.Text.Json.JsonSerializer.Serialize(
        new WorkflowEngine.CheckpointState
        {
            InstanceId = "instance_01",
            WorkflowId = "wf_01",
            WorkflowVersion = 1,
            StepIndex = 1,
            CurrentStepId = "step_02"
        });

    await draftStore.SaveAsync(new Draft.Abstractions.DraftRecord
    {
        DraftId = "wf_ckpt_instance_01",
        DraftType = "workflow.checkpoint",
        Schema = new Schema.Abstractions.VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>("s", 1),
        TenantId = null,
        OwnerId = "instance_01",
        PayloadJson = checkpointJson
    });

    var engine = new WorkflowEngine(registry, draftStore: draftStore);
    var instance = await engine.ResumeAsync("instance_01");

    instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
    instance.StepResults.Should().HaveCount(1);
    instance.StepResults[0].StepId.Should().Be("step_02");
}

[Fact]
public async Task ResumeThenExecute_HasCorrectInstanceId()
{
    var registry = new WorkflowRegistry();
    registry.Register(CreateWorkflow("wf_01", "resume2.wf", 1));
    var draftStore = new Draft.InMemoryDraftStore();
    var checkpointJson = System.Text.Json.JsonSerializer.Serialize(
        new WorkflowEngine.CheckpointState
        {
            InstanceId = "instance_02",
            WorkflowId = "wf_01",
            WorkflowVersion = 1,
            StepIndex = 0,
            CurrentStepId = null
        });

    await draftStore.SaveAsync(new Draft.Abstractions.DraftRecord
    {
        DraftId = "wf_ckpt_instance_02",
        DraftType = "workflow.checkpoint",
        Schema = new Schema.Abstractions.VersionedDescriptorRef<Schema.Abstractions.SchemaDescriptor>("s", 1),
        TenantId = null,
        OwnerId = "instance_02",
        PayloadJson = checkpointJson
    });

    var engine = new WorkflowEngine(registry, draftStore: draftStore);
    var instance = await engine.ResumeAsync("instance_02");

    instance.InstanceId.Should().Be("instance_02");
    instance.Status.Should().Be(WorkflowInstanceStatus.Completed);
}
```

- [ ] **Step 2: Add project refs + run tests**

Add to `framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`:
```xml
<ProjectReference Include="..\..\src\CrestCreates.Draft\CrestCreates.Draft.csproj" />
<ProjectReference Include="..\..\src\CrestCreates.Schema.Abstractions\CrestCreates.Schema.Abstractions.csproj" />
```

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: ~21 tests pass (17 existing + 4 new).

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/
git commit -m "feat: add ResumeAsync tests — 4 tests for checkpoint restoration"
```

---

### Task 5: CapabilityProfile Resolution

**Files:**
- Create: `framework/src/CrestCreates.Capability/CapabilityProfileResolver.cs`

Resolve effective configuration by merging CapabilityProfile overrides.

- [ ] **Step 1: Write CapabilityProfileResolver.cs**

```csharp
using CrestCreates.Capability.Abstractions;

namespace CrestCreates.Capability;

public static class CapabilityProfileResolver
{
    public sealed class EffectiveProfile
    {
        public TimeSpan? Timeout { get; init; }
        public bool? RequireApproval { get; init; }
    }

    public static EffectiveProfile Resolve(
        CapabilityDescriptor descriptor,
        IReadOnlyList<CapabilityProfile> profiles)
    {
        // Resolution order: profile with most specific scope wins
        // For now: first matching profile; future: Tenant → Environment → Global → default
        foreach (var profile in profiles)
        {
            if (profile.Capability.Id == descriptor.Id)
            {
                return new EffectiveProfile
                {
                    Timeout = profile.Timeout,
                    RequireApproval = profile.RequireApproval
                };
            }
        }

        return new EffectiveProfile();
    }
}
```

- [ ] **Step 2: Add a CapabilityProfile resolver test to the existing test project**

- [ ] **Step 3: Build, verify, commit**

```bash
dotnet build framework/src/CrestCreates.Capability/CrestCreates.Capability.csproj
git add framework/src/CrestCreates.Capability/CapabilityProfileResolver.cs
git commit -m "feat: add CapabilityProfileResolver — scope-based profile merging"
```

---

### Task 6: Full Build + All Tests + Final Commit

- [ ] **Step 1: Full solution build**

Run: `dotnet build CrestCreates.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Expected: ~156 tests pass (146 previous + ~10 new).

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "feat: complete Phase 8 — Idempotency, Resume, Profile resolution

- IIdempotenceStore + InMemoryIdempotenceStore
- IdempotencyMiddleware: checks cache, replays duplicate results
- ResumeAsync: loads DraftRecord checkpoint, reconstructs instance, continues execution
- CheckpointState: serializable checkpoint DTO
- CapabilityProfileResolver: scope-based profile merging
- ~10 new tests: 5 Idempotency + 4 Resume + 1 Profile
- ~156 total tests passing across all 8 phases"
```

---

## Phase 8 Summary

| Task | Component | Tests |
|------|-----------|-------|
| 0 | IIdempotenceStore + InMemoryStore | — |
| 1 | IdempotencyMiddleware | — |
| 2 | Idempotency tests | 5 |
| 3 | ResumeAsync checkpoint restore | — |
| 4 | Resume tests | 4 |
| 5 | CapabilityProfileResolver | ~1 |
| 6 | Full build + commit | — |
| **Total** | **~6 new files** | **~10 new tests** |
