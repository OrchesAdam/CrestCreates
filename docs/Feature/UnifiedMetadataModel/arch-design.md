# 统一元数据模型 — 架构总结文档

> **日期:** 2026-06-16 | **状态:** 完成 | **Metadata Kernel v1.0 + Phase 3 + Phase 4 + Phase 4a + Phase 4b + Phase 4c + Phase 5 + Phase 5b + Phase 5f + Phase 6g 完成**

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

### Phase 4a: Main Chain Closure

Phase 4a 统一所有 Registry 到 RegistryBase，消除 static Provider 模式，新增集中式 Provider 存储与元数据引导器：

| 组件 | 说明 |
|------|------|
| `DescriptorProviderRegistry` | 集中式 IDescriptorProvider<T> 存储（替代 4 个 static Provider 类） |
| `MetadataBootstrapper.BuildAll()` | 统一 Build 协调器 — 遍历 6 个 Registry 执行 Build |
| `IDescriptorRegistry.Build()` | 接口级 Build 方法 — 所有 Registry 接口统一可用 |
| `Schema/Form/HumanTask/WorkflowRegistry` | 迁移到 RegistryBase（全部 6 个 Registry 统一） |
| Source Generator 统一 | 生成 IDescriptorProvider<T>（覆盖 5 种 descriptor 类型） |
| E2E 集成测试 | 14 个全链路测试：Dispatch → Pipeline → Handler → Audit |
| Cross-Registry 验证测试 | 3 个 DescriptorRef 跨 Registry 引用验证 |

### Phase 4b: Workflow Runtime Foundation

Phase 4b 将 `WorkflowEngine` 从单体执行器重构为基于 `IWorkflowStepExecutorRegistry` 的委托架构，建立最小执行闭环：

| 组件 | 说明 |
|------|------|
| `IWorkflowStepExecutor` + `IWorkflowStepExecutorRegistry` | 统一执行入口 — 避免 target-type branching |
| `CapabilityStepExecutor` / `HumanTaskStepExecutor` | Pure executors — 返回 StepExecutionResult，不修改 WorkflowInstance |
| `IWorkflowInstanceStore` / `InMemoryWorkflowInstanceStore` | 工作流实例持久化 (upsert)，替代 IDraftStore checkpoint |
| `WorkflowCompatibilityValidator` | 启动阶段验证 — 拒绝 SubWorkflowTarget, Retry, Compensate, Transitions |
| `StepExecutionResult` / `StepExecutionStatus` | 运行时状态模型 — Completed / Suspended / Failed |
| `WorkflowExecutionContext` | 纯状态传递对象 — 无 IServiceProvider |
| `IWorkflowEngine` (revised) | ExecuteAsync only，workflowId 参数，ResumeAsync 移除 |

### Phase 4c: Workflow Runtime Closure

Phase 4c 关闭 Workflow Runtime 执行闭环——HumanTask 完成后自动恢复 Workflow 执行：

| 组件 | 说明 |
|------|------|
| `IWorkflowExecutionRunner` | 共享执行核心 — Engine/ContinuationService 共用 step loop、持久化、suspended/completed/failed 事件 |
| `IWorkflowContinuationService` | 内部运行时基础设施 — 加载暂停实例、验证状态转换、写入 HumanTask StepResult、推进游标、恢复执行 |
| `IWorkflowStateMachine` / `DefaultWorkflowStateMachine` | 纯函数状态验证 — Running↔Suspended 等 4 种有效转换 |
| `IWorkflowLifecycleEventPublisher` | 5 种生命周期事件 — started/suspended/resumed/completed/failed (after save) |
| `HumanTaskCompletedWorkflowSubscriber` | Event-driven bridge — 订阅 HumanTaskCompletedEvent → WorkflowContinuationService |
| `WorkflowContinuationRequest` | 继续请求 — 包含 HumanTaskId、Outcome、Result |
| `HumanTaskCompletedEvent` (implements ILocalEvent) | HumanTask 域事件 — 无 Workflow 字段 |
| `WorkflowInstance.WaitingHumanTaskId` | 暂停时的 HumanTask 关联 — suspend 设置、resume 清空 |
| `IWorkflowInstanceStore.GetByWaitingHumanTaskId()` | 按 HumanTaskId 查询暂停实例 (唯一性, suspended-only) |
| `HumanTaskStepExecutor` (updated) | 通过 StepExecutionResult.WaitingHumanTaskId 返回 task ID |
| `WorkflowEngine` (internal constructor) | 入口事件 (started) + Runner 委托 |

### Phase 5: HumanTask Runtime Foundation

Phase 5 实现 HumanTaskInstance 运行时，让 `HumanTaskStepExecutor` 创建真实的 `HumanTaskInstance`，并通过 `IHumanTaskRuntime.CompleteAsync` 发布 `HumanTaskCompletedEvent`，触发已有 Workflow continuation 闭环：

| 组件 | 说明 |
|------|------|
| `HumanTaskInstance` | 运行时状态对象 — Id (Guid), HumanTaskId, HumanTaskVersion, Status, Assignee, Input/Output, Outcome, Timestamps |
| `HumanTaskInstanceStatus` | Created / Assigned / Completed / Cancelled |
| `HumanTaskCreationRequest` | 创建请求 — HumanTaskId, Version?, TenantId?, AssigneeUserId?, WorkflowInstanceId?, WorkflowStepId?, Input? |
| `HumanTaskCompletionRequest` | 完成请求 — HumanTaskInstanceId, Outcome, Result? |
| `IHumanTaskInstanceStore` | Instance 持久化 (upsert, GetByIdAsync, GetPendingByAssigneeAsync) |
| `InMemoryHumanTaskInstanceStore` | ConcurrentDictionary 实现 (镜像 InMemoryWorkflowInstanceStore) |
| `IHumanTaskRuntime` | 运行时入口 — CreateAsync, CompleteAsync, CancelAsync |
| `DefaultHumanTaskRuntime` | Runtime 实现 — 解析 descriptor, 创建实例, 校验 outcome, 发布 HumanTaskCompletedEvent |
| `CompletionOutcomeMatcher` | outcome 校验 helper — Condition.ToString() 匹配 (OrdinalIgnoreCase), CustomExpression 拒绝 |
| `HumanTaskServiceCollectionExtensions.AddHumanTaskRuntime()` | DI 注册 — IHumanTaskInstanceStore + IHumanTaskRuntime |
| `HumanTaskStepExecutor` (updated) | 构造函数注入 IHumanTaskRuntime, ExecuteAsync 调用 CreateAsync 创建真实 instance |
| `HumanTaskCompletedEvent` (updated) | 新增 HumanTaskInstanceId + HumanTaskVersion 字段; 保持 HumanTaskId 作为 descriptor ID |
| `HumanTaskCompletedWorkflowSubscriber` (updated) | 使用 evt.HumanTaskInstanceId 构造 WorkflowContinuationRequest |

**关键不变量:**
- `WaitingHumanTaskId` = `HumanTaskInstance.Id` (GUID), 非 descriptor ID
- `WorkflowContinuationRequest.HumanTaskId` 接收 instance ID (legacy field name)
- `HumanTaskCompletedEvent` 无 Workflow 字段 — 保持 HumanTask 域事件纯净
- `HumanTaskDescriptor` 仍然是纯元数据 — 无运行时状态
- Executor 返回 `StepExecutionResult`, 不修改 WorkflowInstance 状态

### Phase 5f: HumanTask Assignee Resolver Foundation

Phase 5f 建立 HumanTask assignee resolution 的最小主链，使 `DefaultHumanTaskRuntime.CreateAsync` 通过 `IHumanTaskAssigneeResolver` 解析分派对象：

| 组件 | 说明 |
|------|------|
| `HumanTaskAssigneeResolution` | 分派结果 DTO — AssigneeUserId/RoleId, CandidateUserIds/RoleIds, OrganizationUnitId, PositionId, AssigneeResolutionReason |
| `IHumanTaskAssigneeResolver` | 分派解析器接口 — ResolveAsync(descriptor, request, ct) |
| `DefaultHumanTaskAssigneeResolver` | 4 级累加优先级解析: 显式用户 > 显式角色 > 辅助上下文 (org/position) > 策略适配 |
| `HumanTaskCreationRequest` (extended) | 新增 RequestedOrganizationUnitId, RequestedPositionId, RequestedByUserId |
| `HumanTaskInstance` (extended) | 新增 CandidateUserIds, CandidateRoleIds, OrganizationUnitId, PositionId, AssigneeResolutionReason |
| `IHumanTaskInstanceStore` (extended) | 新增 GetPendingByCandidateUser/Role/Organization/PositionAsync |
| `DefaultHumanTaskRuntime.CreateAsync` (updated) | 通过 resolver 解析 → 应用 resolution → 状态决策 → 保存 |
| `HumanTaskServiceCollectionExtensions` (updated) | TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>() |

**关键不变量:**
- `!string.IsNullOrWhiteSpace()` 用于所有身份字段 — 空白字符串视为 null
- Candidate list snapshot 四层防护: Resolver → Runtime(.ToArray) → Clone(.ToArray) → Store(依赖 Clone)
- RoundRobin/LeastLoaded 返回 unassigned + 原因字符串
- 完全无 Organization 依赖 — HumanTask 不引用 Organization.Abstractions
- 零 Workflow 变更

### Phase 5b: Durable Runtime Store Contracts

Phase 5b 加固 InMemory Workflow 和 HumanTask 实例存储，增加原子 CAS 并发控制、浅层快照语义、幂等重复处理和事件后持久化保障：

| 组件 | 说明 |
|------|------|
| `RuntimeStoreException` | 存储层异常基类 — Metadata.Abstractions |
| `RuntimeConcurrencyException` | CAS 冲突异常 — 并发写入检测 |
| `RuntimeEntityNotFoundException` | 实体缺失异常 — GetById 失败守卫 |
| `WorkflowInstance.ConcurrencyStamp` | Guid 并发戳 — 每次保存更新，纯属性（因 Domain.Shared 同名接口冲突不实现 IHasConcurrencyStamp） |
| `WorkflowInstance.UpdatedAt` | 最后更新时间戳 |
| `WorkflowInstance.Clone()` | 浅层快照 — 复制框架拥有的集合，共享 object? payload |
| `HumanTaskInstance.ConcurrencyStamp` | 同上，HumanTask 实例并发戳 |
| `HumanTaskInstance.UpdatedAt` | 同上 |
| `HumanTaskInstance.Clone()` | 同上 |
| `InMemoryWorkflowInstanceStore` (rewritten) | 原子 CAS 循环 (TryAdd/TryUpdate)，保存/读取时 Clone，冲突 → RuntimeConcurrencyException |
| `InMemoryHumanTaskInstanceStore` (rewritten) | 同上 + `GetPendingByWorkflowAsync` |
| `IHumanTaskInstanceStore.GetPendingByWorkflowAsync()` | 按 WorkflowInstanceId 查询 pending HumanTask |
| `DefaultHumanTaskRuntime` (hardened) | 缺失实例 → RuntimeEntityNotFoundException；完成前并发戳验证 → 冲突时静默抑制事件 |
| `WorkflowContinuationService` (hardened) | 重复 HumanTaskCompletedEvent → 幂等 no-op；保存冲突 → 重新查询 HumanTaskId → null 时为重复 no-op，否则重新抛出 |
| `WorkflowContinuationRequest.HumanTaskInstanceId` | `HumanTaskId` 别名 — 明确此值为 HumanTaskInstance.Id (GUID)，非 descriptor ID |
| `InMemoryWorkflowInstanceStoreTests` | 5 个新测试 — 并发 CAS (Barrier(2) 真实争用)、查询、幂等 |
| `InMemoryHumanTaskInstanceStoreTests` | 4 个新测试 — 并发 CAS、GetPendingByWorkflowAsync |
| `HumanTaskRuntimeTests` | +1 并发失败事件抑制测试 |
| `WorkflowContinuationTests` | +2 重复处理测试、1 个更新为幂等行为 |

**关键不变量:**
- CAS 算法：`while(true)` 循环内 `TryAdd` 插入 / `TryUpdate(key, newValue, comparisonValue)` 更新 — 原子戳检查 + 替换，无 TOCTOU
- Clone 可见性：`public Clone()` 在 WorkflowInstance 和 HumanTaskInstance 上 — 跨程序集边界调用
- 浅层复制边界：`Variables`、`StepVariables`、`StepResults` 集合为新实例；`object?` 值共享。保存后修改 payload 不受支持
- 并发重复幂等：`WorkflowContinuationService.ContinueAsync` 捕获 `RuntimeConcurrencyException`，按 HumanTaskId 重新查询 — null → 重复 no-op，否则重新抛出
- 测试 Barrier：`System.Threading.Barrier(2)` + `SignalAndWait()` 强制真实并发争用

### Phase 6g: Descriptor Stable Hash Builder Public Surface

Phase 6g 将 descriptor 哈希计算收口为框架主链可注入式 API，替换临时哨兵哈希：

| 组件 | 说明 |
|------|------|
| `IDescriptorStableHashBuilder` | 构建器接口 — `Build(IDescriptor) → DescriptorStableHashes` |
| `DescriptorStableHashes` | 结果记录 — ContractHash, DefinitionHash, RuntimeHash?, BindingHash? |
| `DescriptorStableHashBuilder` | 实现 — 全 AoT 安全字符串拼接 (SHA-256)，零 JsonSerializer.Serialize 调用，显式 per-kind 字段提取，字符串分隔符转义，StringComparer.Ordinal 排序，InvariantCulture 数值格式 |
| `AddDescriptorStableHash()` | DI 注册 — `TryAddSingleton<IDescriptorStableHashBuilder, DescriptorStableHashBuilder>()` |
| `DescriptorHashComputer` → `[Obsolete]` | 旧静态类标记废弃，内部委托给 DescriptorStableHashBuilder |
| 样本迁移 | `CompanyCertificationChangeScenarios` 中所有 `"INVALIDATED"` 哨兵哈希替换为实际计算值 |
| 测试 | 15 个 DescriptorStableHashBuilderTests + 12 个样本测试（真实哈希驱动变更检测） |

**关键不变量:**
- ContractHash 按 descriptor kind 提取语义相关字段（Schema 字段、Capability Permissions/SemanticTags、Event PayloadSchema、Form ControlType、HumanTask Outcomes、Workflow Steps），集合规范化排序
- DefinitionHash 覆盖任意定义级变更 — 全 AoT 安全字符串拼接，显式 per-kind 字段枚举，StringComparer.Ordinal 排序，分隔符转义
- Permission/SemanticTag 数组已规范化排序 — `OrderBy()` 保证确定性
- RuntimeHash / BindingHash 保留字段 — 供未来运行时绑定状态分离

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
│                                           # IDescriptorStableHashBuilder, DescriptorStableHashes (Phase 6g)
├── CrestCreates.Metadata/                  # RegistryBase<T>, RegistrySnapshot<T>, RegistryValidationEngine<T>,
│                                           # BootstrapCoordinator, DescriptorResolver, CapabilityDescriptor,
│                                           # CapabilityRegistry (unified, implements ICapabilityRegistry + RegistryBase),
│                                           # EventVersionChainValidator, DuplicateNameVersionValidator,
│                                           # UniquePayloadTypeValidator, TenantScopedRegistry (implements GetByVersion),
│                                           # GlobalRegistry, Catalog, DependencyGraph, HashComputer
│                                           # ICapabilityRegistry (moved from Capability.Abstractions — breaks circular dep)
│                                           # ICapabilityResolver, ICapabilityDispatcher (moved from Capability.Abstractions)
│                                           # CapabilityProfile (moved from Capability.Abstractions)
│                                           # DescriptorProviderRegistry, MetadataBootstrapper (Phase 4a)
│                                           # RuntimeStoreException, RuntimeConcurrencyException (Phase 5b)
│                                           # RuntimeEntityNotFoundException (Phase 5b)
│                                           # DescriptorStableHashBuilder (Phase 6g)
├── CrestCreates.Schema.Abstractions/        # SchemaDescriptor, SchemaFieldDescriptor, ISchemaRegistry, ISchemaValidator
├── CrestCreates.Schema/                     # SchemaRegistry : RegistryBase<SchemaDescriptor>, SchemaValidator
├── CrestCreates.Capability.Abstractions/    # ICapabilityPipeline, CapabilityExecutionContext, CapabilityExecutionResult,
│                                           # CapabilityRef, InvocationSource, CapabilityNotFoundException,
│                                           # CapabilityExecutionRecord, ICapabilityAuditStore,
│                                           # ICapabilityHandlerResolver, ICapabilityHandlerInvoker, CapabilityPipelineDelegate
├── CrestCreates.Capability/                 # CapabilityPipeline (Id-first lookup, descriptor.Id for handler),
│                                           # CapabilityDispatcher (unified facade, injects ITenantContext/ICurrentUser),
│                                           # DefaultCapabilityResolver, DefaultCapabilityVersionResolver,
│                                           # AuditMiddleware, NullCapabilityAuditStore, InMemoryCapabilityAuditStore,
│                                           # CapabilityHandlerValidator, CapabilitySchemaValidator (bootstrap validators),
│                                           # middleware chain (9层)
│                                           # CapabilityServiceCollectionExtensions (AddCapabilityRuntime DI)
├── CrestCreates.Event.Abstractions/         # GeneratedEventDescriptor, DynamicEventDescriptor,
│                                           # IEventDescriptorProvider, IEventRegistry, IEventMetadataProvider
├── CrestCreates.Event/                      # EventRegistry : RegistryBase<GeneratedEventDescriptor>, EventResolver,
│                                           # DynamicEventRegistry, EventRegistryBootstrapper
├── CrestCreates.Form.Abstractions/          # FormDescriptor, FormFieldDescriptor, IFormRegistry
├── CrestCreates.Form/                       # FormRegistry : RegistryBase<FormDescriptor>
├── CrestCreates.HumanTask.Abstractions/     # HumanTaskDescriptor, CompletionOutcome, AssigneeStrategy, IHumanTaskRegistry,
│                                           # HumanTaskCompletedEvent (implements ILocalEvent)
│                                           # HumanTaskInstance, HumanTaskInstanceStatus, IHumanTaskInstanceStore,
│                                           # IHumanTaskRuntime, HumanTaskCreationRequest, HumanTaskCompletionRequest (Phase 5)
│                                           # HumanTaskInstance.ConcurrencyStamp, UpdatedAt, Clone() (Phase 5b)
│                                           # IHumanTaskInstanceStore.GetPendingByWorkflowAsync (Phase 5b)
│                                           # HumanTaskAssigneeResolution, IHumanTaskAssigneeResolver (Phase 5f)
│                                           # HumanTaskCreationRequest.RequestedOrganizationUnitId/PositionId/ByUserId (Phase 5f)
│                                           # HumanTaskInstance.CandidateUserIds/RoleIds, OrganizationUnitId, PositionId,
│                                           #   AssigneeResolutionReason (Phase 5f)
│                                           # IHumanTaskInstanceStore.GetPendingByCandidateUser/Role/Organization/PositionAsync (Phase 5f)
├── CrestCreates.HumanTask/                  # HumanTaskRegistry : RegistryBase<HumanTaskDescriptor>
│                                           # InMemoryHumanTaskInstanceStore (CAS + clone + GetPendingByWorkflowAsync, Phase 5b;
│                                           #   4 new pending queries Phase 5f), CompletionOutcomeMatcher,
│                                           # DefaultHumanTaskRuntime (concurrency guard Phase 5b, resolver integration Phase 5f),
│                                           # DefaultHumanTaskAssigneeResolver (Phase 5f), HumanTaskServiceCollectionExtensions (Phase 5)
├── CrestCreates.Workflow.Abstractions/      # WorkflowDescriptor, WorkflowStep, InteractionTarget (Capability/HumanTask/SubWorkflow),
│                                           # IWorkflowEngine (ExecuteAsync only), IWorkflowRegistry,
│                                           # IWorkflowStepExecutor, IWorkflowStepExecutorRegistry,
│                                           # StepExecutionResult, StepExecutionStatus, WorkflowExecutionContext,
│                                           # IWorkflowInstanceStore, WorkflowStepResult,
│                                           # IWorkflowStateMachine, IWorkflowLifecycleEventPublisher,
│                                           # WorkflowLifecycleEvent, IWorkflowContinuationService,
│                                           # WorkflowContinuationRequest, WorkflowCorrelationException
│                                           # WorkflowInstance.ConcurrencyStamp, UpdatedAt, Clone() (Phase 5b)
├── CrestCreates.Workflow/                   # WorkflowRegistry : RegistryBase<WorkflowDescriptor>,
│                                           # WorkflowEngine (internal ctor, factory DI, delegates to IWorkflowExecutionRunner),
│                                           # IWorkflowExecutionRunner (internal), WorkflowExecutionRunner (owns persistence + events),
│                                           # CapabilityStepExecutor, HumanTaskStepExecutor (injects IHumanTaskRuntime, Phase 5),
│                                           # DefaultStepExecutorRegistry, DefaultWorkflowStateMachine,
│                                           # InMemoryWorkflowInstanceStore (CAS + clone, Phase 5b), WorkflowLifecycleEventPublisher (no-op),
│                                           # WorkflowContinuationService (idempotent dup, Phase 5b),
│                                           # HumanTaskCompletedWorkflowSubscriber (uses HumanTaskInstanceId, Phase 5),
│                                           # WorkflowCompatibilityValidator (bootstrap)
├── CrestCreates.Draft.Abstractions/         # DraftRecord, IDraftStore, DraftStatus
├── CrestCreates.Draft/                      # InMemoryDraftStore, TenantIsolatedDraftStore
├── CrestCreates.Exposure.Abstractions/      # AgentToolDescriptor, MCPToolDescriptor, ToolCallMode
└── framework/tools/CrestCreates.CodeGenerator/  # 5 source generators (Schema/Capability/Event/Form/HumanTask/Workflow + Handler + RefValidation)

framework/test/
├── CrestCreates.Schema.Tests/               (20)
├── CrestCreates.Metadata.Tests/             (355) ← Phase 3/4/4a/6g: RegistryBase, Validators, Bootstrap, Resolver, CapabilityRegistry, RefValidation, StableHashBuilder(15)
├── CrestCreates.Capability.Tests/           (104) ← Phase 4/4a: Resolver, Dispatcher, Audit, E2E(14), Registry migration
├── CrestCreates.Draft.Tests/               (13)
├── CrestCreates.Event.Tests/               (32)
├── CrestCreates.Exposure.Tests/            (12)
├── CrestCreates.Form.Tests/                (9)
├── CrestCreates.HumanTask.Tests/           (44)  ← Phase 5: +7 (Runtime 6 + Store 1); Phase 5b: +5 (Store 4 + Runtime 1);
│                                           # Phase 5f: +22 (10 Resolver + 6 Runtime + 6 Store)
└── CrestCreates.Workflow.Tests/            (57)  ← Phase 5: +2 (Executor unit + E2E); Phase 5b: +6 (Store 5 + Continuation 1)
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
WorkflowDescriptor                             HumanTaskInstance ✅ Phase 5
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

## 5. Workflow 运行时引擎 (Phase 4b Foundation + Phase 4c Closure)

### 5.1 Phase 4c 目标架构

```
IWorkflowEngine (ExecuteAsync only — 不变)
    │
    └── WorkflowEngine (internal ctor, factory DI)
          │
          ├── IWorkflowExecutionRunner (internal)  ── 共享执行核心 (Engine + ContinuationService)
          │     └── Owns: step loop, persistence, suspended/completed/failed events
          ├── IWorkflowStateMachine                 ── ValidateTransition(from, to)
          ├── IWorkflowLifecycleEventPublisher      ── 5 种生命周期事件 (after save)
          ├── IWorkflowStepExecutorRegistry
          └── IWorkflowInstanceStore                ── + GetByWaitingHumanTaskId()

IWorkflowContinuationService
    │
    ├── store.GetByWaitingHumanTaskId() → load Suspended instance
    ├── stateMachine.ValidateTransition(Suspended, Running)
    ├── Write HumanTask StepResult → advance cursor → clear WaitingHumanTaskId
    ├── Publish workflow.resumed
    └── executionRunner.RunAsync() → remaining steps

HumanTaskCompletedWorkflowSubscriber (ILocalEventHandler<HumanTaskCompletedEvent>)
    └── Bridges HumanTaskCompletedEvent → IWorkflowContinuationService.ContinueAsync()
```

### 5.2 HumanTask suspend/resume 完整闭环 (Phase 5 updated)

```
Suspend (in WorkflowExecutionRunner):
    HumanTaskStepExecutor → IHumanTaskRuntime.CreateAsync() → HumanTaskInstance (Id = Guid)
    HumanTaskStepExecutor → StepExecutionResult(Suspended, WaitingHumanTaskId=instance.Id)
    Runner: instance.WaitingHumanTaskId = stepResult.WaitingHumanTaskId  (HumanTaskInstance.Id)
    Status = Suspended → save → publish workflow.suspended → return

Resume (in WorkflowContinuationService):
    Load instance via GetByWaitingHumanTaskId(humanTaskInstanceId)
    ValidateTransition(Suspended, Running)
    Write HumanTask StepResult (Status=Completed, Output=request.Result)
    Variables["lastStepOutcome"] = request.Outcome
    Variables["lastStepResult"] = request.Result
    StepIndex++ → WaitingHumanTaskId = null → Status = Running
    Save → publish workflow.resumed
    executionRunner.RunAsync() → remaining steps
```

**Phase 5 关键变化:**
- `WaitingHumanTaskId` 现在是 `HumanTaskInstance.Id` (GUID)，不再是 descriptor ID
- `HumanTaskStepExecutor` 通过 `IHumanTaskRuntime.CreateAsync()` 创建真实实例
- `HumanTaskCompletedEvent.HumanTaskInstanceId` 携带 instance ID，subscriber 以此查找 workflow
- `WorkflowContinuationRequest.HumanTaskId` 接收 instance ID (legacy field name)

### 5.3 生命周期事件

| 事件 | 触发时机 | 负责组件 |
|------|----------|----------|
| `workflow.started` | 创建实例后、Status=Running、保存成功、首次执行前 | Engine |
| `workflow.suspended` | HumanTask 步骤返回 Suspended、保存成功后、返回前 | Runner |
| `workflow.resumed` | Suspended→Running 验证通过、保存成功后、执行前 | ContinuationService |
| `workflow.completed` | 所有步骤完成、Status=Completed、保存成功后 | Runner |
| `workflow.failed` | 步骤失败、Status=Failed、保存成功后 | Runner |

**所有事件在 WorkflowInstance 成功保存后发布。**

### 5.4 架构不变量

- **WorkflowEngine never performs target-type branching** — 所有 dispatch 通过 `IWorkflowStepExecutorRegistry`
- **Executors are pure** — 返回 `StepExecutionResult`，不修改 `WorkflowInstance` 状态；Variables 由 Engine 应用
- **Bootstrap validation** — `SubWorkflowTarget`、`Retry`、`Compensate`、`Transitions` 在启动阶段被拒绝
- **Metadata retained, execution paths removed** — descriptor 元数据保留，runtime 执行路径被移除

### 5.5 已移除的执行路径（Phase 4b 范围外）

| 移除项 | 后续阶段 |
|--------|----------|
| `ResumeAsync` | Phase 5/6 |
| SubWorkflow 执行 | Phase 5+ |
| Retry (MaxRetries=3) | Phase 5+ |
| Compensation | Phase 5+ |
| Branch/Transition | Phase 5+ |
| `IDraftStore` checkpoint | — |
| `ICapabilityPipeline?` nullable | — |

### 5.6 新增类型一览

| 类型 | 位置 | 职责 |
|------|------|------|
| `StepExecutionResult` | Workflow.Abstractions | executor 契约输出 (Status, Output, Variables) |
| `StepExecutionStatus` | Workflow.Abstractions | Completed / Suspended / Failed |
| `IWorkflowStepExecutor` | Workflow.Abstractions | 单步执行器接口 |
| `IWorkflowStepExecutorRegistry` | Workflow.Abstractions | 按 InteractionTarget 解析 executor |
| `IWorkflowInstanceStore` | Workflow.Abstractions | 工作流实例持久化 (upsert) |
| `WorkflowExecutionContext` | Workflow.Abstractions | 纯状态传递对象 (无 IServiceProvider) |
| `WorkflowCompatibilityValidator` | Workflow | 启动阶段验证器 |

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
| Schema.Tests | 20 | Descriptor 创建, Registry Build + GetById/GetByVersion/GetByName/GetActiveVersion/GetDeprecatedVersions/GetAllByName, Validator(10) |
| Metadata.Tests | 355 | **Phase 3/4/4a/6g:** RegistryBase(9), Validators(6), Bootstrap(4), Resolver(5), CapabilityRegistry(6), CapabilityDescriptor(4), DescriptorRef(12), RefValidation(3), StableHashBuilder(15), + 原有 291 |
| Capability.Tests | 104 | **Phase 4/4a:** Resolver(9), Dispatcher(8), Audit(5), InMemoryStore(5), NullStore(2), E2E(14), Registry(4), Pipeline(6), + 遗留 |
| Draft.Tests | 13 | DraftRecord(4), InMemoryStore(5), TenantIsolated(4) |
| Event.Tests | 32 | EventRegistry(16), Validator(4), DynamicRegistry(7), Descriptor(5) |
| Exposure.Tests | 12 | AgentTool(5), MCPTool(3), CapabilityEndpoint(4) |
| Form.Tests | 9 | Descriptor(5), Registry(4) |
| HumanTask.Tests | 44 | Descriptor(6), Registry(3), **Store(11), Runtime(14), Resolver(10)** |
| Workflow.Tests | 57 | Executor Registry(5), Validator(14), Engine(13), Runtime(5), StateMachine(7), Continuation(7), **Store(5), Executor(1), E2E(1)** |
| **Total** | **~499** | |

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
| 71 | DescriptorProviderRegistry 集中式 Provider 存储 — Registry 与 Provider Discovery 分离 | ✅ Phase 4a |
| 72 | MetadataBootstrapper.BuildAll() 统一 Build 协调 — 6 个 Registry 同一生命周期 | ✅ Phase 4a |
| 73 | 4 个 static *RegistryProvider.cs 废弃 — SG 改为生成 IDescriptorProvider<T> | ✅ Phase 4a |
| 74 | Schema/Form/HumanTask/Workflow Registry 迁移到 RegistryBase — 全部 6 个 Registry 统一 | ✅ Phase 4a |
| 75 | Event Provider 统一为 IDescriptorProvider<GeneratedEventDescriptor> | ✅ Phase 4a |
| 76 | IDescriptorRegistry.Build() 接口级方法 — 所有 Registry 接口统一 Build | ✅ Phase 4a |
| 77 | HandlerResolver 仅接受 Id — Name 永不参与 Runtime Dispatch | ✅ Phase 4a |
| 78 | 测试: 104 个 Capability.Tests（+14 E2E + 3 RefValidation） | ✅ Phase 4a |
| 79 | WorkflowEngine 重构为 registry-based dispatch — IWorkflowStepExecutorRegistry | ✅ Phase 4b |
| 80 | Executors are pure — 返回 StepExecutionResult，不修改 WorkflowInstance | ✅ Phase 4b |
| 81 | WorkflowExecutionContext 是纯状态对象 — 无 IServiceProvider | ✅ Phase 4b |
| 82 | IWorkflowInstanceStore (upsert) 替代 IDraftStore checkpoint | ✅ Phase 4b |
| 83 | Boostrap validation — 启动阶段拒绝不支持的 Workflow 构造 | ✅ Phase 4b |
| 84 | StepExecutionStatus {Completed, Suspended, Failed} 替代 bool IsSuccess | ✅ Phase 4b |
| 85 | IWorkflowEngine 移除 ResumeAsync — 仅保留 ExecuteAsync | ✅ Phase 4b |
| 86 | Metadata retained, execution paths removed — SubWorkflow/Retry/Compensate/Transition | ✅ Phase 4b |
| 87 | 测试: 37 个 Workflow.Tests（含 validator、executor registry、runtime integration） | ✅ Phase 4b |
| 88 | IWorkflowExecutionRunner 是共享执行核心 — Engine/ContinuationService 共用 step loop + 持久化 + 事件 | ✅ Phase 4c |
| 89 | IWorkflowContinuationService — 运行时管理的 continuation 基础设施，非公共 API | ✅ Phase 4c |
| 90 | IWorkflowStateMachine — 纯函数 ValidateTransition(from, to)，4 种有效转换 | ✅ Phase 4c |
| 91 | 所有 lifecycle event 在 WorkflowInstance 成功保存后发布 | ✅ Phase 4c |
| 92 | HumanTaskCompletedEvent 无 Workflow 字段 — HumanTask 模块仅依赖 Event Runtime 契约 | ✅ Phase 4c |
| 93 | HumanTaskCompletedWorkflowSubscriber — Event-driven bridge (ILocalEventHandler<HumanTaskCompletedEvent>) | ✅ Phase 4c |
| 94 | WorkflowContinuationRequest — 携带 HumanTaskId, Outcome, Result；不破坏 future 签名扩展 | ✅ Phase 4c |
| 95 | ContinuationService 写入 HumanTask StepResult 后推进游标 — StepResults 完整 | ✅ Phase 4c |
| 96 | WaitingHumanTaskId suspend 设置, resume 清空 — GetByWaitingHumanTaskId 唯一性 + suspended-only | ✅ Phase 4c |
| 97 | WorkflowEngine internal ctor + factory DI — IWorkflowExecutionRunner 不泄漏到 public API | ✅ Phase 4c |
| 98 | 测试: 47->51 个 Workflow.Tests（+1 Executor +1 E2E HumanTask-complete-resume） | ✅ Phase 5 |
| 99 | HumanTaskInstance 运行时对象 — Id (Guid), HumanTaskId, HumanTaskVersion, Status, Assignee, Input/Output, timestamps | ✅ Phase 5 |
| 100 | IHumanTaskInstanceStore — ConcurrentDictionary upsert, GetPendingByAssigneeAsync (Created\|Assigned) | ✅ Phase 5 |
| 101 | IHumanTaskRuntime — CreateAsync (descriptor resolve + instance create), CompleteAsync (outcome validate + event publish), CancelAsync | ✅ Phase 5 |
| 102 | CompletionOutcomeMatcher — Condition.ToString() 匹配 (OrdinalIgnoreCase), 拒绝 CustomExpression | ✅ Phase 5 |
| 103 | HumanTaskStepExecutor 构造函数注入 IHumanTaskRuntime — 创建真实 HumanTaskInstance | ✅ Phase 5 |
| 104 | HumanTaskCompletedEvent 新增 HumanTaskInstanceId + HumanTaskVersion; 保持 HumanTaskId 作为 descriptor ID | ✅ Phase 5 |
| 105 | HumanTaskCompletedWorkflowSubscriber 使用 evt.HumanTaskInstanceId — 按 instance ID 查找 workflow | ✅ Phase 5 |
| 106 | WaitingHumanTaskId = HumanTaskInstance.Id (GUID) — 不再使用 descriptor ID | ✅ Phase 5 |
| 107 | HumanTaskServiceCollectionExtensions.AddHumanTaskRuntime() — DI 注册 store + runtime | ✅ Phase 5 |
| 108 | HumanTask 运行时发布 HumanTaskCompletedEvent; Workflow 不直接完成 HumanTask — 事件驱动 continuation | ✅ Phase 5 |
| 109 | HumanTaskCompletedEvent 无 Workflow 字段 — Workflow correlation 仅存于 HumanTaskInstance/creation request | ✅ Phase 5 |
| 110 | HumanTaskDescriptor 仍然是纯元数据 — 不持有 instance state | ✅ Phase 5 |
| 111 | RuntimeStoreException — 所有 store-level 异常的基类（Metadata.Abstractions） | ✅ Phase 5b |
| 112 | RuntimeConcurrencyException — CAS 冲突；并发写入 Detect/Prevent（Metadata.Abstractions） | ✅ Phase 5b |
| 113 | RuntimeEntityNotFoundException — 实体缺失；GetById 失败时 Runtime 守卫 | ✅ Phase 5b |
| 114 | WorkflowInstance + HumanTaskInstance ConcurrencyStamp 作为纯属性 — 因 Domain.Shared 接口冲突不实现 IHasConcurrencyStamp | ✅ Phase 5b |
| 115 | 原子 CAS 算法 — while(true) TryAdd/TryUpdate；戳检查 + 替换为原子操作，无 TOCTOU | ✅ Phase 5b |
| 116 | Shallow Clone 语义 — 框架拥有的集合复制；object? payload 引用共享；保存后修改 payload 不受支持 | ✅ Phase 5b |
| 117 | 并发重复幂等 — WorkflowContinuationService 捕获 RuntimeConcurrencyException，重新查询 HumanTaskId；null → 重复 no-op | ✅ Phase 5b |
| 118 | HumanTask 并发守卫 — DefaultHumanTaskRuntime 完成前验证戳；冲突时抑制事件，不发布虚假 HumanTaskCompletedEvent | ✅ Phase 5b |
| 119 | Barrier(2) 并发测试 — System.Threading.Barrier 强制同时进入 SaveAsync，证明 CAS 在真实争用下工作 | ✅ Phase 5b |
| 120 | IHumanTaskAssigneeResolver — 4 级累加优先级: 显式用户 > 显式角色 > 辅助上下文 (org/position) > 策略适配 | ✅ Phase 5f |
| 121 | HumanTaskAssigneeResolution — 不可变 DTO，computed IsAssigned/HasCandidates/IsUnassigned | ✅ Phase 5f |
| 122 | HumanTaskCreationRequest 扩展 — RequestedOrganizationUnitId, RequestedPositionId, RequestedByUserId (audit only) | ✅ Phase 5f |
| 123 | HumanTaskInstance 扩展 — CandidateUserIds/RoleIds, OrganizationUnitId, PositionId, AssigneeResolutionReason | ✅ Phase 5f |
| 124 | HumanTaskInstance.Clone() 包含 .ToArray() snapshot — candidate lists 不可变引用泄露 | ✅ Phase 5f |
| 125 | IHumanTaskInstanceStore 4 个新查询 — GetPendingByCandidateUser/Role/Organization/PositionAsync | ✅ Phase 5f |
| 126 | DefaultHumanTaskRuntime.CreateAsync 通过 resolver 解析 — 状态决策使用 !IsNullOrWhiteSpace | ✅ Phase 5f |
| 127 | DI: TryAddScoped<IHumanTaskAssigneeResolver, DefaultHumanTaskAssigneeResolver>() | ✅ Phase 5f |
| 128 | 无 Organization 依赖 — HumanTask 不引用 Organization.Abstractions | ✅ Phase 5f |
| 129 | 零 Workflow 变更 — HumanTaskStepExecutor 未修改 | ✅ Phase 5f |
| 130 | 测试: 44 个 HumanTask.Tests（+22 Phase 5f: 10 Resolver + 6 Runtime + 6 Store） | ✅ Phase 5f |
| 131 | IDescriptorStableHashBuilder — 框架主链可注入式哈希构建器，替代 DescriptorHashComputer 静态类 | ✅ Phase 6g |
| 132 | DescriptorStableHashes — ContractHash/DefinitionHash/RuntimeHash?/BindingHash? 统一记录 | ✅ Phase 6g |
| 133 | DescriptorHashComputer 标记 [Obsolete]，内部委托给 DescriptorStableHashBuilder — 零破坏性变更 | ✅ Phase 6g |
| 134 | AddDescriptorStableHash() DI 注册 — TryAddSingleton，与现有 Metadata DI 模式一致 | ✅ Phase 6g |
| 135 | ContractHash 按 descriptor kind switch 正则化提取，集合规范化排序 — 全 AoT 安全字符串拼接 | ✅ Phase 6g |
| 136 | Permission/SemanticTag 数组已规范化排序 — OrderBy() 保证确定性 | ✅ Phase 6g |
| 137 | CompanyCertificationChangeScenarios 中所有 "INVALIDATED" 哨兵哈希已替换 | ✅ Phase 6g |
| 138 | 测试: 355 个 Metadata.Tests（+15 StableHashBuilder）+ 12 个 Sample Tests | ✅ Phase 6g |
