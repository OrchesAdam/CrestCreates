# Phase 4c — Workflow Runtime Closure Design Spec

> **日期:** 2026-06-10 | **状态:** APPROVED | **父 Phase:** Phase 4

---

## 1. 目标

Phase 4c 关闭 Workflow Runtime 执行闭环。Phase 4b 建立了 Foundation（执行到 Suspended），Phase 4c 补全 Continuation 机制：HumanTask 完成后，Workflow 自动恢复执行。

验证闭环链路：

```
Workflow
    ↓
HumanTask (suspend)
    ↓
HumanTaskCompletedEvent (外部触发)
    ↓
WorkflowContinuationService
    ↓
WorkflowExecutionRunner (resume)
    ↓
Remaining Steps
    ↓
Completed
```

### 核心原则

1. **不重新引入 ResumeAsync。** 公共 API 保持 `IWorkflowEngine.ExecuteAsync` 唯一。Continuation 是运行时管理的内部基础设施。
2. **Event-driven continuation。** HumanTask 模块只依赖 Event Runtime 契约。Workflow 模块通过 `HumanTaskCompletedWorkflowSubscriber` 订阅 `HumanTaskCompletedEvent`，触发 Continuation。
   - **依赖方向：** `HumanTask → Event Runtime`，`Workflow → Event Runtime + HumanTask.Abstractions (event contract)`。`HumanTask` 不得依赖 `Workflow`。
3. **共享执行核心。** `IWorkflowExecutionRunner` 是 `WorkflowEngine` 和 `IWorkflowContinuationService` 共享的执行引擎。**Runner 拥有：step loop、执行驱动的状态转换、持久化、以及 suspended/completed/failed 生命周期事件。** Engine/ContinuationService 仅负责入口事件（started/resumed）。
4. **所有 lifecycle event 均在 WorkflowInstance 成功保存后发布。** 不先于持久化。
5. **Metadata retained, execution paths removed。** 同 Phase 4b 原则。

---

## 2. 前置条件

| 前置条件 | 来源 | 状态 |
|----------|------|------|
| `IWorkflowEngine` (ExecuteAsync only, workflowId) | Phase 4b | ✅ |
| `IWorkflowStepExecutorRegistry` + executors | Phase 4b | ✅ |
| `IWorkflowInstanceStore` (SaveAsync, GetAsync) | Phase 4b | ✅ |
| `WorkflowInstance` (含 StepIndex, CurrentStepId, Status) | Phase 4b | ✅ |
| `HumanTaskStepExecutor` (返回 Suspended) | Phase 4b | ✅ |
| `WorkflowCompatibilityValidator` | Phase 4b | ✅ |
| Event Runtime (EventRegistry, event publication) | Phase 3/4 | ✅ |

---

## 3. 架构概述

### 3.1 目标架构

```
IWorkflowEngine (ExecuteAsync only — unchanged)
    │
    └── WorkflowEngine
          │
          ├── IWorkflowExecutionRunner (internal)  ── 共享执行核心
          ├── IWorkflowStateMachine                 ── ValidateTransition(from, to)
          ├── IWorkflowLifecycleEventPublisher      ── 生命周期事件
          ├── IWorkflowStepExecutorRegistry
          └── IWorkflowInstanceStore                ── + GetByWaitingHumanTaskId()

IWorkflowContinuationService
    │
    ├── IWorkflowInstanceStore (GetByWaitingHumanTaskId + load)
    ├── IWorkflowStateMachine (validate Suspended → Running)
    ├── IWorkflowExecutionRunner (resume execution)
    └── IWorkflowLifecycleEventPublisher

HumanTask Module (Phase 5/6)
    │
    └── Publish HumanTaskCompletedEvent { HumanTaskId, Outcome, Result }
              ↓
         HumanTaskCompletedWorkflowSubscriber (implements IEventHandler<HumanTaskCompletedEvent>)
              │
              └── WorkflowContinuationRequest { HumanTaskId, Outcome, Result }
                       ↓
                  IWorkflowContinuationService.ContinueAsync(request)
```

### 3.2 CurrentStepIndex 语义 — 游标（下一步待执行步骤）

```
Start:  CurrentStepIndex = 0
        执行 Step[0] → CurrentStepIndex = 1
Suspend: 在 Step[1] (HumanTask) → CurrentStepIndex = 1 (指向 HumanTask 步骤)
Resume:  HumanTask 已完成 → CurrentStepIndex = 2 (跳过 HumanTask，直接到下一步)
```

`CurrentStepIndex` 始终指向下一步待执行的步骤。不是"最近完成的步骤"。

### 3.3 HumanTask suspend/resume 流程

```
Suspend (in WorkflowExecutionRunner):
    HumanTaskStepExecutor returns StepExecutionResult(
        Status=Suspended,
        WaitingHumanTaskId="ht_01")
    ↓
    Runner sets: instance.WaitingHumanTaskId = stepResult.WaitingHumanTaskId
    Runner validates: WaitingHumanTaskId must be non-null when Status=Suspended
    instance.Status = Suspended

Resume (in WorkflowContinuationService):
    1. Load instance via GetByWaitingHumanTaskId
    2. Ensure instance.Status == Suspended
    3. ValidateTransition(Suspended, Running)
    4. Write HumanTask step result:
       - Resolve current step by CurrentStepIndex
       - instance.StepResults.Add(new WorkflowStepResult {
           StepId = currentStep.Id,
           Status = StepExecutionStatus.Completed,
           Output = request.Result
         })
    5. instance.Variables["lastStepOutcome"] = request.Outcome
       instance.Variables["lastStepResult"] = request.Result
    6. instance.CurrentStepIndex++            // advance past HumanTask step
    7. instance.WaitingHumanTaskId = null
    8. instance.Status = Running
    9. await store.SaveAsync(instance)
    10. Publish workflow.resumed
    11. executionRunner.RunAsync(instance)    // starts from step after HumanTask
```

`HumanTaskStepExecutor` 始终返回 `Suspended`。ContinuationService 负责补写 HumanTask 步骤结果并跳过该步骤。

---

## 4. 核心契约

### 4.1 IWorkflowExecutionRunner

```csharp
// CrestCreates.Workflow (internal)
internal interface IWorkflowExecutionRunner
{
    /// <summary>
    /// Shared execution core.
    /// Does NOT create WorkflowInstance.
    /// Does NOT handle external continuation entry.
    /// Owns: step loop, execution-driven state transitions, persistence,
    ///       and lifecycle events for workflow.suspended / workflow.completed / workflow.failed.
    /// </summary>
    Task<WorkflowInstance> RunAsync(
        WorkflowInstance instance,
        CancellationToken ct);
}
```

Runner 从 `instance.CurrentStepIndex` 开始执行步骤。每次状态转换后主动保存并发布对应 lifecycle event。

### 4.1.1 StepExecutionResult（扩展）

`StepExecutionResult` 新增 `WaitingHumanTaskId` 字段，使 HumanTaskStepExecutor 能将 task ID 传递给 Runner：

```csharp
public sealed record StepExecutionResult(
    StepExecutionStatus Status,
    object? Output = null,
    IReadOnlyDictionary<string, object?>? Variables = null,
    string? WaitingHumanTaskId = null);  // Phase 4c: set by HumanTaskStepExecutor on Suspend
```

Runner 在收到 `Status=Suspended` 时必须验证 `WaitingHumanTaskId` 非空。若为空则抛出 `InvalidOperationException`。

### 4.2 IWorkflowContinuationService

```csharp
// CrestCreates.Workflow.Abstractions
public sealed class WorkflowContinuationRequest
{
    public string HumanTaskId { get; init; } = string.Empty;
    public string Outcome { get; init; } = string.Empty;
    public object? Result { get; init; }
}

public interface IWorkflowContinuationService
{
    Task ContinueAsync(
        WorkflowContinuationRequest request,
        CancellationToken ct = default);
}
```

算法：

```
1. instance = store.GetByWaitingHumanTaskId(request.HumanTaskId)
   → null → throw InvalidOperationException
2. Ensure instance.Status == Suspended
3. stateMachine.ValidateTransition(Suspended, Running)
4. Resolve current HumanTask step by instance.CurrentStepIndex
5. Write HumanTask step result:
   instance.StepResults.Add(new WorkflowStepResult {
       StepId = currentStep.Id,
       Status = StepExecutionStatus.Completed,
       Output = request.Result
   })
6. instance.Variables["lastStepOutcome"] = request.Outcome
   instance.Variables["lastStepResult"] = request.Result
7. instance.CurrentStepIndex++                // advance past HumanTask step
8. instance.WaitingHumanTaskId = null
9. instance.Status = Running
10. await store.SaveAsync(instance)
11. eventPublisher.PublishAsync(workflow.resumed)
12. return executionRunner.RunAsync(instance)
```

注意：ContinuationService 不再根据 Runner 返回状态发布 completed/failed——Runner 自己负责。

### 4.3 IWorkflowStateMachine

```csharp
// CrestCreates.Workflow.Abstractions
public interface IWorkflowStateMachine
{
    /// <summary>
    /// Validates the transition. Throws InvalidWorkflowTransitionException
    /// if the transition is invalid.
    /// </summary>
    void ValidateTransition(
        WorkflowInstanceStatus from,
        WorkflowInstanceStatus to);
}
```

有效过渡:

```
Running → Suspended
Running → Completed
Running → Failed
Suspended → Running
```

所有其他组合将抛出 `InvalidWorkflowTransitionException`。

### 4.4 IWorkflowLifecycleEventPublisher

```csharp
// CrestCreates.Workflow.Abstractions
public interface IWorkflowLifecycleEventPublisher
{
    Task PublishAsync(WorkflowLifecycleEvent lifecycleEvent, CancellationToken ct);
}

public sealed class WorkflowLifecycleEvent
{
    public string EventType { get; init; }        // "workflow.started", etc.
    public string WorkflowInstanceId { get; init; }
    public string WorkflowId { get; init; }
    public WorkflowInstanceStatus Status { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public object? Payload { get; init; }
}
```

**事件触发点（所有事件在 WorkflowInstance 成功保存后发布）：**

| 事件 | 触发时机 |
|------|----------|
| `workflow.started` | Engine: 创建实例后、Status=Running、保存成功、首次执行 Runner 前 |
| `workflow.suspended` | Runner: HumanTask 步骤返回 Suspended、保存成功、返回前 |
| `workflow.resumed` | ContinuationService: Suspended→Running 转换验证通过、保存成功、Runner 执行前 |
| `workflow.completed` | Runner: 所有步骤完成、Status=Completed、保存成功、返回前 |
| `workflow.failed` | Runner: 步骤失败、Status=Failed、保存成功后。**基础设施故障在持久化之前不保证发布 lifecycle event。** |

### 4.5 IWorkflowInstanceStore（扩展）

新增方法：

```csharp
// CrestCreates.Workflow.Abstractions
public interface IWorkflowInstanceStore
{
    // Existing (Phase 4b)
    Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default);
    Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default);

    // New (Phase 4c)
    /// <summary>
    /// Returns the Suspended WorkflowInstance waiting for the given HumanTask.
    /// WaitingHumanTaskId must be unique across active instances.
    /// If multiple matches are found, the store must throw WorkflowCorrelationException.
    /// </summary>
    Task<WorkflowInstance?> GetByWaitingHumanTaskId(
        string humanTaskId,
        CancellationToken ct = default);
}
```

`WaitingHumanTaskId` 在活跃的 workflow instances 中必须唯一。`GetByWaitingHumanTaskId` 只返回 `Status=Suspended` 且 `WaitingHumanTaskId` 匹配的实例。若发现多个匹配，持久化 store 必须抛出 `WorkflowCorrelationException` 而非返回任意实例。Phase 4c InMemory store 亦须遵循此规则。

### 4.6 WorkflowInstance（扩展）

新增字段：

```csharp
// Added to existing WorkflowInstance
public string? WaitingHumanTaskId { get; set; }
```

在 suspend 时设置，resume 时清空。

### 4.7 HumanTaskCompletedEvent

```csharp
// CrestCreates.HumanTask.Abstractions
public sealed class HumanTaskCompletedEvent
{
    public string HumanTaskId { get; init; }
    public string Outcome { get; init; }   // "Approved", "Rejected", etc.
    public object? Result { get; init; }
}
```

不包含任何 Workflow 字段（无 WorkflowInstanceId, WorkflowId, WorkflowStepId）。HumanTask 模块仅依赖 Event Runtime 契约。

### 4.9 HumanTaskCompletedWorkflowSubscriber

```csharp
// CrestCreates.Workflow (internal)
internal sealed class HumanTaskCompletedWorkflowSubscriber
    : IEventHandler<HumanTaskCompletedEvent>
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
            },
            ct);
    }
}
```

此订阅者是闭环的关键——它将 HumanTask 域事件桥接到 Workflow 继续运行时。DI 必须注册：

```csharp
services.TryAddSingleton<IEventHandler<HumanTaskCompletedEvent>,
    HumanTaskCompletedWorkflowSubscriber>();
```

HumanTaskStepExecutor 始终返回 `Suspended`，但现在通过 `StepExecutionResult.WaitingHumanTaskId` 将 task ID 传递给 Runner：

```csharp
public Task<StepExecutionResult> ExecuteAsync(
    WorkflowExecutionContext context, CancellationToken ct)
{
    var target = (HumanTaskTarget)context.Step.Target;
    return Task.FromResult(
        new StepExecutionResult(
            StepExecutionStatus.Suspended,
            WaitingHumanTaskId: target.HumanTask.Id));
}
```

Runner 接收到此结果后设置 `instance.WaitingHumanTaskId`。Continuation 逻辑在 `WorkflowContinuationService` 中。

---

## 5. 实现细节

### 5.1 WorkflowEngine（修订版）

构造函数为 `internal`（因为 `IWorkflowExecutionRunner` 为 `internal`，不能出现在 public 构造函数中）：

```csharp
// CrestCreates.Workflow
public sealed class WorkflowEngine : IWorkflowEngine
{
    internal WorkflowEngine(
        IWorkflowRegistry registry,
        IWorkflowInstanceStore store,
        IWorkflowExecutionRunner executionRunner,
        IWorkflowLifecycleEventPublisher eventPublisher)
    {
        // ...
    }
}
```

DI 使用 factory 注册：

```csharp
services.TryAddSingleton<IWorkflowEngine>(sp =>
    new WorkflowEngine(
        sp.GetRequiredService<IWorkflowRegistry>(),
        sp.GetRequiredService<IWorkflowInstanceStore>(),
        sp.GetRequiredService<IWorkflowExecutionRunner>(),
        sp.GetRequiredService<IWorkflowLifecycleEventPublisher>()));
```

`ExecuteAsync` 流程（Engine 仅负责入口事件 + Runner 委托。Runner 拥有持久化和 terminal events）：

```
1. descriptor = registry.GetById(workflowId)
2. Create WorkflowInstance (Status=Running, CurrentStepIndex=0)
3. await store.SaveAsync(instance)
4. Publish workflow.started (after save)
5. return executionRunner.RunAsync(instance)
   // Runner handles: execution → state transitions → save → suspended/completed/failed events
```

### 5.2 WorkflowExecutionRunner

```csharp
// CrestCreates.Workflow (internal)
internal sealed class WorkflowExecutionRunner : IWorkflowExecutionRunner
```

Runner 职责（重构自当前 `WorkflowEngine.ExecuteStepsAsync`）：

- 从 `instance.CurrentStepIndex` 开始执行步骤
- 执行成功后：记录 step result，递增 `CurrentStepIndex`
- HumanTask Suspended 时：验证 `stepResult.WaitingHumanTaskId` 非空，设置 `instance.WaitingHumanTaskId`，设置 `Status = Suspended`，保存，发布 `workflow.suspended`，返回
- 所有步骤完成：设置 `Status = Completed`，保存，发布 `workflow.completed`，返回
- 失败时：设置 `Status = Failed`，保存，发布 `workflow.failed`，返回

### 5.3 DefaultWorkflowStateMachine

```csharp
// CrestCreates.Workflow
public sealed class DefaultWorkflowStateMachine : IWorkflowStateMachine
{
    public void ValidateTransition(WorkflowInstanceStatus from, WorkflowInstanceStatus to)
    {
        var valid = (from, to) switch
        {
            (Running, Suspended) => true,
            (Running, Completed) => true,
            (Running, Failed) => true,
            (Suspended, Running) => true,
            _ => false
        };

        if (!valid)
            throw new InvalidWorkflowTransitionException(from, to);
    }
}
```

### 5.4 WorkflowContinuationService

```csharp
// CrestCreates.Workflow
public sealed class WorkflowContinuationService : IWorkflowContinuationService
{
    private readonly IWorkflowInstanceStore _store;
    private readonly IWorkflowStateMachine _stateMachine;
    private readonly IWorkflowExecutionRunner _executionRunner;
    private readonly IWorkflowLifecycleEventPublisher _eventPublisher;

    // Full algorithm as described in Section 4.2
}
```

### 5.5 InMemoryWorkflowInstanceStore（扩展）

```csharp
public Task<WorkflowInstance?> GetByWaitingHumanTaskId(
    string humanTaskId, CancellationToken ct)
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

**并发说明：**
- InMemory store 是线程安全的（ConcurrentDictionary），但不支持分布式锁。
- Phase 4c 仅验证顺序幂等性（double-resume 被拒绝），不保证分布式 exactly-once continuation。
- 持久化 store 应在后续阶段使用乐观并发 / row version。

### 5.6 DI 注册

```csharp
public static IServiceCollection AddWorkflowEngine(this IServiceCollection services)
{
    // Phase 4b registrations (existing)
    services.TryAddSingleton<IWorkflowEngine, WorkflowEngine>();
    services.TryAddSingleton<IWorkflowInstanceStore, InMemoryWorkflowInstanceStore>();
    services.TryAddSingleton<CapabilityStepExecutor>();
    services.TryAddSingleton<HumanTaskStepExecutor>();
    services.TryAddSingleton<IWorkflowStepExecutorRegistry, DefaultStepExecutorRegistry>();
    services.TryAddSingleton<WorkflowCompatibilityValidator>();

    // Phase 4c additions
    services.TryAddSingleton<IWorkflowExecutionRunner, WorkflowExecutionRunner>();
    services.TryAddSingleton<IWorkflowStateMachine, DefaultWorkflowStateMachine>();
    services.TryAddSingleton<IWorkflowLifecycleEventPublisher, WorkflowLifecycleEventPublisher>();
    services.TryAddSingleton<IWorkflowContinuationService, WorkflowContinuationService>();

    return services;
}
```

---

## 6. 类型归属

| 类型 | 项目 | 可见性 |
|------|------|--------|
| `IWorkflowExecutionRunner` | `CrestCreates.Workflow` | internal |
| `WorkflowExecutionRunner` | `CrestCreates.Workflow` | internal |
| `IWorkflowStateMachine` | `CrestCreates.Workflow.Abstractions` | public |
| `DefaultWorkflowStateMachine` | `CrestCreates.Workflow` | public |
| `InvalidWorkflowTransitionException` | `CrestCreates.Workflow.Abstractions` | public |
| `IWorkflowLifecycleEventPublisher` | `CrestCreates.Workflow.Abstractions` | public |
| `WorkflowLifecycleEvent` | `CrestCreates.Workflow.Abstractions` | public |
| `WorkflowLifecycleEventPublisher` | `CrestCreates.Workflow` | internal |
| `IWorkflowContinuationService` | `CrestCreates.Workflow.Abstractions` | public |
| `WorkflowContinuationService` | `CrestCreates.Workflow` | public |
| `WorkflowContinuationRequest` | `CrestCreates.Workflow.Abstractions` | public |
| `HumanTaskCompletedWorkflowSubscriber` | `CrestCreates.Workflow` | internal |
| `HumanTaskCompletedEvent` | `CrestCreates.HumanTask.Abstractions` | public |
| `WorkflowCorrelationException` | `CrestCreates.Workflow.Abstractions` | public |
| `HumanTaskCompletedEvent` | `CrestCreates.HumanTask.Abstractions` | public |
| `WorkflowInstance` (extended) | `CrestCreates.Workflow.Abstractions` | public |
| `IWorkflowInstanceStore` (extended) | `CrestCreates.Workflow.Abstractions` | public |
| `WorkflowEngine` (revised) | `CrestCreates.Workflow` | public |

---

## 7. 验收标准

使用 xUnit + FluentAssertions。

### Case 1 — 全闭环

```
Workflow: Step1(Capability) → Step2(HumanTask "ht_01") → Step3(Capability)

ExecuteAsync("wf_01") → Step1 Completed → Step2 Suspended
    Status = Suspended, WaitingHumanTaskId = "ht_01", StepIndex = 1

ContinueAsync(new WorkflowContinuationRequest { HumanTaskId = "ht_01",
    Outcome = "Approved", Result = new { Score = 95 } })
    → HumanTask step result written to StepResults (Status=Completed)
    → StepIndex = 2, WaitingHumanTaskId = null
    → Step3 Completed
    Status = Completed, StepResults.Count = 3
```

### Case 2 — 双次 resume

```
ContinueAsync(request { HumanTaskId = "ht_01", Outcome = "ok" }) → success (WaitingHumanTaskId 清空)
ContinueAsync(request { HumanTaskId = "ht_01", Outcome = "ok" }) → GetByWaitingHumanTaskId 返回 null → InvalidOperationException
```

### Case 3 — Invalid state transition

```
Validator.ValidateTransition(Completed, Running) → 抛出 InvalidWorkflowTransitionException
Validator.ValidateTransition(Suspended, Suspended) → 抛出 InvalidWorkflowTransitionException
```

### Case 4 — lifecycle 事件

```
ExecuteAsync → workflow.started (1 次)
    ...执行...
    Suspend → workflow.suspended (1 次)
ContinueAsync → workflow.resumed (1 次)
    ...完成...
    Complete → workflow.completed (1 次)
```

### Case 5 — workflow.failed on failure

```
Capability 步骤失败 (OnError=Fail) → workflow.failed
Executor 抛出异常 → workflow.failed
```

### Case 6 — WaitingHumanTaskId 清理

```
ExecuteAsync suspends → WaitingHumanTaskId = "ht_01"
ContinueAsync → WaitingHumanTaskId = null
    store.GetByWaitingHumanTaskId("ht_01") → null
```

### Case 7 — lastStepResult / lastStepOutcome 变量传播

```
ContinueAsync(request { Outcome = "Approved", Result = new { Score = 95 } })
    → instance.Variables["lastStepOutcome"] == "Approved"
    → instance.Variables["lastStepResult"] == { Score: 95 }
    → HumanTask StepResult written with Status=Completed, Output = request.Result
Step3 Capability 执行 → context.Instance.Variables["lastStepResult"] 可访问
```

### Case 8 — HumanTaskCompletedEvent 无 Workflow 字段

```
HumanTaskCompletedEvent: 仅 HumanTaskId、Outcome、Result
无 WorkflowInstanceId、WorkflowId、WorkflowStepId
```

---

## 8. 迁移影响评估

| 变更 | 影响 |
|------|------|
| `WorkflowEngine` 构造函数新增 3 个参数 | 仅影响 DI 注册——由 `AddWorkflowEngine()` 自动处理。外部调用者不受影响 |
| `IWorkflowInstanceStore` 新增方法 | `InMemoryWorkflowInstanceStore` 实现新方法。若有其他 store 实现，需实现新方法 |
| `WorkflowInstance` 新增属性 | 新增可选属性，序列化兼容（null by default） |
| `IWorkflowEngine` API | 不变。公共 API 无破坏性变更 |
| `HumanTaskCompletedEvent` | 新类型，HumanTask 模块中的新增内容 |
| 已有测试 | 现有测试继续通过（`WorkflowEngine` 构造函数变更在测试帮助方法中处理） |

---

## 9. 不做内容（Phase 4c 范围外）

以下内容明确禁止进入 Phase 4c：

| 类别 | 禁止内容 |
|------|----------|
| ResumeAsync | 不重新引入公共 API |
| Retry | RetryPolicy、Backoff |
| Compensation | Saga、Rollback、Compensate |
| SubWorkflow | 子工作流执行 |
| Branching/Parallel | 分支、fork、join、并行执行 |
| Timers/Cron | 调度器功能 |
| Message correlation | 基于消息的工作流恢复 |
| BPMN | BPMN 语义 |
