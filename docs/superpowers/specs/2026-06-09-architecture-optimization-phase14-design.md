# Phase 14: Architecture Optimization — Design Spec

> **Date:** 2026-06-09 | **Status:** Approved
>
> 基于 Phase 1-13 完成后的架构评审，4 项增量优化——不涉及架构重构，纯增量改进。

---

## 1. IInteractionDescriptor — HumanTask-Form 解耦

### 1.1 问题

当前 `HumanTaskDescriptor.Form` 直接引用 `VersionedDescriptorRef<FormDescriptor>`。这意味着：
- 每个 HumanTask 必须绑定一个 Form
- Agent 时代的对话式交互（没有 Form）无法表示
- Spec §14 已预见："HumanTask references `IInteractionDescriptor`, not `FormDescriptor` directly"

### 1.2 设计

```csharp
// CrestCreates.Metadata.Abstractions/IInteractionDescriptor.cs
namespace CrestCreates.Metadata.Abstractions;

/// <summary>
/// Marker interface for types that represent a human interaction surface.
/// FormDescriptor is the initial implementation. Future: ConversationDescriptor.
/// </summary>
public interface IInteractionDescriptor : IVersionedDescriptor
{
}
```

**变更清单：**

| 文件 | 变更 |
|------|------|
| `CrestCreates.Metadata.Abstractions/IInteractionDescriptor.cs` | 新增标记接口 |
| `CrestCreates.Form.Abstractions/FormDescriptor.cs` | 增加 `: IInteractionDescriptor` |
| `CrestCreates.HumanTask.Abstractions/HumanTaskDescriptor.cs` | `Form` 改名为 `Interaction`，类型变为 `VersionedDescriptorRef<IInteractionDescriptor>` |
| `CrestCreates.HumanTask.Abstractions/CompletionOutcome.cs` | 无需改动（Outcome 引用的是 Capability，不是 Interaction） |
| 所有测试文件中的 `Form = new VersionedDescriptorRef<FormDescriptor>` | 改为 `Interaction = new VersionedDescriptorRef<IInteractionDescriptor>` |

### 1.3 向后兼容

这是破坏性变更（`HumanTaskDescriptor.Form` 不存在了），但因为 HumanTask 还未被任何生产代码使用（仅在 Phase 3 创建），影响范围仅限于我们的 8 个测试。

---

## 2. Workflow 事件驱动闭环

### 2.1 问题

当前 `WorkflowEngine.ExecuteStepAsync` 对 `HumanTaskTarget` 只是 passthrough——直接返回成功，工作流继续。现实中 HumanTask 是异步的：创建任务后需要等待用户操作，用户在提交时应触发 capability 执行，然后该 capability 的成功事件应恢复工作流。

Spec §1 明确："Workflow is fundamentally an event-driven state machine — Workflow steps advance when Capability events arrive."

### 2.2 设计

**Step A: HumanTaskTarget 挂起行为**

```csharp
// WorkflowEngine.ExecuteStepAsync 中:
HumanTaskTarget => SuspendAndSave(instance, step, descriptor, ct)

private async Task<WorkflowStepResult> SuspendAndSave(...)
{
    instance.Status = WorkflowInstanceStatus.Suspended;
    await CheckpointAsync(instance, descriptor, ct);
    return new WorkflowStepResult
    {
        StepId = step.Id,
        IsSuccess = true,  // 挂起本身不是失败
        Duration = DateTimeOffset.UtcNow - startedAt
    };
}
```

**Step B: WorkflowEventConsumer**

```csharp
// CrestCreates.Workflow/IWorkflowEventConsumer.cs
public interface IWorkflowEventConsumer
{
    Task OnCapabilityEventAsync(string eventName, object? payload, CancellationToken ct);
}

// CrestCreates.Workflow/WorkflowEventConsumer.cs
public sealed class WorkflowEventConsumer : IWorkflowEventConsumer
{
    private readonly IWorkflowEngine _engine;
    private readonly IDraftStore _draftStore;
    
    // 订阅 ILocalEventBus, 收到 "capability.succeeded" 或 "capability.failed"
    // 匹配挂起的 WorkflowInstance → ResumeAsync
}
```

**触发链：**

```
用户完成 HumanTask 
  → HumanTask 的 CompletionOutcome.Capability 被 pipeline 执行
    → pipeline 发布 capability.succeeded 
      → EventBus 分发
        → WorkflowEventConsumer.OnCapabilityEventAsync
          → 匹配挂起的 WorkflowInstance (by correlationId)
            → WorkflowEngine.ResumeAsync(instanceId)
              → 从 checkpoint 恢复 → 继续执行
```

### 2.3 DI 注册

```csharp
services.AddWorkflowEngine(options => options.EnableEventDrivenResume = true);
```

当 `EnableEventDrivenResume = true` 时，`WorkflowEventConsumer` 会自动注册并订阅 EventBus。

---

## 3. ExpectedContractHash — 契约漂移检测

### 3.1 问题

Spec §4.8 设计："`VersionedDescriptorRef<T>` may optionally carry an `ExpectedContractHash` for runtime consistency checks." 这是描述符引用层面的防漂移机制——确保引用时看到的 descriptor 在执行时没有发生结构性变化。

### 3.2 设计

```csharp
// VersionedDescriptorRef.cs — 增加可选字段
public readonly record struct VersionedDescriptorRef<TDescriptor>(
    string Id,
    int Version,
    string? ExpectedContractHash = null
) where TDescriptor : IVersionedDescriptor;
```

**Pipeline 检查逻辑：**

```csharp
// CapabilityPipeline.ExecuteAsync 中，resolve descriptor 之后:
if (descriptorRef.ExpectedContractHash != null 
    && descriptorRef.ExpectedContractHash != descriptor.ContractHash)
{
    // 发布 drift warning event（不阻塞执行）
    await _eventPublisher?.PublishAsync("capability.contract_drift", new
    {
        descriptorName = descriptor.Name,
        expectedHash = descriptorRef.ExpectedContractHash,
        actualHash = descriptor.ContractHash,
        correlationId = context.CorrelationId
    }, ct);
}
```

**原则：**
- Warning 级别，不阻塞执行
- 通过 EventPublisher 发布 `capability.contract_drift` 事件
- 不影响执行结果（不影响 Idempotency 缓存 key）
- Source generator 在生成注册代码时自动计算 ExpectedContractHash

---

## 4. DomainEventCollection — 统一发布

### 4.1 问题

Spec §7.3 划分了 Domain Events 和 Capability Events。Handler 执行中产生的 Domain Events（如 `CustomerCreatedDomainEvent`）需要与 Capability Events 一起发布。当前 EventPublishingMiddleware 只发布 Capability Events。

### 4.2 设计

**收集：** Handler 将 domain events 放入 `CapabilityExecutionContext.Items`：

```csharp
// Handler 中:
context.Items["__domainEvents"] = new List<object>
{
    new CustomerCreatedDomainEvent { CustomerId = customer.Id, Name = input.Name }
};
```

**发布：** EventPublishingMiddleware 在发布 capability 事件后遍历发布：

```csharp
// EventPublishingMiddleware.InvokeAsync 中:
if (context.Items.TryGetValue("__domainEvents", out var val) 
    && val is IReadOnlyList<object> domainEvents)
{
    foreach (var domainEvent in domainEvents)
    {
        await _publisher.PublishAsync(domainEvent.GetType().Name, domainEvent, ct);
    }
}
```

**已有集成路径：** 框架中 `Entity.AddDomainEvent()` 是现有的 domain event 收集机制。我们在 Pipeline 的 `CapabilityExecutionContext` 上桥接这个机制，让 `Entity.AddDomainEvent()` 追加的事件在 pipeline 结束时被统一发布。

---

## 5. 影响范围总览

| 优化项 | 新文件 | 修改文件 | 测试 |
|--------|--------|---------|------|
| 1. IInteractionDescriptor | 1 (接口) | 2 (FormDescriptor, HumanTaskDescriptor) + 8 tests | 更新现有测试 |
| 2. Workflow 事件驱动 | 3 (IWorkflowEventConsumer, WorkflowEventConsumer, DI) | 1 (WorkflowEngine) | ~5 new |
| 3. ExpectedContractHash | 0 | 1 (VersionedDescriptorRef) + 1 (Pipeline) | ~3 new |
| 4. DomainEvents | 0 | 1 (EventPublishingMiddleware) | ~3 new |
| **Total** | **4 new files** | **6 modified + tests** | **~11 new/updated** |

---

## 6. 自审

- **占位符扫描：** 无 TBD/TODO
- **一致性：** IInteractionDescriptor 在所有引用处一致使用，WorkflowEventConsumer 接口签名与 EventBus 兼容
- **范围：** 4 项优化全部增量——不破坏现有 Pipeline/Registry/Engine 架构，不改变任何 Descriptor 的核心行为
