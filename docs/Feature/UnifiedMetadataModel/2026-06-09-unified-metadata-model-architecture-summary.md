# 统一元数据模型 — 架构总结文档

> **日期:** 2026-06-09 | **状态:** 完成 | **13 个 Phase, 14 个 Commits, ~196 个测试**

---

## 1. 设计目标

为 CrestCreates 企业 .NET 框架建立统一的元数据模型，避免 4-5 套并行的 descriptor 系统。**四个核心抽象（四大支柱）：**

| 支柱 | 回答的问题 | Descriptor |
|------|-----------|------------|
| **Schema** | "数据长什么样？" | `SchemaDescriptor` |
| **Capability** | "系统能做什么？" | `CapabilityDescriptor` |
| **Event** | "发生了什么？" | `EventDescriptor` |
| **Workflow** | "如何编排？" | `WorkflowDescriptor` |

加两个 Instance Infrastructure descriptors：**Form**（Schema + UI metadata）和 **HumanTask**（人工交互的业务操作）。

---

## 2. 项目结构

```
framework/src/
├── CrestCreates.Metadata.Abstractions/     # IDescriptor, IVersionedDescriptor, DescriptorRef, VersionedDescriptorRef
├── CrestCreates.Metadata/                   # GlobalRegistry, Catalog, DependencyGraph, HashComputer
├── CrestCreates.Schema.Abstractions/        # SchemaDescriptor, SchemaFieldDescriptor, ISchemaRegistry, ISchemaValidator
├── CrestCreates.Schema/                     # SchemaRegistry, SchemaValidator
├── CrestCreates.Capability.Abstractions/    # CapabilityDescriptor, ICapabilityPipeline, ExecutionContext, ICapabilityHandlerInvoker
├── CrestCreates.Capability/                 # CapabilityRegistry, CapabilityPipeline, middleware chain (8个)
├── CrestCreates.Event.Abstractions/         # EventDescriptor, EventCategory, EventSemantic, EventImportance
├── CrestCreates.Event/                      # EventRegistry
├── CrestCreates.Form.Abstractions/          # FormDescriptor, FormFieldDescriptor
├── CrestCreates.Form/                       # FormRegistry
├── CrestCreates.HumanTask.Abstractions/     # HumanTaskDescriptor, CompletionOutcome, AssigneeStrategy
├── CrestCreates.HumanTask/                  # HumanTaskRegistry
├── CrestCreates.Workflow.Abstractions/      # WorkflowDescriptor, WorkflowStep, InteractionTarget, IWorkflowEngine
├── CrestCreates.Workflow/                   # WorkflowRegistry, WorkflowEngine (step execution, resume, checkpoint)
├── CrestCreates.Draft.Abstractions/         # DraftRecord, IDraftStore, DraftStatus
├── CrestCreates.Draft/                      # InMemoryDraftStore, TenantIsolatedDraftStore
├── CrestCreates.Exposure.Abstractions/      # AgentToolDescriptor, MCPToolDescriptor, ToolCallMode
└── framework/tools/CrestCreates.CodeGenerator/  # 5 source generators (Schema/Capability/Event/Form/HumanTask/Workflow + Handler + RefValidation)

framework/test/
├── CrestCreates.Schema.Tests/               (19)
├── CrestCreates.Metadata.Tests/             (33)
├── CrestCreates.Capability.Tests/           (59)
├── CrestCreates.Draft.Tests/               (13)
├── CrestCreates.Event.Tests/               (11)
├── CrestCreates.Exposure.Tests/            (12)
├── CrestCreates.Form.Tests/                (8)
├── CrestCreates.HumanTask.Tests/           (8)
└── CrestCreates.Workflow.Tests/            (28)
```

---

## 3. 核心架构原则

### 3.1 Descriptor 与 Instance 的铁律

```
Descriptor = What can exist (Stateless)    vs    Instance = What is happening (Stateful)
─────────────────────────────────────         ─────────────────────────────────────
SchemaDescriptor                               DraftRecord
CapabilityDescriptor                           CapabilityExecution
EventDescriptor                                WorkflowInstance
WorkflowDescriptor                             HumanTaskInstance
FormDescriptor
HumanTaskDescriptor
```

### 3.2 依赖规则（白名单 vs 黑名单）

**允许:**
```
Capability   → Schema     (Input/Output)
Form         → Schema     (Form = Schema + UI)
HumanTask    → Form       (UI delegate)
HumanTask    → Schema     (Task I/O)
HumanTask    → Capability (Post-completion actions)
Workflow     → Schema     (Variable schema)
WorkflowStep → CapabilityTarget | HumanTaskTarget | SubWorkflowTarget
Event        → Schema     (Payload schema)
Exposure     → Capability (Projection views)
Entity       → Schema     (Produces schema)
```

**禁止:**
```
Capability → Capability    (原子性 — 编排须用 Workflow)
Capability → Workflow      (Capability 不拥有编排)
Capability → Form/HumanTask (Capability 无 UI)
WorkflowStep → Form/ApplicationService (必须经过 Capability/HumanTask)
Form → Capability/HumanTask (Form 是纯 UI 元数据)
Entity → Form/Workflow/Capability (Entity 在 Capability 链之外)
Draft → Capability/Workflow/HumanTask (Draft 只引用 Schema)
```

### 3.3 IDescriptor 基础接口

```csharp
public interface IDescriptor
{
    DescriptorKind Kind { get; }        // Schema, Capability, Event, Workflow, Form, HumanTask
    string Id { get; }                  // 主键 (GUID/ULID, 重命名不改)
    string Name { get; }                // 人类可读别名 (全球唯一, 可改名)
    DescriptorState State { get; }      // Draft → Active → Deprecated → Removed
    string? SupersededById { get; }
    string ContractHash { get; }        // 结构性兼容性指纹
    string DefinitionHash { get; }      // 完整内容指纹 (审计)
}

public interface IVersionedDescriptor : IDescriptor
{
    int Version { get; }  // 单调递增, 所有6个类型都有版本
}
```

### 3.4 VersionedDescriptorRef<T> — 统一的类型化引用

```csharp
public readonly record struct VersionedDescriptorRef<T>(string Id, int Version)
    where T : IVersionedDescriptor;
```

所有 descriptor 之间的结构化引用都使用 `VersionedDescriptorRef<T>`。

---

## 4. Capability 执行流水线

### 4.1 流水线架构（8 层中间件）

```
RateLimitMiddleware  →  滑动窗口限流 (100 req/min default)
    ↓
TenantMiddleware     →  注入 TenantId
    ↓
AuthorizationMiddleware → 权限检查 (ICapabilityAuthorizationService)
    ↓
ValidationMiddleware →  Schema 验证 (ISchemaValidator → InputSchema)
    ↓
IdempotencyMiddleware → 幂等性 (IIdempotenceStore → 重复检测/缓存)
    ↓
Handler Invoker      →  ICapabilityHandlerInvoker (source-gen, 零反射)
    ↓
EventPublishingMiddleware → 发布 lifecycle events (ILocalEventBus)
    ↓
MetricsMiddleware    →  执行指标 (IPipelineMetrics → count/duration)
    ↓
CapabilityExecutionResult (Status, Output, Duration, Events)
```

### 4.2 关键接口

```csharp
// 流水线入口
public interface ICapabilityPipeline
{
    Task<CapabilityExecutionResult> ExecuteAsync(string capabilityName, object? input, ...);
}

// 零反射 handler 调用 (source-gen 生成)
public interface ICapabilityHandlerInvoker
{
    Task<object?> InvokeAsync(object? input, CancellationToken ct);
}

// 所有中间件接口
public interface ICapabilityPipelineMiddleware
{
    Task<CapabilityExecutionResult> InvokeAsync(CapabilityExecutionContext ctx, CapabilityPipelineDelegate next);
}
```

---

## 5. Workflow 运行时引擎

### 5.1 执行模型

```
IWorkflowEngine.ExecuteAsync("employee.onboarding", vars)
  → Resolve WorkflowDescriptor (registry)
  → Create WorkflowInstance (pinned to descriptor version)
  → Iterate steps:
      ├── CapabilityTarget  → ICapabilityPipeline (执行 capability)
      ├── HumanTaskTarget   → passthrough (deferred to HumanTask phase)
      └── SubWorkflowTarget → 递归 ExecuteAsync
  → Error handling: Retry(×3) | Skip | Fail | Compensate
  → Step transitions: sequential or explicit transition targets
  → Checkpoint → IDraftStore (DraftRecord)
  → Instance.Completed
```

### 5.2 断点恢复

```csharp
// 崩溃后从 DraftRecord checkpoint 恢复
await engine.ResumeAsync("instance_01")
  → Load DraftRecord (wf_ckpt_{id})
  → Deserialize CheckpointState (InstanceId, StepIndex, Variables)
  → Reconstruct WorkflowInstance
  → Continue from saved StepIndex
```

---

## 6. 多租户架构

```
ITenantContext.CurrentTenantId = "tenant_A"
    ↓
TenantMiddleware          → 注入 TenantId 到 execution context
TenantScopedRegistry<T>   → 装饰器: 过滤 by tenant selector
TenantIsolatedDraftStore  → 装饰器: Save 覆盖 TenantId, Get/Query 过滤
CapabilityProfileResolver → Tenant:VIP > Environment:Prod > Global > default
```

---

## 7. Exposure Layer（投影视图）

三个轻量级 Capability 投影 — 不定义自己的 Schema:

```csharp
AgentToolDescriptor  → CapabilityDescriptor (LLM 工具: Description, ToolCallMode, BudgetLimit, Tags)
MCPToolDescriptor    → CapabilityDescriptor (MCP 协议: Description, ToolCallMode)
CapabilityEndpointDescriptor → CapabilityDescriptor (HTTP: HttpMethod, RoutePattern, Authorization)
```

---

## 8. Source Generator 基础设施

| Generator | 功能 |
|-----------|------|
| `SchemaCapabilitySourceGenerator` | [Entity] → SchemaDescriptor, [CrestService] → CapabilityDescriptor |
| `HandlerInvokerSourceGenerator` | 发现 ICapabilityHandler<TIn,TOut>, 生成 XXX_Invoker + 注册代码 |
| `RefValidationSourceGenerator` | 编译时验证所有 VersionedDescriptorRef 可解析, CC1001 error |

---

## 9. 系统事件

4 个框架定义的 capability lifecycle events（注册在 EventRegistry）:

| Event | Category | Semantic | Importance |
|-------|----------|----------|------------|
| `capability.executing` | Capability | StateTransition | Operational |
| `capability.succeeded` | Capability | Fact | Business |
| `capability.failed` | Capability | Fact | Business |
| `capability.compensated` | Capability | StateTransition | Business |

---

## 10. 测试覆盖

| 测试项目 | 测试数 | 覆盖范围 |
|----------|--------|---------|
| Schema.Tests | 19 | Descriptor 创建, Registry CRUD + 版本, Validator(10) |
| Metadata.Tests | 33 | DescriptorRef, HashComputer(8), DependencyGraph(4), GlobalRegistry(3), Catalog(2), Snapshot(1), Manifest(1), TenantScoped(4), RefValidator(5) |
| Capability.Tests | 59 | Descriptor(4), Registry(4), Profile(2), Pipeline(6), Builder(3), ExecutionContext(5), ExecutionResult(4), SystemEvents(6), Idempotency(5), EventPublisher(4), Metrics(7), Tenant(2), RateLimit(5), DelegateHandler(3) |
| Draft.Tests | 13 | DraftRecord(4), InMemoryStore(5), TenantIsolated(4) |
| Event.Tests | 11 | Descriptor(5), Registry(6) |
| Exposure.Tests | 12 | AgentTool(5), MCPTool(3), CapabilityEndpoint(4) |
| Form.Tests | 8 | Descriptor(5), Registry(3) |
| HumanTask.Tests | 8 | Descriptor(6), Registry(2) |
| Workflow.Tests | 28 | Descriptor(6), Registry(3), InteractionTarget(4), Engine(11), Resume(4) |
| **Total** | **~196** | |

---

## 11. 设计决策清单（43 项全部实现）

| # | 决策 | 状态 |
|---|------|------|
| 1 | SchemaDescriptor 是数据形状的唯一真相源 | ✅ |
| 2 | 每个 Schema 有稳定 Id + Version | ✅ |
| 3 | CapabilityDescriptor 回答 What/Input/Output | ✅ |
| 4 | CapabilityKind = Query/Command, Draft 是 Runtime Data | ✅ |
| 5 | Capability 原子性 — 组合须用 Workflow | ✅ |
| 6 | Id 是主键, Name 是人类别名 | ✅ |
| 7 | Schema 版本演进 + ChangeKind + 消费者 pin 版本 | ✅ |
| 8 | Descriptor 是纯元数据, Handler 是执行逻辑 | ✅ |
| 9 | 所有 descriptors 实现 IDescriptor | ✅ |
| 10 | 所有 6 个类型都是 IVersionedDescriptor | ✅ |
| 11 | ContractHash + DefinitionHash 分离 | ✅ |
| 12 | Hash = canonical JSON → SHA256 | ✅ |
| 13 | VersionedDescriptorRef<T> with VersionSelectionMode | ✅ |
| 14 | .Version 统一属性（无 SchemaVersion/CapabilityVersion 重复） | ✅ |
| 15 | WorkflowStep Id 全局唯一 | ✅ |
| 16 | 四大支柱: Schema, Capability, Event, Workflow | ✅ |
| 17 | EventDescriptor 携带 PayloadSchema, Category, Semantic, Importance | ✅ |
| 18 | 系统事件是普通 EventDescriptors — 无特殊 registry | ✅ |
| 19 | IDescriptorDependencyGraph + DescriptorDependencyKind | ✅ |
| 20 | IGlobalDescriptorRegistry 跨类型统一视图 | ✅ |
| 21 | DescriptorPackage 模块层级分组 | ✅ |
| 22 | DescriptorManifest 轻量模块级索引 | ✅ |
| 23 | DescriptorSnapshot 运行实例的时间点快照 | ✅ |
| 24 | Draft 是 Runtime Data 且只引用 Schema | ✅ |
| 25 | IDraftStore 是共享平台服务 | ✅ |
| 26 | Draft 提交验证 Schema 版本兼容性 | ✅ |
| 27 | WorkflowDraftPolicy 管理断点行为 | ✅ |
| 28 | CapabilityDescriptor.Name 支持 Aliases | ✅ |
| 29 | SemanticTags 用于 Agent/Workflow 搜索 | ✅ |
| 30 | EventImportance 驱动基础设施策略 | ✅ |
| 31 | DescriptorState 生命周期: Draft→Active→Deprecated→Removed | ✅ |
| 32 | Descriptors 不可变, running instances pin version | ✅ |
| 33 | Capability 执行产出生周期 events | ✅ |
| 34 | Domain Events ≠ Capability Events | ✅ |
| 35 | FormDescriptor = Schema + UI metadata | ✅ |
| 36 | HumanTaskDescriptor 是人工交互的业务操作 | ✅ |
| 37 | WorkflowDescriptor 有 version, instances pin at instantiation | ✅ |
| 38 | Workflow 变量有定义的作用域 | ✅ |
| 39 | WorkflowStep 绑定 InteractionTarget | ✅ |
| 40 | DynamicApi/AgentTool/MCPTool 是 Capability 的投影 | ✅ |
| 41 | 所有 Capability 调用进入统一流水线 | ✅ |
| 42 | Entity 是 Schema 源，不在 Capability 链中 | ✅ |
| 43 | Entity→Form/Workflow/Capability 是禁止的依赖 | ✅ |
