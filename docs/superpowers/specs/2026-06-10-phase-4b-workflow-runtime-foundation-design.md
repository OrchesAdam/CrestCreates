# Phase 4b — Workflow Runtime Foundation Design Spec

> **日期:** 2026-06-10 | **状态:** APPROVED | **父 Phase:** Phase 4

---

## 1. 目标

Phase 4b 在 Workflow 模块现有基础设施之上，将 `WorkflowEngine` 从单体执行器重构为基于 delegate（Step Executor）的干净运行时架构，并建立最小执行闭环。

验证链路：

```
Workflow
    ↓
Workflow Engine (IWorkflowEngine)
    ↓
Step Executor Registry (IWorkflowStepExecutorRegistry)
    ↓
Capability Step Executor / HumanTask Step Executor
    ↓
Capability Pipeline → Handler
```

### 核心原则

1. **Workflow Engine 是 Capability Runtime 的编排层，不是独立业务执行系统。** Capability 才是真正执行单元。
2. **Workflow Engine 只负责：** Step Sequencing, State Tracking, Suspension。不负责 Business Logic。
3. **Metadata retained, execution paths removed.** Descriptor 模型可包含未来概念，Runtime 只实现当前支持的行为。
4. **唯一主链。** `IWorkflowEngine` 是唯一公开契约，`WorkflowEngine` 是唯一实现。

---

## 2. 前置条件

| 前置条件 | 来源 | 状态 |
|----------|------|------|
| `WorkflowDescriptor`, `WorkflowStep`, `InteractionTarget` 层次 | Phase 1-2 | ✅ |
| `WorkflowRegistry : RegistryBase<WorkflowDescriptor>` | Phase 4a | ✅ |
| `IWorkflowEngine` 接口 (`ExecuteAsync` + `ResumeAsync`) | Phase 2 | ✅ |
| `WorkflowEngine` 实现（单体式，含 SubWorkflow/Retry/Resume/Transition） | Phase 2 | ✅ |
| `ICapabilityPipeline.ExecuteAsync()` | Phase 4 | ✅ |
| `WorkflowStepResult` (class, `bool IsSuccess`) | Phase 2 | ✅ |
| `WorkflowInstanceStatus` (含 `Compensated`) | Phase 2 | ✅ |
| `IDraftStore` 依赖（checkpoint 序列化） | Phase 2 | ✅ |

---

## 3. 架构概述

### 3.1 目标架构

```
IWorkflowEngine (ExecuteAsync only)
    │
    └── WorkflowEngine (internally refactored)
          │
          ├── IWorkflowStepExecutorRegistry  ── Resolves executor by InteractionTarget subtype
          │     ├── CapabilityStepExecutor    ── Delegates to ICapabilityPipeline
          │     └── HumanTaskStepExecutor     ── Produces Suspended execution result
          │
          ├── IWorkflowInstanceStore
          │     └── SaveAsync (upsert) / GetAsync
          │
          └── WorkflowCompatibilityValidator  ── Bootstrap validation (fail-fast at startup)
```

### 3.2 架构不变量

> **WorkflowEngine never performs target-type branching.**
> All target dispatch must occur through `IWorkflowStepExecutorRegistry`.
> Adding a new `InteractionTarget` must not require modification of `WorkflowEngine`.

### 3.3 责任边界

| 组件 | 职责 |
|---|---|
| `IWorkflowStepExecutor` | 执行单步 → 返回 `StepExecutionResult`。不访问持久化，不修改 WorkflowInstance 状态。 |
| `WorkflowEngine` | 状态转换、持久化、生命周期管理。 |
| `IWorkflowInstanceStore` | Save / Load `WorkflowInstance`。 |
| `WorkflowCompatibilityValidator` | Bootstrap 验证（非运行时验证）。 |

### 3.4 新运行时状态模型（架构级变更）

```
Previous:  bool IsSuccess

Phase 4b:  StepExecutionStatus
              ├── Completed
              ├── Suspended
              └── Failed
```

`StepExecutionResult(Suspended)`（→ engine 转换为 `WorkflowInstanceStatus.Suspended`）是后续 HumanTask Runtime、Event Runtime、Timer Runtime 的统一暂停原语——无需修改 engine。

---

## 4. 删除内容

### 4.1 从 Engine 移除的执行路径

| 功能 | 操作 | 后续阶段 |
|------|------|----------|
| `ResumeAsync` | 从接口 + 实现删除 | Phase 5/6 |
| SubWorkflow 执行 | `ExecuteSubWorkflowTarget()` 删除 | Phase 5+ |
| Retry 执行 | `HandleStepError` Retry 分支删除 | Phase 5+ |
| Compensation 执行 | `HandleStepError` Compensate 分支删除 | Phase 5+ |
| Branch/Transition 执行 | `step.Transitions[0]` 逻辑删除 | Phase 5+ |
| `IDraftStore` checkpoint | `CheckpointAsync`, `CheckpointState` 类删除 | — |
| `ICapabilityPipeline?` nullable | 从构造函数删除（CapabilityStepExecutor 持有） | — |

### 4.2 保留的 Descriptor 元数据（Runtime 不执行）

- `SubWorkflowTarget` — Phase 4b bootstrap validator 拒绝
- `StepErrorBehavior.Retry` — Phase 4b bootstrap validator 拒绝
- `StepErrorBehavior.Compensate` — Phase 4b bootstrap validator 拒绝
- `WorkflowStep.Transitions` — Phase 4b bootstrap validator 拒绝
- `StepErrorBehavior.Skip` — 保留（Phase 4b 支持）
- `WorkflowInstanceStatus.Compensated` — 保留枚举值（Runtime 不产生）

---

## 5. 核心契约

### 5.1 IWorkflowEngine

```csharp
// CrestCreates.Workflow.Abstractions/IWorkflowEngine.cs
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

**变更：** `workflowName` → `workflowId`。`ResumeAsync` 移除。

### 5.2 IWorkflowStepExecutor

```csharp
// CrestCreates.Workflow.Abstractions/IWorkflowStepExecutor.cs
public interface IWorkflowStepExecutor
{
    /// <summary>
    /// Executes a single workflow step. The executor:
    /// - MUST return StepExecutionResult(Failed) for known business failures.
    /// - MUST throw only for infrastructure/programming errors.
    /// - MUST NOT access persistence or modify WorkflowInstance state.
    /// </summary>
    Task<StepExecutionResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken ct);
}
```

### 5.3 StepExecutionResult & StepExecutionStatus

```csharp
// CrestCreates.Workflow.Abstractions/StepExecutionResult.cs
public sealed record StepExecutionResult(
    StepExecutionStatus Status,
    object? Output = null,
    IReadOnlyDictionary<string, object?>? Variables = null);
```

```csharp
// CrestCreates.Workflow.Abstractions/StepExecutionStatus.cs
public enum StepExecutionStatus
{
    Completed,
    Suspended,
    Failed
}
```

- `Failed` — 已知业务失败（验证拒绝、业务规则拒绝、安全拒绝）。Executor 返回，Engine 记录到 WorkflowStepResult。
- `Suspended` — HumanTask 等待人工。Engine 转换为 `WorkflowInstanceStatus.Suspended`。
- Exception — 基础设施/编程错误。Engine catch 后构造 `WorkflowStepResult(Status=Failed, ErrorMessage=ex.Message)`。
- `Variables` — Executor 返回变量变更；Engine 负责应用到 `WorkflowInstance.Variables`。Executor 永远不直接修改 `context.Instance`。

### 5.4 IWorkflowStepExecutorRegistry

```csharp
// CrestCreates.Workflow.Abstractions/IWorkflowStepExecutorRegistry.cs
public interface IWorkflowStepExecutorRegistry
{
    /// <summary>
    /// Resolves the executor for the given target.
    /// Throws UnsupportedWorkflowTargetException if no executor is registered.
    /// WorkflowCompatibilityValidator must guarantee this never fails at runtime.
    /// </summary>
    IWorkflowStepExecutor Resolve(InteractionTarget target);
}
```

**注册表在启动期间预计算。** 映射 `Type → IWorkflowStepExecutor`，不可变。

### 5.5 WorkflowExecutionContext

```csharp
// CrestCreates.Workflow.Abstractions/WorkflowExecutionContext.cs
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

**不含 `IServiceProvider`。不含 `CancellationToken`。不含持久化引用。** 纯粹的状态传递对象。CancellationToken 通过 `ExecuteAsync(..., ct)` 单独传递。

### 5.6 IWorkflowInstanceStore

```csharp
// CrestCreates.Workflow.Abstractions/IWorkflowInstanceStore.cs
public interface IWorkflowInstanceStore
{
    Task SaveAsync(WorkflowInstance instance, CancellationToken ct = default);
    Task<WorkflowInstance?> GetAsync(string instanceId, CancellationToken ct = default);
}
```

Upsert 语义。无 INSERT/UPDATE 数据库语义。两个方法。

### 5.7 WorkflowStepResult（升级）

```csharp
// CrestCreates.Workflow.Abstractions/WorkflowStepResult.cs
public sealed class WorkflowStepResult
{
    public string StepId { get; init; } = string.Empty;
    public string StepName { get; init; } = string.Empty;
    public StepExecutionStatus Status { get; init; }        // was: bool IsSuccess
    public object? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTimeOffset ExecutedAt { get; init; }         // new
    public TimeSpan Duration { get; init; }
}
```

**变更：** `bool IsSuccess` → `StepExecutionStatus Status`。新增 `DateTimeOffset ExecutedAt`。

**语义说明：** `WorkflowStepResult.Status=Suspended` 表示该 step 请求暂停 workflow 并从 executor 视角视为已成功执行，而非 step 本身未完成。`StepExecutionResult` 表达 executor 对 workflow 的产出（继续/暂停/失败），`WorkflowStepResult` 表达已记录的历史（该 step 的最终执行记录）。

---

## 6. 实现

### 6.1 WorkflowEngine 算法

```
ExecuteAsync(workflowId, inputVariables, ct)
    │
    ├─ 1. Resolve descriptor via WorkflowRegistry
    │
    ├─ 2. Create WorkflowInstance (Status=Running, StepIndex=0)
    │
    ├─ 3. For each step (linear, StepIndex++):
    │   │
    │   ├─ 3a. Resolve executor via IWorkflowStepExecutorRegistry
    │   │
    │   ├─ 3b. Execute: executor.ExecuteAsync(context, ct)
    │   │     │
    │   │     ├─ Completed → record WorkflowStepResult, apply Variables to instance, continue
    │   │     │
    │   │     ├─ Suspended →
    │   │     │    record WorkflowStepResult(Status=Suspended)
    │   │     │    instance.Status = Suspended
    │   │     │    await store.SaveAsync(instance)
    │   │     │    return instance
    │   │     │
    │   │     ├─ Failed →
    │   │     │    record WorkflowStepResult
    │   │     │    instance.Status = Failed
    │   │     │    await store.SaveAsync(instance)
    │   │     │    return instance
    │   │     │
    │   │     └─ Exception (infrastructure) →
    │   │          catch, construct WorkflowStepResult(Status=Failed, ErrorMessage=ex.Message)
    │   │          instance.Status = Failed
    │   │          await store.SaveAsync(instance)
    │   │          return instance
    │   │
    │   ├─ 3c. On Skip: record WorkflowStepResult, continue (Skip does not alter Status)
    │   │
    │   └─ 3d. StepIndex++
    │
    ├─ 4. instance.Status = Completed
    │   instance.CompletedAt = utcNow
    │
    └─ 5. await store.SaveAsync(instance)
           return instance
```

**仅支持线性工作流。** 不允许 Branch、Fork、Join、Parallel、Loop、SubWorkflow。

**Engine 负责所有 WorkflowInstance 状态变更。** Executor 视为纯执行代理：可返回输出和变量更新，但不得修改 WorkflowInstance 状态、step index、时间戳或生命周期状态。

### 6.2 CapabilityStepExecutor

```csharp
// CrestCreates.Workflow/CapabilityStepExecutor.cs
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

**Engine 负责将 `result.Variables` 应用到 `WorkflowInstance`** — executor 永远不修改 `context.Instance`。

> **⚠️ 临时方案 (Phase 4b)：** 当前 Capability 输出到 Workflow Variables 的映射是直接字典合并。后续阶段应引入 Schema-based Variable Mapping，通过 `WorkflowStep.InputMapping` / `OutputMapping` 声明式地控制变量流入流出，而非将 Capability 的全部输出写入 WorkflowInstance。

### 6.3 HumanTaskStepExecutor

```csharp
// CrestCreates.Workflow/HumanTaskStepExecutor.cs
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

产生 `Suspended`。Engine 解释此结果并转换到 `WorkflowInstanceStatus.Suspended`。

### 6.4 DefaultStepExecutorRegistry

```csharp
// CrestCreates.Workflow/DefaultStepExecutorRegistry.cs
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

### 6.5 InMemoryWorkflowInstanceStore

```csharp
// CrestCreates.Workflow/InMemoryWorkflowInstanceStore.cs
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

### 6.6 WorkflowCompatibilityValidator

```csharp
// CrestCreates.Workflow/WorkflowCompatibilityValidator.cs
public sealed class WorkflowCompatibilityValidator
{
    /// <summary>
    /// Bootstrap validation only. Validates that a WorkflowDescriptor
    /// contains only constructs supported by the current runtime phase.
    /// Must be called during application startup, not during execution.
    /// </summary>
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

**Bootstrap 验证器，非运行时验证器。** 不可达的运行时分支不存在——不受支持的构造在启动时即被拒绝。

#### 6.6.1 启动执行时机

**Validator registration alone does not activate validation.**
`WorkflowCompatibilityValidator` 必须由 `MetadataBootstrapper` 在应用程序启动期间显式调用。

具体执行时机：

```
MetadataBootstrapper.BuildAll()
    │
    ├─ Registry.Build(providers)
    │
    └─ For each WorkflowDescriptor in registry.GetAll():
           CompatibilityValidator.Validate(descriptor)
```

在 `WorkflowRegistry.Build()` 完成之后、应用程序开始接受请求之前。验证失败将导致应用程序启动失败（硬失败而非软降级）。

### 6.7 DI 注册

```csharp
// CrestCreates.Workflow/WorkflowServiceCollectionExtensions.cs (revised)
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

---

## 7. 类型归属

| 类型 | 项目 | 命名空间 |
|------|------|----------|
| `IWorkflowEngine` (revised) | `CrestCreates.Workflow.Abstractions` | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowStepExecutor` | `CrestCreates.Workflow.Abstractions` | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowStepExecutorRegistry` | `CrestCreates.Workflow.Abstractions` | `CrestCreates.Workflow.Abstractions` |
| `StepExecutionResult`, `StepExecutionStatus` | `CrestCreates.Workflow.Abstractions` | `CrestCreates.Workflow.Abstractions` |
| `WorkflowExecutionContext` | `CrestCreates.Workflow.Abstractions` | `CrestCreates.Workflow.Abstractions` |
| `IWorkflowInstanceStore` | `CrestCreates.Workflow.Abstractions` | `CrestCreates.Workflow.Abstractions` |
| `WorkflowStepResult` (upgraded) | `CrestCreates.Workflow.Abstractions` | `CrestCreates.Workflow.Abstractions` |
| `WorkflowInstance` (preserved) | `CrestCreates.Workflow.Abstractions` | `CrestCreates.Workflow.Abstractions` |
| `WorkflowEngine` (refactored) | `CrestCreates.Workflow` | `CrestCreates.Workflow` |
| `CapabilityStepExecutor` | `CrestCreates.Workflow` | `CrestCreates.Workflow` |
| `HumanTaskStepExecutor` | `CrestCreates.Workflow` | `CrestCreates.Workflow` |
| `DefaultStepExecutorRegistry` | `CrestCreates.Workflow` | `CrestCreates.Workflow` |
| `InMemoryWorkflowInstanceStore` | `CrestCreates.Workflow` | `CrestCreates.Workflow` |
| `WorkflowCompatibilityValidator` | `CrestCreates.Workflow` | `CrestCreates.Workflow` |

---

## 8. 验收标准

使用 xUnit + FluentAssertions。遵循现有 `CrestCreates.Workflow.Tests` 测试模式。

### Case 1 — 线性 Capability 流程 → Completed

```
Workflow:  Step1(Capability A) → Step2(Capability B)
Expected:  Status=Completed, 2 step results, both Status=Completed
```

### Case 2 — Capability + HumanTask → Suspended

```
Workflow:  Step1(Capability A) → Step2(HumanTask)
Expected:  Status=Suspended after Step1 completes, Step2 returns Suspended,
           Step2 WorkflowStepResult recorded (Status=Suspended), StepIndex=1
```

### Case 3 — Capability 失败 → Failed

```
Workflow:  Step1(Capability A) → Step2(Capability B fails)
Expected:  Status=Failed, 2 step results, Step2 Status=Failed
```

### Case 4 — Bootstrap 验证拒绝 SubWorkflow

```
Descriptor contains SubWorkflowTarget
Expected:  WorkflowCompatibilityValidator.Validate() throws WorkflowValidationException
```

### Case 5 — Bootstrap 验证拒绝 Retry

```
Descriptor step has StepErrorBehavior.Retry
Expected:  WorkflowCompatibilityValidator.Validate() throws WorkflowValidationException
```

### Case 6 — Bootstrap 验证拒绝 Transitions

```
Descriptor step has Transitions.Count > 0
Expected:  WorkflowCompatibilityValidator.Validate() throws WorkflowValidationException
```

### Case 7 — Skip 不改变执行状态

```
Workflow:  Step1(Capability fails, Skip) → Step2(HumanTask)
Expected:  Status=Suspended, 2 step results, Step1 Status=Failed (Skip does not alter
           execution status), Step2 Status=Suspended
           Workflow execution continues past the skipped step
```

---

## 9. 不做内容（Phase 4b 范围外）

以下内容明确禁止进入 Phase 4b：

| 类别 | 禁止内容 |
|------|----------|
| Event Resume | `ResumeAsync()` — 已从接口移除 |
| Scheduler | Timer、Cron、Delay、Timeout |
| Compensation | Saga、Rollback、Compensate（元数据保留，Runtime 拒绝） |
| Retry | RetryPolicy、Backoff（元数据保留，Runtime 拒绝） |
| Distributed Execution | RabbitMQ、Kafka、Outbox |
| HumanTask Runtime | 任务创建、分配、审批、结果 |
| Workflow Persistence Recovery | 重启恢复、崩溃恢复、重放 |

---

## 10. 未来扩展说明

### Context 有意最小化

`WorkflowExecutionContext` 当前仅包含 `Workflow`、`Instance`、`Step`。后续阶段可能新增 `CorrelationId`、`TraceId`、`ExecutionMode`，或 `Items` 字典——不影响现有 executor。

### Phase 5+ 工作流继续

Phase 5 可能引入工作流继续 API。当前 `IWorkflowInstanceStore` 将作为重载暂停实例的持久化基础。

### 新增 Target 类型

新增 `EventWaitTarget`、`TimerTarget` 或 `AgentTarget` 仅需：
1. 在 Workflow Abstractions 中定义 `InteractionTarget` 子类型
2. 实现 `IWorkflowStepExecutor`
3. 在 `DefaultStepExecutorRegistry` 中注册
4. 更新 `WorkflowCompatibilityValidator`（从拒绝列表中移除）

**无需修改 `WorkflowEngine`。**
