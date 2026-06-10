# Phase 4a — Main Chain Closure Design Spec

> **日期:** 2026-06-10 | **状态:** 已评审（架构反馈已纳入） | **父 Phase:** Phase 4

---

## 1. 目标

Phase 4 完成 Capability 运行时收口后，存在三项结构性债务阻碍框架主链闭合（详见架构评审）。Phase 4a 的目标是一次性偿还这些债务，使框架达到"功能可闭合"状态。

### 债务清单

| 优先级 | 债务 | 说明 |
|--------|------|------|
| P0 | Registry 实现不一致 | 4/6 Registry 仍用 ConcurrentDictionary，未迁移到 RegistryBase |
| P0 | 核心闭环无集成测试 | Dispatcher → Pipeline → Handler → Audit 链路无真实 E2E 测试 |
| P1 | Id/Name 语义未统一 | Pipeline 参数名 `capabilityName` 误导，Handler key 语义模糊 |
| P1 | Source Generator ↔ Runtime 对接缺失 | Form/HumanTask/Workflow 的 SG 未生成注册代码 |
| P2 | 空 catch 块 | 3 处在 UnitOfWorkFactory.cs，已有注释说明意图，不处理 |

---

## 2. 前置条件

Phase 4a 依赖以下已完成产物：

| 前置条件 | 来源 | 状态 |
|----------|------|------|
| `RegistryBase<T>` + `RegistrySnapshot<T>` + `RegistryValidationEngine<T>` | Phase 3 | ✅ |
| `IDescriptorProvider<T>` 接口 | Phase 3（Metadata.Abstractions） | ✅ |
| CapabilityRegistry 已迁移到 RegistryBase（参考实现） | Phase 4 | ✅ |
| `ISchemaDescriptorProvider` / `IFormDescriptorProvider` / `IHumanTaskDescriptorProvider` / `IWorkflowDescriptorProvider` 接口已存在 | Phase 1-2 | ✅ |
| `SchemaCapabilitySourceGenerator` 已收集所有 descriptor 类型的 Provider 信息 | Phase 2a | ✅ |

**Consumer 项目引用要求（Source Generator 改动后）：**

SG 生成的代码将引用 `CrestCreates.Metadata`（for `DescriptorProviderRegistry`）和 `CrestCreates.Metadata.Abstractions`（for `IDescriptorProvider<T>`）。所有使用 SG 的 consumer 项目（如 samples/LibraryManagement.Domain）必须已引用这两个程序集。当前检查：`LibraryManagement.Domain` 通过依赖链已间接引用两者。Phase 4a 开始前需显式验证。

---

## 3. Registry 迁移

### 3.1 目标架构

所有 6 个 Registry 统一基于 `RegistryBase<T>`：

```
CapabilityRegistry : RegistryBase<CapabilityDescriptor>  ✅ Phase 4
EventRegistry      : RegistryBase<GeneratedEventDescriptor> ✅ Phase 3
SchemaRegistry     : RegistryBase<SchemaDescriptor>        ❌→✅ Phase 4a
HumanTaskRegistry  : RegistryBase<HumanTaskDescriptor>     ❌→✅ Phase 4a
FormRegistry       : RegistryBase<FormDescriptor>          ❌→✅ Phase 4a
WorkflowRegistry   : RegistryBase<WorkflowDescriptor>      ❌→✅ Phase 4a
```

### 3.2 架构原则：Provider Discovery 与 Registry 分离

**Registry 不承担 Provider Discovery/Storage 职责。** 这是 RegistryBase 原始模型的核心：

```
Provider  →  DescriptorProviderRegistry (存储)  →  MetadataBootstrapper.BuildAll() (协调)  →  Registry.Build(providers)
```

每个 Registry 保持纯粹：
- `RegistryBase<T>`: 持有快照 + 验证 + 查询
- 不含静态 Collector / Provider 存储

### 3.3 新增：DescriptorProviderRegistry

```csharp
// CrestCreates.Metadata
public static class DescriptorProviderRegistry
{
    private static readonly ConcurrentBag<object> _providers = new();

    public static void Register<T>(IDescriptorProvider<T> provider) where T : class, IDescriptor
        => _providers.Add(provider);

    public static IReadOnlyList<IDescriptorProvider<T>> GetProviders<T>() where T : class, IDescriptor
        => _providers.OfType<IDescriptorProvider<T>>().ToList();
}
```

> **实现优化（不阻塞 Phase 4a）**: 当 provider 数量增长后，可将 `ConcurrentBag<object>` 改为 `ConcurrentDictionary<Type, List<object>>`，使 `GetProviders<T>()` 从 O(n) 扫描变为 O(1) 索引。

### 3.4 新增：MetadataBootstrapper.BuildAll()

```csharp
// CrestCreates.Metadata — BootstrapCoordinator 调用
public static class MetadataBootstrapper
{
    public static void BuildAll(
        ISchemaRegistry schemaRegistry,
        IFormRegistry formRegistry,
        IHumanTaskRegistry humanTaskRegistry,
        IWorkflowRegistry workflowRegistry,
        IEventRegistry eventRegistry)
    {
        schemaRegistry.Build(DescriptorProviderRegistry.GetProviders<SchemaDescriptor>());
        formRegistry.Build(DescriptorProviderRegistry.GetProviders<FormDescriptor>());
        humanTaskRegistry.Build(DescriptorProviderRegistry.GetProviders<HumanTaskDescriptor>());
        workflowRegistry.Build(DescriptorProviderRegistry.GetProviders<WorkflowDescriptor>());
        eventRegistry.Build(DescriptorProviderRegistry.GetProviders<GeneratedEventDescriptor>());
    }
}
```

> **Architecture Enhancement (Phase 5+)**: 当前 `MetadataBootstrapper` 直接依赖具体 Registry 接口。未来引入 `IRegistryBuilder { void Build(); }` 接口后，可改为：
> ```csharp
> foreach (var builder in registries.OfType<IRegistryBuilder>())
>     builder.Build();
> ```
> 这样新增 `AgentRegistry` / `TemplateRegistry` 等不再需要修改 Bootstrapper。不阻塞 Phase 4a。

> **Bootstrap 时序**：`MetadataBootstrapper.BuildAll()` 由 `BootstrapCoordinator` 在 `IBootstrapTask` 执行阶段调用，位于所有 `[ModuleInitializer]` 完成后、WorkflowRuntime/HumanTaskRuntime 初始化前。具体位置：`BootstrapCoordinator` 现有 task pipeline 末尾新增 `MetadataBootstrapperTask : IBootstrapTask`。

### 3.5 每个 Registry 的改动模式

以 `SchemaRegistry` 为例：

```
当前:
  ConcurrentDictionary<string, T> _byId
  ConcurrentDictionary<string, List<T>> _byName
  void Register(T) → 写入字典

改为:
  RegistryBase<T> (继承)
  protected override string RegistryNamespace → "schema"
  protected override RegistrySnapshot<T> BuildSnapshot(List<T>) → FrozenDictionary
  （无 static 状态，无 Provider 收集）
```

### 3.6 保留的接口方法

Registry 迁移后，现有 `ISchemaRegistry` / `IHumanTaskRegistry` / `IFormRegistry` / `IWorkflowRegistry` 接口不变。  
消费者（WorkflowEngine、WorkflowEventConsumer）仅依赖接口，不受影响。

### 3.7 受影响文件

| 文件 | 操作 |
|------|------|
| `framework/src/CrestCreates.Schema/SchemaRegistry.cs` | 重写 |
| `framework/src/CrestCreates.Schema/SchemaRegistryProvider.cs` | **删除** |
| `framework/src/CrestCreates.HumanTask/HumanTaskRegistry.cs` | 重写 |
| `framework/src/CrestCreates.HumanTask/HumanTaskRegistryProvider.cs` | **删除** |
| `framework/src/CrestCreates.Form/FormRegistry.cs` | 重写 |
| `framework/src/CrestCreates.Form/FormRegistryProvider.cs` | **删除** |
| `framework/src/CrestCreates.Workflow/WorkflowRegistry.cs` | 重写 |
| `framework/src/CrestCreates.Workflow/WorkflowRegistryProvider.cs` | **删除** |
| `framework/test/CrestCreates.Schema.Tests/SchemaRegistryTests.cs` | 更新 |
| `framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs` | 更新 |
| `framework/test/CrestCreates.HumanTask.Tests/HumanTaskRegistryTests.cs` | 更新 |
| `framework/test/CrestCreates.Workflow.Tests/WorkflowRegistryTests.cs` | 更新 |

---

## 4. Source Generator 改造

### 4.1 当前状态

`SchemaCapabilitySourceGenerator.GenerateRegistries()` 当前行为：

| Descriptor 类型 | 收集 | 生成注册代码 |
|-----------------|------|-------------|
| Schema | ✅ | ✅ `SchemaRegistryProvider.Register(...)` |
| Form | ✅ | ❌ 无 |
| HumanTask | ✅ | ❌ 无 |
| Workflow | ✅ | ❌ 无 |
| Event | ✅ | ✅ `IEventDescriptorProvider`（单独模式，不一致） |
| Capability | ✅ | ❌ 已注释 |

### 4.2 改造方案

**两个核心变更：**

1. **统一 Event**：Event 不再使用 `IEventDescriptorProvider`，改为和 Schema/Form/HumanTask/Workflow 完全一致：生成 `IDescriptorProvider<GeneratedEventDescriptor>`
2. **统一注册入口**：所有 provider 注册到 `DescriptorProviderRegistry`，不再注册到具体 Registry

#### 生成代码格式

```csharp
// <auto-generated />
namespace CrestCreates.Generated;

// Provider 实现（每种 descriptor 类型一个）
internal sealed class GeneratedSchemaProvider : IDescriptorProvider<SchemaDescriptor>
{
    public IReadOnlyList<SchemaDescriptor> GetDescriptors() => new List<SchemaDescriptor>
    {
        new SchemaDescriptor { Id = "...", ... },
    };
}

internal sealed class GeneratedFormProvider : IDescriptorProvider<FormDescriptor> { ... }
internal sealed class GeneratedHumanTaskProvider : IDescriptorProvider<HumanTaskDescriptor> { ... }
internal sealed class GeneratedWorkflowProvider : IDescriptorProvider<WorkflowDescriptor> { ... }
internal sealed class GeneratedEventProvider : IDescriptorProvider<GeneratedEventDescriptor> { ... }

// ModuleInitializer 注册到 DescriptorProviderRegistry（不是某个具体 Registry）
internal static class GeneratedDescriptorRegistry
{
    [ModuleInitializer]
    internal static void Register()
    {
        DescriptorProviderRegistry.Register(new GeneratedSchemaProvider());
        DescriptorProviderRegistry.Register(new GeneratedFormProvider());
        DescriptorProviderRegistry.Register(new GeneratedHumanTaskProvider());
        DescriptorProviderRegistry.Register(new GeneratedWorkflowProvider());
        DescriptorProviderRegistry.Register(new GeneratedEventProvider());
    }
}
```

**注意：** `IEventDescriptorProvider` 接口本身保留不删除（已有 consumer 实现它），但 source generator 不再生成它的实现 — 改为生成 `IDescriptorProvider<GeneratedEventDescriptor>`。

> **Capability 统一化不属 Phase 4a**：Capability 的 source generator 生成代码当前为注释状态。Capability Provider 统一到 `IDescriptorProvider<CapabilityDescriptor>` 将在后续 Metadata Runtime Consolidation 中处理。

#### Source Generator 改动点

- 删除 `IEventDescriptorProvider` 的 generated class 生成逻辑（`GeneratedCapabilityEventDescriptorProvider`）
- 新增 `IDescriptorProvider<GeneratedEventDescriptor>` 的 generated class 生成
- Schema 部分：`SchemaRegistryProvider.Register(...)` → 生成 provider class + `DescriptorProviderRegistry.Register(...)`
- Form/HumanTask/Workflow 部分：新增 provider class 生成 + `DescriptorProviderRegistry.Register(...)`
- Namespace 引用更新：移除 `using CrestCreates.Schema` 等 Provider 相关 using

> ⚠️ **风险标注**：Source Generator 是本 Phase 最危险改动。它是 `netstandard2.0` Roslyn analyzer，修改后必须拿至少一个 consumer 项目（如 `samples/LibraryManagement/LibraryManagement.Domain`）做全量编译验证。仅运行 Source Generator 的单元测试不足以验证生成代码在真实 consumer 项目中可编译。
>
> **Consumer 引用**：SG 生成代码引用 `DescriptorProviderRegistry`（`CrestCreates.Metadata`）和 `IDescriptorProvider<T>`（`CrestCreates.Metadata.Abstractions`）。所有使用 SG 的 consumer 项目必须已引用这两个程序集。

---

## 5. 集成测试

### 5.1 全链路 E2E 测试

**文件**: `framework/test/CrestCreates.Capability.Tests/CapabilityEndToEndTests.cs`

不依赖 Mock — 用真实组件 + ServiceCollection：

```
Registry.Build → Resolver → Dispatcher → Pipeline → Handler → Audit → 验证
```

#### A. 正常执行路径 (4 个)

| # | 测试 | 验证点 |
|---|------|--------|
| A1 | E2E_Execute_ReturnsSuccess_AndAuditRecorded | Output 正确, Audit.IsSuccess, Duration>0, CorrelationId 非空, ExecutionId 非空 |
| A2 | E2E_WithTenantAndUser_PopulatesAuditContext | Audit.TenantId/UserId 与注入值一致 |
| A3 | E2E_InvocationSource_Http_Workflow_Agent | 每种 Source 的 Audit.Source 正确 |
| A4 | E2E_IdDifferentFromName_PreservesBoth | Id="echo.v2", Name="Echo Command" → Audit.CapabilityId/Name 各自正确 |

#### B. 错误路径 (4 个)

| B1 | E2E_CapabilityNotFound_ReturnsErrorCode | ErrorCode="CAPABILITY_NOT_FOUND", Audit.ErrorCode 相同 |
| B2 | E2E_HandlerNotFound_ReturnsErrorCode | ErrorCode="HANDLER_NOT_FOUND" |
| B3 | E2E_HandlerThrows_RecordsUnhandledException | ErrorCode="PIPELINE_ERROR", Audit.ErrorCode="UNHANDLED_EXCEPTION" |
| B4 | E2E_Cancelled_RecordsCancelledStatus | Audit.ErrorCode="CANCELLED" |

#### C. 审计存储 (3 个)

| C1 | E2E_AuditRecord_AllFieldsPopulated | 所有字段非默认值 |
| C2 | E2E_TwoExecutions_ProduceTwoAuditRecords | 不同 ExecutionId |
| C3 | E2E_AuditStoreThrows_ExecutionStillSucceeds | 审计失败隔离 |

#### D. Registry 集成 (2 个)

| D1 | E2E_MultiVersion_ResolverReturnsActive | v1=Active, v2=Deprecated → 返回 v1 |
| D2 | E2E_GetByKind_And_GetByTag | 按 Kind/Tag 筛选正确 |

#### E. Id/Name 语义 (2 个)

| E1 | E2E_Pipeline_AcceptsId | 推荐路径 |
| E2 | Legacy_NameLookup_BackwardCompatibility | ⚠️ Backward Compat — 不推荐 |

> E1: `ExecuteAsync("echo.v2")` → GetById 命中。E2: `ExecuteAsync("Echo Command")` → Name fallback 命中。  
> **新代码应始终传 Id。** WorkflowRuntime 等组件传的是 `descriptor.Id`（稳定标识符），不是 Name。

### 5.2 Metadata Reference Validation（跨 Registry 引用验证）

**文件**: `framework/test/CrestCreates.Metadata.Tests/DescriptorReferenceValidationTests.cs`

验证 `DescriptorRef` 机制的跨 Registry 完整性。**核心价值：Build 时发现配置错误，而非运行时。**

本质验证的是 `DescriptorRef` 系统 — 同一个机制被 Form→Schema、Workflow→Capability、HumanTask→Form 等所有交叉引用复用。

| # | 测试 | 引用 | 目标 | 预期 |
|---|------|------|------|------|
| R1 | Form_ReferencesSchema_Existing_Ok | Form.SchemaRef="schema_01" | Schema 存在 | Build 成功 ✅ |
| R2 | Form_ReferencesSchema_Missing_BuildFails | Form.SchemaRef="schema_missing" | Schema 不存在 | Build 失败 ❌ |
| R3 | Workflow_ReferencesCapability_Existing_Ok | CapabilityTarget.Id="cap_01" | Capability 存在 | Build 成功 ✅ |
| R4 | Workflow_ReferencesCapability_Missing_BuildFails | CapabilityTarget.Id="cap_missing" | Capability 不存在 | Build 失败 ❌ |
| R5 | HumanTask_ReferencesForm_Existing_Ok | HumanTask.Form="form_01" | Form 存在 | Build 成功 ✅ |
| R6 | HumanTask_ReferencesCapability_Missing_BuildFails | CompletionOutcome.Capability="cap_missing" | Capability 不存在 | Build 失败 ❌ |

### 5.3 Registry 迁移测试

每个迁移后的 Registry 测试项目新增验证：

| Registry | 新增测试 | 验证点 |
|----------|---------|--------|
| Schema | BuildSucceeds / GetById / GetByVersion | State=Built, 正确返回, 精确版本查找 |
| Form | 同上 | 同上 |
| HumanTask | 同上 | 同上 |
| Workflow | 同上 | 同上 |

---

## 6. Id/Name 语义统一

### 6.1 原则

**唯一主链：Id 是 Runtime 阶段的唯一标识符。Name 仅用于人类可读显示。**

### 6.2 重命名

| 位置 | 当前 | 改为 | 说明 |
|------|------|------|------|
| `ICapabilityPipeline.ExecuteAsync` | `string capabilityName` | `string capabilityIdOrName` | 接受 Id-first, Name 仅向后兼容 |
| `CapabilityPipeline.ExecuteAsync` | capName 相关引用 | capIdOrName | 同上 |
| `CapabilityHandlerResolver.Register` | `string capabilityName` | `string capabilityId` | **仅接受 Id** — 禁止传 Name |
| `CapabilityHandlerResolver.Resolve` | `string capabilityName` | `string capabilityId` | **仅接受 Id** — 禁止传 Name |

### 6.3 HandlerResolver 语义明确化

**Runtime 永远不使用 Name 解析 Handler。**

```csharp
// ✅ 唯一正确路径：传 descriptor.Id
handlerResolver.Register(descriptor.Id, handler);
handlerResolver.Resolve(descriptor.Id);
```

Name 仅参与以下场景，**绝不参与 Runtime Dispatch**：
- UI 显示（Audit.CapabilityName）
- 文档 / Designer
- `descriptor.Name` 作为人类可读元数据存储

> 确保未来不会出现 Id/Name 双注册路径。

### 6.4 受影响的测试文件

- `CapabilityPipelineTests.cs` — 参数引用更新
- `CapabilityHandlerResolver` 无单独测试（仅通过 PipelineTests 间接测试）

---

## 7. 空 catch 块

**不处理。** `UnitOfWorkFactory.cs` 的 3 处空 catch 已有注释：

```csharp
catch { /* 忽略注册失败，可能是因为未引用 EfCore 提供者 */ }
catch { /* 忽略注册失败，可能是因为未引用 SqlSugar 提供者 */ }
catch { /* 忽略注册失败，可能是因为未引用 FreeSql 提供者 */ }
```

这是模块化 ORM 注册的合法设计——ORM provider 不存在时不应报错。

---

## 8. 文件清单

### 重写

| 文件 | 说明 |
|------|------|
| `framework/src/CrestCreates.Schema/SchemaRegistry.cs` | RegistryBase 迁移 |
| `framework/src/CrestCreates.HumanTask/HumanTaskRegistry.cs` | RegistryBase 迁移 |
| `framework/src/CrestCreates.Form/FormRegistry.cs` | RegistryBase 迁移 |
| `framework/src/CrestCreates.Workflow/WorkflowRegistry.cs` | RegistryBase 迁移 |
| `framework/tools/CrestCreates.CodeGenerator/SchemaCapabilityGenerator/SchemaCapabilitySourceGenerator.cs` | 生成逻辑更新 |

### 删除

| 文件 | 说明 |
|------|------|
| `framework/src/CrestCreates.Schema/SchemaRegistryProvider.cs` | 废弃（被 DescriptorProviderRegistry 取代） |
| `framework/src/CrestCreates.HumanTask/HumanTaskRegistryProvider.cs` | 废弃（被 DescriptorProviderRegistry 取代） |
| `framework/src/CrestCreates.Form/FormRegistryProvider.cs` | 废弃（被 DescriptorProviderRegistry 取代） |
| `framework/src/CrestCreates.Workflow/WorkflowRegistryProvider.cs` | 废弃（被 DescriptorProviderRegistry 取代） |

### 新增

| 文件 | 说明 |
|------|------|
| `framework/src/CrestCreates.Metadata/DescriptorProviderRegistry.cs` | 集中式 Provider 存储 |
| `framework/src/CrestCreates.Metadata/MetadataBootstrapper.cs` | 统一 BuildAll() 协调 |
| `framework/test/CrestCreates.Capability.Tests/CapabilityEndToEndTests.cs` | 全链路 E2E 测试 (~15 个用例) |
| `framework/test/CrestCreates.Metadata.Tests/DescriptorReferenceValidationTests.cs` | Metadata Reference Validation (6 个用例) |

### 修改

| 文件 | 说明 |
|------|------|
| `framework/src/CrestCreates.Capability/CapabilityPipeline.cs` | 参数 rename + Id 引用更新 |
| `framework/src/CrestCreates.Capability.Abstractions/ICapabilityPipeline.cs` | 参数 rename |
| `framework/src/CrestCreates.Capability/CapabilityHandlerResolver.cs` | 参数 rename |
| `framework/test/CrestCreates.Schema.Tests/SchemaRegistryTests.cs` | 更新 Register→Build |
| `framework/test/CrestCreates.Form.Tests/FormRegistryTests.cs` | 更新 Register→Build |
| `framework/test/CrestCreates.HumanTask.Tests/HumanTaskRegistryTests.cs` | 更新 Register→Build |
| `framework/test/CrestCreates.Workflow.Tests/WorkflowRegistryTests.cs` | 更新 Register→Build |
| `framework/test/CrestCreates.Capability.Tests/CapabilityPipelineTests.cs` | 参数引用更新 |
| `framework/test/CrestCreates.Capability.Tests/CapabilityDispatcherTests.cs` | Handle 参数名变更 |

---

## 9. 成功标准

- [ ] 所有 6 个 Registry 基于 RegistryBase（含 FrozenDictionary 快照 + Build 生命周期）— **Registry 不含 static Collector**
- [ ] `DescriptorProviderRegistry` + `MetadataBootstrapper` 已实现（Provider 存储与 Registry 分离）
- [ ] 4 个 `*RegistryProvider.cs` 已删除，source generator 改为生成 `IDescriptorProvider<T>`
- [ ] Event 统一为 `IDescriptorProvider<GeneratedEventDescriptor>`（不再用 `IEventDescriptorProvider`）
- [ ] 15+ 全链路 E2E 测试通过（含正常/错误/审计/版本/Id-Name 场景）
- [ ] 6 个 Cross-Registry 验证测试通过（Build 时发现配置错误）
- [ ] Id/Name 参数名全部统一：Pipeline 用 `capabilityIdOrName`，Handler Resolver **仅接受 Id**
- [ ] 所有 Registry 测试项目更新（Build 模式）并通过
- [ ] Framework 全量 build + test 通过
- [ ] 文档 `docs/Feature/UnifiedMetadataModel/` 同步更新
