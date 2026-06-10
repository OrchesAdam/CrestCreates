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
2. **Event-driven continuation。** HumanTask 模块只依赖 Event Runtime 契约。Workflow 模块通过事件订阅触发 Continuation。
3. **共享执行核心。** `IWorkflowExecutionRunner` 是 `WorkflowEngine` 和 `IWorkflowContinuationService` 共享的执行引擎，避免执行逻辑分叉。
4. **Metadata retained, execution paths removed。** 同 Phase 4b 原则。

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
         Event Subscriber (Workflow module)
              ↓
         IWorkflowContinuationService.ContinueAsync()
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
Suspend:
    instance.CurrentStepIndex = currentIndex;   // 游标停在 HumanTask 步骤
    instance.WaitingHumanTaskId = "ht_01";
    instance.Status = Suspended;

Resume:
    instance.CurrentStepIndex++;                // 跳过 HumanTask 步骤
    instance.WaitingHumanTaskId = null;
    instance.Status = Running;

    executionRunner.RunAsync(instance);         // 从 Step[2] 开始
```

`HumanTaskStepExecutor` 始终返回 `Suspended`。ContinuationService 负责跳过 HumanTask 步骤——executor 不需要"检查任务是否完成"。

---

## 4. 核心契约

### 4.1 IWorkflowExecutionRunner

```csharp
// CrestCreates.Workflow (internal)
internal interface IWorkflowExecutionRunner
{
    /// <summary>
    /// Shared execution core. Consumed by both WorkflowEngine and WorkflowContinuationService.
    /// Takes an instance with Status=Running and CurrentStepIndex set.
    /// Returns instance with terminal status (Completed, Suspended, or Failed).
    /// </summary>
    Task<WorkflowInstance> RunAsync(
        WorkflowInstance instance,
        CancellationToken ct);
}
```

实现 Phase 4b 的 step loop（for-loop, executor dispatch, state transitions, persistence）。纯执行——不发布事件，不创建实例。

### 4.2 IWorkflowContinuationService

```csharp
// CrestCreates.Workflow.Abstractions
public interface IWorkflowContinuationService
{
    Task ContinueAsync(
        string humanTaskId,
        object? result,
        CancellationToken ct = default);
}
```

算法：

```
1. instance = store.GetByWaitingHumanTaskId(humanTaskId)
   → null → throw InvalidOperationException
2. stateMachine.ValidateTransition(Suspended, Running)
   → invalid → throw InvalidWorkflowTransitionException
3. instance.Variables["stepResult"] = result
4. instance.CurrentStepIndex++
5. instance.WaitingHumanTaskId = null
6. instance.Status = Running
7. await store.SaveAsync(instance)
8. eventPublisher.PublishAsync(workflow.resumed)
9. executionRunner.RunAsync(instance)
10. 根据返回状态发布 workflow.completed 或 workflow.failed
```

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

**事件触发点：**

| 事件 | 触发时机 |
|------|----------|
| `workflow.started` | 创建实例后、Status=Running、首次执行步骤前 |
| `workflow.suspended` | HumanTask 步骤返回 Suspended 后、保存前 |
| `workflow.resumed` | `ValidateTransition` 通过后、Status=Running、执行恢复前 |
| `workflow.completed` | 所有步骤完成 |
| `workflow.failed` | 任何 Failure 退出路径（业务失败、基础设施异常、store 保存失败、continuation 失败） |

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
    Task<WorkflowInstance?> GetByWaitingHumanTaskId(
        string humanTaskId,
        CancellationToken ct = default);
}
```

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

### 4.8 HumanTaskStepExecutor — 不变

与 Phase 4b 完全相同。始终返回 `StepExecutionResult(Suspended)`。Continuation 逻辑在 `WorkflowContinuationService` 中。

---

## 5. 实现细节

### 5.1 WorkflowEngine（修订版）

构造函数新增：

```csharp
public WorkflowEngine(
    IWorkflowRegistry registry,
    IWorkflowStepExecutorRegistry executorRegistry,
    IWorkflowInstanceStore store,
    IWorkflowExecutionRunner executionRunner,
    IWorkflowStateMachine stateMachine,
    IWorkflowLifecycleEventPublisher eventPublisher)
```

`ExecuteAsync` 流程：

```
1. descriptor = registry.GetById(workflowId)
2. Create WorkflowInstance (Running, CurrentStepIndex=0)
3. eventPublisher.PublishAsync(workflow.started)
4. executionRunner.RunAsync(instance)
5. 根据 returned 状态发布对应事件
6. await store.SaveAsync(instance)
7. return instance
```

### 5.2 WorkflowExecutionRunner

```csharp
// CrestCreates.Workflow (internal)
internal sealed class WorkflowExecutionRunner : IWorkflowExecutionRunner
```

从当前 `WorkflowEngine.ExecuteStepsAsync` 提取。实现相同的步骤循环（for-loop, executor dispatch, state transitions），但不进行事件发布或实例创建。作为纯执行核心。

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
    var match = _instances.Values
        .FirstOrDefault(i => i.WaitingHumanTaskId == humanTaskId);
    return Task.FromResult(match);
}
```

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
    Status = Suspended, WaitingHumanTaskId = "ht_01", CurrentStepIndex = 1

ContinueAsync("ht_01", result) → CurrentStepIndex = 2, WaitingHumanTaskId = null
    → Step3 Completed
    Status = Completed, StepResults.Count = 3
```

### Case 2 — 双次 resume

```
ContinueAsync("ht_01", ...) → success (WaitingHumanTaskId 已清空)
ContinueAsync("ht_01", ...) → GetByWaitingHumanTaskId 返回 null → InvalidOperationException
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

### Case 7 — stepResult 变量传播

```
ContinueAsync("ht_01", new { Approved = true })
    → instance.Variables["stepResult"] == { Approved: true }
Step3 Capability 执行 → context.Instance.Variables["stepResult"] 可访问
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
