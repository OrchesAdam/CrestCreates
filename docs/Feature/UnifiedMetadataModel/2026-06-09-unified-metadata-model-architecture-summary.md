# 统一元数据模型 — 架构总结文档

> **日期:** 2026-06-10 | **状态:** 完成 | **Metadata Kernel v1.0 + Phase 3 + Phase 4 + Phase 4a 完成**

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

### Phase 4: Capability Runtime Consolidation

Phase 4 统一 Capability 运行时，打破循环依赖，收口 ICapabilityRegistry 到 Metadata，新增 Dispatcher、Resolver、Audit 链路：

| 组件 | 说明 |
|------|------|
| `ICapabilityResolver` | 统一解析入口，Id-first 分辨率，移至 Metadata（避免循环依赖） |
| `ICapabilityDispatcher` | Capability 执行统一门面，注入 ITenantContext/ICurrentUser |
| `ICapabilityAuditStore` | 审计存储契约（InMemory/Null 实现） |
| `AuditMiddleware` | 最外层审计中间件（隔离失败，不阻断执行） |
| `DefaultCapabilityVersionResolver` | Id/Name 区分：Id 为稳定标识符，Name 为显示名 |
| `IBootstrapValidator` | 启动阶段验证器契约 |
| `IDescriptorLookup` | 跨 Registry 描述符查找契约 |
| `ICapabilityHandlerRegistry` | Handler 注册表契约 |
| `IVersionedDescriptorRegistry.GetByVersion(id, version)` | 基于 Id 的版本精确查找 |

### Phase 3: Metadata Runtime Foundation

Phase 3 将 EventRegistry 的模式提炼为通用注册表基类 `RegistryBase<T>`，建立 Crest Metadata Kernel：

| 组件 | 说明 |
|------|------|
| `RegistryBase<T>` | 通用注册表基类，FrozenDictionary 不可变快照 |
| `RegistrySnapshot<T>` | ById/ByName/ByVersion 三索引快照 |
| `RegistryValidationEngine<T>` | 可插拔验证管道，批量错误报告 |
| `BootstrapCoordinator` | 拓扑排序启动协调器，循环依赖检测 |
| `DescriptorResolver` | 统一描述符解析器 |
| `IBootstrapTask` | 通用启动任务接口 |
| `IDynamicRegistry<T>` | 动态注册表 Future Hook |
| `IHasContractIdentity` | 兼容性身份接口（ContractHash/DefinitionHash） |
| `IRelationshipAwareDescriptor` | 自描述关系接口 |
| `CapabilityRegistry` | 第一个基于 RegistryBase 的非 Event 注册表 |

---

## 2. 项目结构

```
framework/src/
├── CrestCreates.Metadata.Abstractions/     # IDescriptor, IVersionedDescriptor, IHasContractIdentity,
│                                           # IRelationshipAwareDescriptor, IDescriptorRef, DescriptorRef,
│                                           # DescriptorKey, ValidationIssue/Report, IRegistryValidator,
│                                           # IRegistryValidationEngine, IRegistryIndex, IDescriptorProvider,
│                                           # IDescriptorResolver, DescriptorQuery, IBootstrapTask,
│                                           # BootstrapDependencyException, IDynamicRegistry, RegistryState
│                                           # CapabilityKind, CapabilityRiskLevel (moved from Capability.Abstractions)
│                                           # IBootstrapValidator, IDescriptorLookup, ICapabilityHandlerRegistry
│                                           # IVersionedDescriptorRegistry (updated: +GetByVersion)
├── CrestCreates.Metadata/                  # RegistryBase<T>, RegistrySnapshot<T>, RegistryValidationEngine<T>,
│                                           # BootstrapCoordinator, DescriptorResolver, CapabilityDescriptor,
│                                           # CapabilityRegistry (unified, implements ICapabilityRegistry + RegistryBase),
│                                           # EventVersionChainValidator, DuplicateNameVersionValidator,
│                                           # UniquePayloadTypeValidator, TenantScopedRegistry (implements GetByVersion),
│                                           # GlobalRegistry, Catalog, DependencyGraph, HashComputer
│                                           # ICapabilityRegistry (moved from Capability.Abstractions — breaks circular dep)
│                                           # ICapabilityResolver, ICapabilityDispatcher (moved from Capability.Abstractions)
│                                           # CapabilityProfile (moved from Capability.Abstractions)
├── CrestCreates.Schema.Abstractions/        # SchemaDescriptor, SchemaFieldDescriptor, ISchemaRegistry, ISchemaValidator
├── CrestCreates.Schema/                     # SchemaRegistry (implements GetByVersion), SchemaValidator
├── CrestCreates.Capability.Abstractions/    # ICapabilityPipeline, CapabilityExecutionContext, CapabilityExecutionResult,
│                                           # CapabilityRef, InvocationSource, CapabilityNotFoundException,
│                                           # CapabilityExecutionRecord, ICapabilityAuditStore,
│                                           # ICapabilityHandlerResolver, ICapabilityHandlerInvoker, CapabilityPipelineDelegate
├── CrestCreates.Capability/                 # CapabilityPipeline (updated: Id-first lookup + CapabilityId in context),
│                                           # CapabilityDispatcher (unified facade, injects ITenantContext/ICurrentUser),
│                                           # DefaultCapabilityResolver, DefaultCapabilityVersionResolver,
│                                           # AuditMiddleware, NullCapabilityAuditStore, InMemoryCapabilityAuditStore,
│                                           # CapabilityHandlerValidator, CapabilitySchemaValidator (bootstrap validators),
│                                           # middleware chain (9层: Audit → RateLimit → Tenant → Auth → Validation → Idempotency → Metrics → Handler → EventPub)
│                                           # CapabilityServiceCollectionExtensions (AddCapabilityRuntime DI)
├── CrestCreates.Event.Abstractions/         # GeneratedEventDescriptor, DynamicEventDescriptor, EventCategory,
│                                           # EventSemantic, EventImportance, CrestEventAttribute,
│                                           # IEventDescriptorProvider, IEventRegistry, IEventMetadataProvider,
│                                           # IEventResolver, IDynamicEventRegistry, IEventValidator
├── CrestCreates.Event/                      # EventRegistry (inherits RegistryBase), EventResolver,
│                                           # DynamicEventRegistry, RegistryEventValidator,
│                                           # PassThroughEventValidator, EventRegistryBootstrapper
├── CrestCreates.Form.Abstractions/          # FormDescriptor, FormFieldDescriptor, IFormRegistry
├── CrestCreates.Form/                       # FormRegistry (implements GetByVersion)
├── CrestCreates.HumanTask.Abstractions/     # HumanTaskDescriptor, CompletionOutcome, AssigneeStrategy, IHumanTaskRegistry
├── CrestCreates.HumanTask/                  # HumanTaskRegistry (implements GetByVersion)
├── CrestCreates.Workflow.Abstractions/      # WorkflowDescriptor, WorkflowStep, InteractionTarget, IWorkflowEngine, IWorkflowRegistry
├── CrestCreates.Workflow/                   # WorkflowRegistry (implements GetByVersion), WorkflowEngine
├── CrestCreates.Draft.Abstractions/         # DraftRecord, IDraftStore, DraftStatus
├── CrestCreates.Draft/                      # InMemoryDraftStore, TenantIsolatedDraftStore
├── CrestCreates.Exposure.Abstractions/      # AgentToolDescriptor, MCPToolDescriptor, ToolCallMode
└── framework/tools/CrestCreates.CodeGenerator/  # 5 source generators (Schema/Capability/Event/Form/HumanTask/Workflow + Handler + RefValidation)

framework/test/
├── CrestCreates.Schema.Tests/               (19)
├── CrestCreates.Metadata.Tests/             (79)  ← Phase 3/4: RegistryBase, Validators, Bootstrap, Resolver, CapabilityRegistry
├── CrestCreates.Capability.Tests/           (104) ← Phase 4/4a: Resolver, Dispatcher, Audit, E2E, Registry migration
├── CrestCreates.Draft.Tests/               (13)
├── CrestCreates.Event.Tests/               (32)  ← Phase 2a: EventRegistry, Validator, DynamicRegistry
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
/// <summary>
/// 所有描述符的底层接口。
/// Namespace + Id = Global Identity
/// </summary>
public interface IDescriptor
{
    string Namespace { get; }           // Registry domain: "event", "capability", "workflow"
    string Id { get; }                  // Domain-local identity: "user.created"
    string FullId => $"{Namespace}.{Id}";  // Global identity: "event.user.created"
    string Name { get; }                // 人类可读名称
}

/// <summary>
/// 版本化描述符。EventDescriptor、CapabilityDescriptor 等有版本概念。
/// </summary>
public interface IVersionedDescriptor : IDescriptor
{
    int Version { get; }
}

/// <summary>
/// 具有兼容性身份的描述符。
/// 用于版本兼容性判断、拓扑分析、AI推理。
/// </summary>
public interface IHasContractIdentity
{
    string ContractHash { get; }        // 结构性兼容性指纹
    string DefinitionHash { get; }      // 完整内容指纹 (审计)
}

/// <summary>
/// 关系感知描述符。描述符自身提供关系信息，供 Topology Engine 消费。
/// </summary>
public interface IRelationshipAwareDescriptor
{
    IEnumerable<DescriptorRelationship> GetRelationships();
}
```

### 3.3.1 RegistryBase<T> — 通用注册表基类

```csharp
/// <summary>
/// 通用注册表基类。所有 Registry 的母体。
/// Build-once, FrozenDictionary 不可变快照, 可插拔验证。
/// </summary>
public abstract class RegistryBase<TDescriptor> where TDescriptor : class, IDescriptor
{
    protected abstract string RegistryNamespace { get; }

    public void Build(IEnumerable<IDescriptorProvider<TDescriptor>> providers) { }
    public TDescriptor? GetById(string id) { }
    public IReadOnlyList<TDescriptor> GetByName(string name) { }
    public IReadOnlyList<TDescriptor> GetAll() { }
    public TDescriptor? GetByVersion(string id, int version) { }

    protected abstract RegistrySnapshot<TDescriptor> BuildSnapshot(List<TDescriptor> descriptors);
}
```

### 3.3.2 RegistrySnapshot<T> — 不可变快照

```csharp
public sealed record RegistrySnapshot<TDescriptor>(
    FrozenDictionary<string, TDescriptor> ById,              // Canonical (latest)
    FrozenDictionary<string, ImmutableArray<TDescriptor>> ByName,  // All versions
    FrozenDictionary<DescriptorKey, TDescriptor> ByVersion,  // Exact version
    ImmutableArray<TDescriptor> All,
    ImmutableDictionary<Type, IRegistryIndex> CustomIndexes);  // Extensible
```

### 3.3.3 验证管道

```csharp
// 可插拔验证器
public interface IRegistryValidator<TDescriptor>
{
    int Order { get; }
    ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors);
}

// 验证引擎 — 协调所有验证器，收集所有错误
public interface IRegistryValidationEngine<TDescriptor>
{
    ValidationReport Validate(IReadOnlyList<TDescriptor> descriptors);
}
```

### 3.3.4 Bootstrap 协调器

```csharp
public interface IBootstrapTask
{
    string TaskId { get; }                    // "event-registry", "capability-registry"
    Type ServiceType { get; }
    IReadOnlyList<string> Dependencies { get; }
    bool IsRequired { get; }                  // true = Fatal, false = Warning
    Task ExecuteAsync(IServiceProvider sp, CancellationToken ct);
}

// BootstrapCoordinator: 拓扑排序 + 循环依赖检测
```

### 3.4 VersionedDescriptorRef<T> — 统一的类型化引用

```csharp
public readonly record struct VersionedDescriptorRef<T>(string Id, int Version)
    where T : IVersionedDescriptor;
```

所有 descriptor 之间的结构化引用都使用 `VersionedDescriptorRef<T>`。

---

## 4. Capability 执行流水线

### 4.1 流水线架构（9 层中间件 + Dispatcher 门面）

```
ICapabilityDispatcher (统一执行入口 — 注入 InvocationSource + TenantId/UserId)
    ↓
ICapabilityPipeline.ExecuteAsync(id) → GetById → GetActiveVersion → GetByName (Id-first 查找)
    ↓
AuditMiddleware     →  最外层审计 (记录所有结果, 隔离失败不阻断)
    ↓
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
MetricsMiddleware    →  执行指标 (IPipelineMetrics → count/duration)
    ↓
Handler Invoker      →  ICapabilityHandlerInvoker (source-gen, 零反射)
    ↓
EventPublishingMiddleware → 发布 lifecycle events (ILocalEventBus)
    ↓
CapabilityExecutionResult (Status, Output, Duration, Events, AuditRecordId)
```

### 4.2 关键接口

```csharp
// 流水线入口
public interface ICapabilityPipeline
{
    Task<CapabilityExecutionResult> ExecuteAsync(string capabilityName, object? input, ...);
}

// 统一执行门面 (Phase 4 新增)
public interface ICapabilityDispatcher
{
    Task<CapabilityExecutionResult> DispatchAsync(CapabilityDescriptor descriptor, InvocationSource source, ...);
    Task<CapabilityExecutionResult> DispatchAsync(string capabilityId, InvocationSource source, ...);
}

// 统一解析器 (Phase 4 新增 — 移至 Metadata 避免循环依赖)
public interface ICapabilityResolver
{
    CapabilityDescriptor Resolve(CapabilityRef capabilityRef);
    CapabilityDescriptor Resolve(string capabilityIdOrVersion);  // DIM
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

// 执行上下文 (Phase 4 更新: CapabilityId + InvocationSource, descriptor 属性为 init-only)
public sealed class CapabilityExecutionContext
{
    public string CapabilityId { get; init; }       // 稳定标识符 — pipeline 设置
    public string CapabilityName { get; init; }     // 显示名 — pipeline 设置
    public int CapabilityVersion { get; init; }     // 版本 — pipeline 设置
    public InvocationSource InvocationSource { get; set; }  // 调用来源 — dispatcher 设置
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

4 个框架定义的 capability lifecycle events（通过 `SystemEventDescriptorProvider` 注册到 EventRegistry）:

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
| Metadata.Tests | 79 | **Phase 3/4:** RegistryBase(9), EventValidators(6), BootstrapCoordinator(4), DescriptorResolver(5), CapabilityRegistry(6, +GetByKind/GetByTag), CapabilityDescriptor(4), DescriptorIdentity(4), DescriptorRef(12), ValidationReport(2), + 原有 27 |
| Capability.Tests | 104 | **Phase 4/4a:** Resolver(9), Dispatcher(8), Audit(5), InMemoryStore(5), NullStore(2), E2E(14), Registry(4), Pipeline(6), + 遗留 |
| Draft.Tests | 13 | DraftRecord(4), InMemoryStore(5), TenantIsolated(4) |
| Event.Tests | 32 | **Phase 2a:** EventRegistry(16), Validator(4), DynamicRegistry(7), Descriptor(5) |
| Exposure.Tests | 12 | AgentTool(5), MCPTool(3), CapabilityEndpoint(4) |
| Form.Tests | 8 | Descriptor(5), Registry(3) |
| HumanTask.Tests | 8 | Descriptor(6), Registry(2) |
| Workflow.Tests | 28 | Descriptor(6), Registry(3), InteractionTarget(4), Engine(11), Resume(4) |
| **Total** | **~289** | |

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
| 44 | RegistryBase<T> 是所有 Registry 的母体 | ✅ Phase 3 |
| 45 | IDescriptor.Namespace + Id = Global Identity | ✅ Phase 3 |
| 46 | IHasContractIdentity 与 IDescriptor 解耦 | ✅ Phase 3 |
| 47 | RegistrySnapshot 使用 FrozenDictionary 不可变快照 | ✅ Phase 3 |
| 48 | IRegistryValidator<T> 可插拔验证管道 | ✅ Phase 3 |
| 49 | IRegistryValidationEngine<T> 协调验证器 | ✅ Phase 3 |
| 50 | BootstrapCoordinator 拓扑排序 + 循环依赖检测 | ✅ Phase 3 |
| 51 | IBootstrapTask.TaskId 字符串标识替代 Type | ✅ Phase 3 |
| 52 | IDescriptorResolver 统一解析入口 | ✅ Phase 3 |
| 53 | IDynamicRegistry<T> 动态注册表 Future Hook | ✅ Phase 3 |
| 54 | CapabilityDescriptor 使用 Categories 替代 CapabilityKind | ✅ Phase 3 |
| 55 | EventRef/CapabilityRef/WorkflowRef 强类型引用 | ✅ Phase 3 |
| 56 | DescriptorRef.Version = null 表示 Latest Stable | ✅ Phase 3 |
| 57 | DescriptorKey 与 DescriptorRef 语义分离 | ✅ Phase 3 |
| 58 | EventRegistry 内部迁移到 RegistryBase，API 不变 | ✅ Phase 3 |
| 59 | CapabilityRegistry 基于 RegistryBase<CapabilityDescriptor> 统一实现 | ✅ Phase 4 |
| 60 | ICapabilityRegistry + CapabilityProfile 移至 Metadata（打破 Capability.Descriptions → Metadata 循环依赖） | ✅ Phase 4 |
| 61 | CapabilityDescriptor 在 Metadata 中统一（融合 Capability.Descriptions 版运行时属性） | ✅ Phase 4 |
| 62 | CapabilityKind + CapabilityRiskLevel 移至 Metadata.Descriptions | ✅ Phase 4 |
| 63 | Id 是稳定标识符，Name 是人类显示名 — DefaultCapabilityVersionResolver 使用 Id-first 查找 | ✅ Phase 4 |
| 64 | ICapabilityDispatcher 是统一执行门面 — 注入 InvocationSource + TenantId/UserId | ✅ Phase 4 |
| 65 | ICapabilityResolver 统一解析入口 — 移至 Metadata 而非 Capability.Descriptions | ✅ Phase 4 |
| 66 | AuditMiddleware 在最外层记录所有执行结果（隔离审计失败，不阻断） | ✅ Phase 4 |
| 67 | IVersionedDescriptorRegistry.GetByVersion(id, version) — 所有 Registry 实现 | ✅ Phase 4 |
| 68 | CapabilityExecutionContext descriptor 属性 init-only（pipeline 设置），InvocationSource 为 set（dispatcher 设置） | ✅ Phase 4 |
| 69 | IBootstrapValidator / IDescriptorLookup / ICapabilityHandlerRegistry 加入 Metadata.Descriptions | ✅ Phase 4 |
| 70 | 测试: 90 个 Capability.Tests（+31 Phase 4 新增：Resolver/Dispacher/Audit） | ✅ Phase 4 |
