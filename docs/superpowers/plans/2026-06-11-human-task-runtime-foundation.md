# Phase 5 — HumanTask Runtime Foundation: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement HumanTask Runtime Foundation — create real `HumanTaskInstance` objects via `IHumanTaskRuntime`, publish `HumanTaskCompletedEvent` with `HumanTaskInstanceId`, and trigger existing Workflow continuation loop.

**Architecture:** New abstractions (HumanTaskInstance, requests, store/runtime interfaces) in `HumanTask.Abstractions`. In-memory store + runtime implementation in `HumanTask`. Workflow `HumanTaskStepExecutor` injects `IHumanTaskRuntime` to create real instances; subscriber passes `HumanTaskInstanceId` for continuation.

**Tech Stack:** .NET 10, C# 12, ConcurrentDictionary, ILocalEventBus, xUnit + FluentAssertions + Moq.

---

### Phase 0: Project Setup

### Task 0.1: Add Moq to HumanTask.Tests.csproj

**Files:**
- Modify: `framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj`

- [ ] **Step 1: Add Moq package reference**

```xml
<PackageReference Include="Moq" />
```

Add inside the `<ItemGroup>` that contains other `PackageReference` entries. The version is centrally managed via `Directory.Packages.props`.

- [ ] **Step 2: Verify restore**

Run: `dotnet restore framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.HumanTask.Tests/CrestCreates.HumanTask.Tests.csproj
git commit -m "test: add Moq to HumanTask.Tests"
```

---

### Task 0.2: Add EventBus.Abstractions reference to HumanTask.csproj

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj`

- [ ] **Step 1: Add ProjectReference**

Add inside the `<ItemGroup>` with other `ProjectReference` entries:

```xml
<ProjectReference Include="..\CrestCreates.EventBus.Abstractions\CrestCreates.EventBus.Abstractions.csproj" />
```

The existing ItemGroup already contains:
```xml
<ItemGroup>
  <ProjectReference Include="..\CrestCreates.HumanTask.Abstractions\CrestCreates.HumanTask.Abstractions.csproj" />
  <ProjectReference Include="..\CrestCreates.Metadata.Abstractions\CrestCreates.Metadata.Abstractions.csproj" />
  <ProjectReference Include="..\CrestCreates.Metadata\CrestCreates.Metadata.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Verify restore**

Run: `dotnet restore framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj
git commit -m "feat: add EventBus.Abstractions dependency to HumanTask"
```

---

### Task 0.3: Add HumanTask runtime reference to Workflow.Tests.csproj

**Files:**
- Modify: `framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`

- [ ] **Step 1: Add ProjectReference**

Add inside the `<ItemGroup>` with other `ProjectReference` entries:

```xml
<ProjectReference Include="..\..\src\CrestCreates.HumanTask\CrestCreates.HumanTask.csproj" />
```

- [ ] **Step 2: Verify restore**

Run: `dotnet restore framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj
git commit -m "test: add HumanTask runtime reference to Workflow.Tests"
```

---

### Phase 1: Abstractions — New Models and Interfaces

### Task 1.1: Create HumanTaskInstanceStatus.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskInstanceStatus.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public enum HumanTaskInstanceStatus
{
    Created,
    Assigned,
    Completed,
    Cancelled
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskInstanceStatus.cs
git commit -m "feat: add HumanTaskInstanceStatus enum"
```

---

### Task 1.2: Create HumanTaskInstance.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs`

- [ ] **Step 1: Write the file**

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

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskInstance.cs
git commit -m "feat: add HumanTaskInstance model"
```

---

### Task 1.3: Create HumanTaskCreationRequest.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCreationRequest.cs`

- [ ] **Step 1: Write the file**

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
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCreationRequest.cs
git commit -m "feat: add HumanTaskCreationRequest"
```

---

### Task 1.4: Create HumanTaskCompletionRequest.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletionRequest.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletionRequest
{
    public string HumanTaskInstanceId { get; init; } = default!;
    public string Outcome { get; init; } = default!;
    public object? Result { get; init; }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletionRequest.cs
git commit -m "feat: add HumanTaskCompletionRequest"
```

---

### Task 1.5: Create IHumanTaskInstanceStore.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskInstanceStore.cs`

- [ ] **Step 1: Write the file**

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

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskInstanceStore.cs
git commit -m "feat: add IHumanTaskInstanceStore interface"
```

---

### Task 1.6: Create IHumanTaskRuntime.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskRuntime.cs`

- [ ] **Step 1: Write the file**

```csharp
namespace CrestCreates.HumanTask.Abstractions;

public interface IHumanTaskRuntime
{
    Task<HumanTaskInstance> CreateAsync(HumanTaskCreationRequest request, CancellationToken ct = default);
    Task<HumanTaskInstance> CompleteAsync(HumanTaskCompletionRequest request, CancellationToken ct = default);
    Task<HumanTaskInstance> CancelAsync(string instanceId, string reason, CancellationToken ct = default);
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/IHumanTaskRuntime.cs
git commit -m "feat: add IHumanTaskRuntime interface"
```

---

### Task 1.7: Modify HumanTaskCompletedEvent.cs (+2 fields)

**Files:**
- Modify: `framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs`

- [ ] **Step 1: Add HumanTaskInstanceId and HumanTaskVersion fields**

The current file is:
```csharp
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletedEvent : ILocalEvent
{
    public string HumanTaskId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
```

Add two new properties after `HumanTaskId`:

```csharp
using CrestCreates.EventBus.Abstractions;

namespace CrestCreates.HumanTask.Abstractions;

public sealed class HumanTaskCompletedEvent : ILocalEvent
{
    public string HumanTaskId { get; init; } = string.Empty;
    public string HumanTaskInstanceId { get; init; } = string.Empty;
    public int HumanTaskVersion { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.HumanTask.Abstractions/HumanTaskCompletedEvent.cs
git commit -m "feat: add HumanTaskInstanceId and HumanTaskVersion to HumanTaskCompletedEvent"
```

---

### Task 1.8: Build verification

- [ ] **Step 1: Build the solution**

Run: `dotnet build`
Expected: Zero errors. (All abstractions compile cleanly.)

---

### Phase 2: InMemory Store — Implementation + Tests (TDD)

### Task 2.1: Write InMemoryHumanTaskInstanceStoreTests.cs

**Files:**
- Create: `framework/test/CrestCreates.HumanTask.Tests/InMemoryHumanTaskInstanceStoreTests.cs`

- [ ] **Step 1: Write the test file**

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
        var store = new InMemoryHumanTaskInstanceStore();

        var created = new HumanTaskInstance
        {
            Id = "inst-01", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Created,
            AssigneeUserId = "user-a", CreatedAt = DateTimeOffset.UtcNow
        };
        var assigned = new HumanTaskInstance
        {
            Id = "inst-02", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Assigned,
            AssigneeUserId = "user-a", CreatedAt = DateTimeOffset.UtcNow
        };
        var completed = new HumanTaskInstance
        {
            Id = "inst-03", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Completed,
            AssigneeUserId = "user-a", CreatedAt = DateTimeOffset.UtcNow
        };
        var cancelled = new HumanTaskInstance
        {
            Id = "inst-04", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Cancelled,
            AssigneeUserId = "user-a", CreatedAt = DateTimeOffset.UtcNow
        };
        var otherUser = new HumanTaskInstance
        {
            Id = "inst-05", HumanTaskId = "ht_01", HumanTaskVersion = 1,
            Status = HumanTaskInstanceStatus.Assigned,
            AssigneeUserId = "user-b", CreatedAt = DateTimeOffset.UtcNow
        };

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
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~InMemoryHumanTaskInstanceStoreTests" --no-build`
Expected: FAIL — `InMemoryHumanTaskInstanceStore` type not found.

---

### Task 2.2: Implement InMemoryHumanTaskInstanceStore.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs`

> Pattern: mirrors `framework/src/CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs` — ConcurrentDictionary, direct upsert, no deep copy.

- [ ] **Step 1: Write the implementation**

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

- [ ] **Step 2: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~InMemoryHumanTaskInstanceStoreTests"`
Expected: 1 test passes.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/InMemoryHumanTaskInstanceStore.cs
git add framework/test/CrestCreates.HumanTask.Tests/InMemoryHumanTaskInstanceStoreTests.cs
git commit -m "feat: add InMemoryHumanTaskInstanceStore with tests"
```

---

### Phase 3: CompletionOutcomeMatcher

### Task 3.1: Implement CompletionOutcomeMatcher.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask/CompletionOutcomeMatcher.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using CrestCreates.HumanTask.Abstractions;

namespace CrestCreates.HumanTask;

internal static class CompletionOutcomeMatcher
{
    public static bool Matches(CompletionOutcome outcome, string requestOutcome)
        => outcome.Condition.ToString().Equals(requestOutcome, StringComparison.OrdinalIgnoreCase);

    public static CompletionOutcome Resolve(HumanTaskDescriptor descriptor, string outcome)
    {
        var matches = descriptor.Outcomes
            .Where(o => Matches(o, outcome))
            .ToList();

        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"Outcome '{outcome}' not found in HumanTask '{descriptor.Id}' v{descriptor.Version}.");

        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Multiple outcomes match '{outcome}' in HumanTask '{descriptor.Id}'. " +
                "Identifier-based matching not yet supported.");

        var matched = matches[0];
        if (matched.Condition == CompletionCondition.CustomExpression)
            throw new NotSupportedException(
                "CustomExpression outcome evaluation is not supported in Phase 5.");

        return matched;
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj`
Expected: Zero errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/CompletionOutcomeMatcher.cs
git commit -m "feat: add CompletionOutcomeMatcher internal helper"
```

---

### Phase 4: DefaultHumanTaskRuntime — Implementation + Tests (TDD)

### Task 4.1: Write HumanTaskRuntimeTests.cs (failing)

**Files:**
- Create: `framework/test/CrestCreates.HumanTask.Tests/HumanTaskRuntimeTests.cs`

- [ ] **Step 1: Write the full test file**

```csharp
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Metadata;
using CrestCreates.Metadata.Abstractions;
using FluentAssertions;
using Moq;
using Xunit;

namespace CrestCreates.HumanTask.Tests;

public class HumanTaskRuntimeTests
{
    private static HumanTaskDescriptor CreateDescriptor(string id, string name, int version,
        params CompletionCondition[] conditions)
    {
        return new HumanTaskDescriptor
        {
            Id = id,
            Name = name,
            Version = version,
            Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
            Outcomes = conditions
                .Select(c => new CompletionOutcome { Condition = c })
                .ToList()
        };
    }

    private class TestHumanTaskProvider : IDescriptorProvider<HumanTaskDescriptor>
    {
        private readonly List<HumanTaskDescriptor> _descriptors;
        public TestHumanTaskProvider(List<HumanTaskDescriptor> descriptors) => _descriptors = descriptors;
        public IReadOnlyList<HumanTaskDescriptor> GetDescriptors() => _descriptors;
    }

    private static HumanTaskRegistry CreateRegistry(params HumanTaskDescriptor[] descriptors)
    {
        var engine = new RegistryValidationEngine<HumanTaskDescriptor>(
            Array.Empty<IRegistryValidator<HumanTaskDescriptor>>());
        var registry = new HumanTaskRegistry(engine);
        registry.Build([new TestHumanTaskProvider(descriptors.ToList())]);
        return registry;
    }

    private static (DefaultHumanTaskRuntime runtime, InMemoryHumanTaskInstanceStore store, Mock<ILocalEventBus> eventBusMock)
        CreateRuntime(HumanTaskRegistry registry, Mock<ILocalEventBus>? busMock = null)
    {
        var store = new InMemoryHumanTaskInstanceStore();
        var eventBus = busMock ?? new Mock<ILocalEventBus>();
        var runtime = new DefaultHumanTaskRuntime(registry, store, eventBus.Object);
        return (runtime, store, eventBus);
    }

    [Fact]
    public async Task CreateAsync_Creates_Instance_From_Descriptor()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var (runtime, store, _) = CreateRuntime(registry);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            Input = new { key = "value" }
        });

        instance.Id.Should().NotBeNullOrEmpty();
        instance.HumanTaskId.Should().Be("ht_01");
        instance.HumanTaskVersion.Should().Be(1);
        instance.Status.Should().Be(HumanTaskInstanceStatus.Created);
        instance.Input.Should().NotBeNull();

        var stored = await store.GetByIdAsync(instance.Id);
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(instance.Id);
    }

    [Fact]
    public async Task CreateAsync_Throws_When_Descriptor_Not_Found()
    {
        var registry = CreateRegistry();
        var (runtime, _, _) = CreateRuntime(registry);

        await runtime.Invoking(r => r.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "nonexistent"
        })).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CompleteAsync_Completes_Instance_And_Publishes_Event()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve, CompletionCondition.Reject));
        var eventBusMock = new Mock<ILocalEventBus>();
        var (runtime, store, _) = CreateRuntime(registry, eventBusMock);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01",
            Input = new { key = "value" }
        });

        var completed = await runtime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "Approve",
            Result = new { Score = 95 }
        });

        completed.Status.Should().Be(HumanTaskInstanceStatus.Completed);
        completed.Outcome.Should().Be("Approve");
        completed.Output.Should().NotBeNull();
        completed.CompletedAt.Should().NotBeNull();

        eventBusMock.Verify(
            b => b.PublishAsync(
                It.Is<HumanTaskCompletedEvent>(e =>
                    e.HumanTaskInstanceId == instance.Id &&
                    e.HumanTaskId == "ht_01" &&
                    e.HumanTaskVersion == 1 &&
                    e.Outcome == "Approve" &&
                    e.Result != null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_Throws_When_Outcome_Invalid()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve));
        var (runtime, store, _) = CreateRuntime(registry);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        await runtime.Invoking(r => r.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "NonExistent"
        })).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CompleteAsync_Throws_When_Instance_Already_Completed()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1,
            CompletionCondition.Approve));
        var (runtime, store, _) = CreateRuntime(registry);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        await runtime.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "Approve"
        });

        await runtime.Invoking(r => r.CompleteAsync(new HumanTaskCompletionRequest
        {
            HumanTaskInstanceId = instance.Id,
            Outcome = "Approve"
        })).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancelAsync_Cancels_Instance()
    {
        var registry = CreateRegistry(CreateDescriptor("ht_01", "manager.approval", 1));
        var (runtime, store, _) = CreateRuntime(registry);

        var instance = await runtime.CreateAsync(new HumanTaskCreationRequest
        {
            HumanTaskId = "ht_01"
        });

        var cancelled = await runtime.CancelAsync(instance.Id, "No longer needed");

        cancelled.Status.Should().Be(HumanTaskInstanceStatus.Cancelled);
        cancelled.CancellationReason.Should().Be("No longer needed");
        cancelled.CancelledAt.Should().NotBeNull();

        var stored = await store.GetByIdAsync(instance.Id);
        stored!.Status.Should().Be(HumanTaskInstanceStatus.Cancelled);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~HumanTaskRuntimeTests" --no-build`
Expected: FAIL — `DefaultHumanTaskRuntime` type not found.

---

### Task 4.2: Implement DefaultHumanTaskRuntime.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask/DefaultHumanTaskRuntime.cs`

- [ ] **Step 1: Write the implementation**

```csharp
using CrestCreates.EventBus.Abstractions;
using CrestCreates.HumanTask.Abstractions;

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
            throw new InvalidOperationException(
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
            throw new InvalidOperationException(
                $"HumanTask instance '{instanceId}' not found.");

        if (instance.Status == HumanTaskInstanceStatus.Completed ||
            instance.Status == HumanTaskInstanceStatus.Cancelled)
            throw new InvalidOperationException(
                $"HumanTask instance '{instanceId}' is already '{instance.Status}' " +
                "and cannot be cancelled.");

        instance.Status = HumanTaskInstanceStatus.Cancelled;
        instance.CancellationReason = reason;
        instance.CancelledAt = DateTimeOffset.UtcNow;

        await _store.SaveAsync(instance, ct).ConfigureAwait(false);
        return instance;
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~HumanTaskRuntimeTests"`
Expected: 6 tests pass.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/DefaultHumanTaskRuntime.cs
git add framework/test/CrestCreates.HumanTask.Tests/HumanTaskRuntimeTests.cs
git commit -m "feat: add DefaultHumanTaskRuntime with tests"
```

---

### Phase 5: DI Registration

### Task 5.1: Implement HumanTaskServiceCollectionExtensions.cs

**Files:**
- Create: `framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs`

- [ ] **Step 1: Write the implementation**

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
        services.TryAddSingleton<IHumanTaskRuntime, DefaultHumanTaskRuntime>();
        return services;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build framework/src/CrestCreates.HumanTask/CrestCreates.HumanTask.csproj`
Expected: Zero errors.

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.HumanTask/HumanTaskServiceCollectionExtensions.cs
git commit -m "feat: add HumanTaskServiceCollectionExtensions for DI registration"
```

---

### Phase 6: Workflow Changes

### Task 6.1: Modify HumanTaskStepExecutor.cs

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/HumanTaskStepExecutor.cs`

- [ ] **Step 1: Add IHumanTaskRuntime constructor injection**

The current file is:
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

Replace with:

```csharp
using CrestCreates.HumanTask.Abstractions;
using CrestCreates.Workflow.Abstractions;

namespace CrestCreates.Workflow;

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
        }, ct).ConfigureAwait(false);

        return new StepExecutionResult(
            StepExecutionStatus.Suspended,
            WaitingHumanTaskId: instance.Id);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add framework/src/CrestCreates.Workflow/HumanTaskStepExecutor.cs
git commit -m "feat: inject IHumanTaskRuntime into HumanTaskStepExecutor"
```

---

### Task 6.2: Modify HumanTaskCompletedWorkflowSubscriber.cs

**Files:**
- Modify: `framework/src/CrestCreates.Workflow/HumanTaskCompletedWorkflowSubscriber.cs`

- [ ] **Step 1: Change evt.HumanTaskId → evt.HumanTaskInstanceId**

The current `HandleAsync` body is:
```csharp
return _continuationService.ContinueAsync(
    new WorkflowContinuationRequest
    {
        HumanTaskId = evt.HumanTaskId,
        Outcome = evt.Outcome,
        Result = evt.Result
    }, ct);
```

Change `evt.HumanTaskId` to `evt.HumanTaskInstanceId`:

```csharp
return _continuationService.ContinueAsync(
    new WorkflowContinuationRequest
    {
        HumanTaskId = evt.HumanTaskInstanceId,
        Outcome = evt.Outcome,
        Result = evt.Result
    }, ct);
```

The full file (no other changes):

```csharp
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
                HumanTaskId = evt.HumanTaskInstanceId,
                Outcome = evt.Outcome,
                Result = evt.Result
            }, ct);
    }
}
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build`
Expected: Zero errors. (All projects should compile.)

- [ ] **Step 3: Commit**

```bash
git add framework/src/CrestCreates.Workflow/HumanTaskCompletedWorkflowSubscriber.cs
git commit -m "feat: switch subscriber to use HumanTaskInstanceId for continuation"
```

---

### Phase 7: Workflow.Tests — Executor + E2E

### Task 7.1: Add executor unit test to WorkflowContinuationTests.cs

**Files:**
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs`

- [ ] **Step 1: Add the executor unit test**

Add the following test method at the end of the `WorkflowContinuationTests` class (before the closing `}`):

```csharp
[Fact]
public async Task HumanTaskStepExecutor_Creates_Instance_And_Returns_Suspended()
{
    var mockRuntime = new Mock<IHumanTaskRuntime>();
    mockRuntime
        .Setup(r => r.CreateAsync(
            It.IsAny<HumanTaskCreationRequest>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync((HumanTaskCreationRequest req, CancellationToken _) =>
            new HumanTaskInstance
            {
                Id = "inst-001",
                HumanTaskId = req.HumanTaskId,
                HumanTaskVersion = 1,
                Status = HumanTaskInstanceStatus.Created,
                WorkflowInstanceId = req.WorkflowInstanceId,
                WorkflowStepId = req.WorkflowStepId,
                Input = req.Input
            });

    var executor = new HumanTaskStepExecutor(mockRuntime.Object);

    var descriptor = new WorkflowDescriptor
    {
        Id = "wf_01", Name = "test.wf", Version = 1,
        State = DescriptorState.Active,
        Steps = new List<WorkflowStep>
        {
            new()
            {
                Id = "step_01", Name = "Approval",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
                }
            }
        }
    };
    var instance = new WorkflowInstance
    {
        Workflow = new VersionedDescriptorRef<WorkflowDescriptor>("wf_01", 1),
        Variables = { ["request"] = "test-data" }
    };

    var context = new WorkflowExecutionContext(descriptor, instance, descriptor.Steps[0]);
    var result = await executor.ExecuteAsync(context, CancellationToken.None);

    result.Status.Should().Be(StepExecutionStatus.Suspended);
    result.WaitingHumanTaskId.Should().Be("inst-001");

    mockRuntime.Verify(
        r => r.CreateAsync(
            It.Is<HumanTaskCreationRequest>(req =>
                req.HumanTaskId == "ht_01" &&
                req.WorkflowInstanceId == instance.InstanceId &&
                req.WorkflowStepId == "step_01" &&
                req.Input != null),
            It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 2: Add required using directives at top of file**

Add these usings if not already present:
```csharp
using CrestCreates.HumanTask;
using CrestCreates.HumanTask.Abstractions;
using Moq;
```

The file already has `using CrestCreates.HumanTask.Abstractions;` and `using Xunit;` and `using FluentAssertions;`. Check whether `using Moq;` is present; if not, add it.

- [ ] **Step 3: Run test**

Run: `dotnet test --filter "FullyQualifiedName~WorkflowContinuationTests.HumanTaskStepExecutor_Creates_Instance_And_Returns_Suspended"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs
git commit -m "test: add HumanTaskStepExecutor unit test"
```

---

### Task 7.2: Add E2E test to WorkflowContinuationTests.cs

**Files:**
- Modify: `framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs`

- [ ] **Step 1: Add the E2E test**

Add the following test method after the executor test in the `WorkflowContinuationTests` class:

```csharp
[Fact]
public async Task Workflow_HumanTask_EndToEnd_Complete_Task_Resumes_Workflow()
{
    // Build HumanTask descriptors with outcomes
    var htDescriptor = new HumanTaskDescriptor
    {
        Id = "ht_01", Name = "approval.task", Version = 1,
        Interaction = new VersionedDescriptorRef<IInteractionDescriptor>("form_01", 1),
        Outcomes = new[]
        {
            new CompletionOutcome { Condition = CompletionCondition.Approve },
            new CompletionOutcome { Condition = CompletionCondition.Reject }
        }
    };
    var htValidationEngine = new RegistryValidationEngine<HumanTaskDescriptor>(
        Array.Empty<IRegistryValidator<HumanTaskDescriptor>>());
    var htRegistry = new HumanTaskRegistry(htValidationEngine);
    htRegistry.Build([new TestHumanTaskDescriptorProvider([htDescriptor])]);

    var htStore = new InMemoryHumanTaskInstanceStore();
    var htRuntime = new DefaultHumanTaskRuntime(htRegistry, htStore, NullLocalEventBus.Instance);

    // Build Workflow with a HumanTask step followed by a Capability step
    var pipeline = new CapturingCapabilityPipeline();
    var wfRegistry = CreateRegistry(new WorkflowDescriptor
    {
        Id = "wf_01", Name = "e2e.wf", Version = 1,
        State = DescriptorState.Active,
        Steps = new List<WorkflowStep>
        {
            new()
            {
                Id = "step_01", Name = "Approval",
                Target = new HumanTaskTarget
                {
                    HumanTask = new VersionedDescriptorRef<HumanTaskDescriptor>("ht_01", 1)
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

    // Start workflow → should suspend at HumanTask step
    var instance = await engine.ExecuteAsync("wf_01");
    instance.Status.Should().Be(WorkflowInstanceStatus.Suspended);
    instance.WaitingHumanTaskId.Should().NotBeNullOrEmpty();
    instance.StepIndex.Should().Be(1);

    // Find the created HumanTaskInstance
    var humanTaskInstance = await htStore.GetByIdAsync(instance.WaitingHumanTaskId!);
    humanTaskInstance.Should().NotBeNull();
    humanTaskInstance!.WorkflowInstanceId.Should().Be(instance.InstanceId);

    // Complete the HumanTask
    await htRuntime.CompleteAsync(new HumanTaskCompletionRequest
    {
        HumanTaskInstanceId = humanTaskInstance.Id,
        Outcome = "Approve",
        Result = new { Score = 95 }
    });

    // Manually trigger continuation (event bus is no-op in test)
    await continuation.ContinueAsync(new WorkflowContinuationRequest
    {
        HumanTaskId = humanTaskInstance.Id,
        Outcome = "Approve",
        Result = new { Score = 95 }
    });

    // Workflow should complete
    var final = await wfStore.GetAsync(instance.InstanceId);
    final!.Status.Should().Be(WorkflowInstanceStatus.Completed);
    final.WaitingHumanTaskId.Should().BeNull();
    final.StepResults.Should().HaveCountGreaterThanOrEqualTo(3);
    final.Variables["lastStepOutcome"].Should().Be("Approve");
}
```

- [ ] **Step 2: Add helper types at the end of the test file (before closing namespace `}`)**

Add these helper types:

```csharp
private class TestHumanTaskDescriptorProvider : IDescriptorProvider<HumanTaskDescriptor>
{
    private readonly List<HumanTaskDescriptor> _descriptors;
    public TestHumanTaskDescriptorProvider(List<HumanTaskDescriptor> descriptors)
        => _descriptors = descriptors;
    public IReadOnlyList<HumanTaskDescriptor> GetDescriptors() => _descriptors;
}

private sealed class NullLocalEventBus : ILocalEventBus
{
    public static readonly NullLocalEventBus Instance = new();
    public Task PublishAsync(ILocalEvent @event, CancellationToken ct = default)
        => Task.CompletedTask;
    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : ILocalEvent
        => Task.CompletedTask;
}
```

- [ ] **Step 3: Add required using directives**

Ensure these usings are present at the top of the file:
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

- [ ] **Step 4: Run the E2E test**

Run: `dotnet test --filter "FullyQualifiedName~WorkflowContinuationTests.Workflow_HumanTask_EndToEnd_Complete_Task_Resumes_Workflow"`
Expected: PASS.

- [ ] **Step 5: Run all Workflow tests**

Run: `dotnet test --filter "FullyQualifiedName~CrestCreates.Workflow.Tests"`
Expected: ALL tests pass (existing + 2 new).

- [ ] **Step 6: Commit**

```bash
git add framework/test/CrestCreates.Workflow.Tests/WorkflowContinuationTests.cs
git commit -m "test: add HumanTask end-to-end workflow test"
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
Expected: ALL tests pass (2 existing + 7 runtime + 1 store = 10 total).

- [ ] **Step 2: Run Workflow tests**

Run: `dotnet test framework/test/CrestCreates.Workflow.Tests/CrestCreates.Workflow.Tests.csproj`
Expected: ALL tests pass (existing + 2 new).

- [ ] **Step 3: Run Capability tests (sanity)**

Run: `dotnet test framework/test/CrestCreates.Capability.Tests/`
Expected: ALL tests pass (no regressions).

- [ ] **Step 4: Run Metadata tests (sanity)**

Run: `dotnet test framework/test/CrestCreates.Metadata.Tests/`
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
- [ ] `CrestCreates.HumanTask.Tests` — all new + existing tests pass
- [ ] `CrestCreates.Workflow.Tests` — all new + existing tests pass
- [ ] `CrestCreates.Capability.Tests` not broken
- [ ] `CrestCreates.Metadata.Tests` not broken
