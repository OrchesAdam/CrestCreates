# Tool DTO & JSON Contract — Architecture Design

> **Date:** 2026-06-21 | **Status:** Implemented | **Phase 7c**

## 1. 概述 (Overview)

Phase 7c 在 Agent Control Plane 与外部协议适配器（MCP、HTTP、SignalR）之间引入了一个纯 DTO 序列化边界。所有工具请求/结果类型都通过**密封 record DTO** 暴露，不再暴露领域内部的 `IDescriptor`、`IServiceProvider`、`JsonElement` 或运行时处理程序类型。

核心交付物：
- **32 个 Tool DTO** — 统一的密封 record 类型，代替所有领域类型
- **Source-Generated JSON Contract** — `AgentControlPlaneToolJsonSerializerContext`，AoT 兼容
- **Projection Helpers** — 领域 ←→ DTO 的双向投影，位于 ControlPlane 项目
- **Contract Version** — `AgentControlPlaneContractVersion.Current = "7c.v1"`

### 1.1 目标

| 目标 | 说明 |
|------|------|
| Adapter Readiness | 任何协议适配器（MCP、HTTP、gRPC）只需依赖 `Abstractions` 项目，无需引用 `ControlPlane` 或 `DescriptorDraft` |
| AoT 安全 | 所有 DTO 是密封 record，JSON 序列化使用 Source Generator，无运行时反射 |
| 契约边界 | DTO 不暴露 `IDescriptor`、`IServiceProvider`、`object`/`dynamic`/`JsonElement`、运行时处理程序类型、注册表实例 |
| 元数据级契约 | DTO 携带的是适配器相关的元数据和引用（DescriptorRef），而非全量领域模型 |

---

## 2. 在框架中的位置 (Position in the Framework)

```
Protocol Adapters (MCP / HTTP / SignalR)
         ↓
┌─────────────────────────────────────────────┐
│   Tool DTO Serialization Boundary (Phase 7c) │  ← THIS DOCUMENT
│   ┌───────────────────────────────────────┐  │
│   │ AgentControlPlaneToolJsonSerializer   │  │
│   │ Context (Source Generated)            │  │
│   └───────────────────────────────────────┘  │
│   ┌───────────────────────────────────────┐  │
│   │ Tool Request/Result DTOs              │  │
│   │ (32 sealed records)                   │  │
│   └───────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
         ↓
┌─────────────────────────────────────────────┐
│   Agent Control Plane (Phase 6 / Phase 7)    │
│   ┌───────────────────────────────────────┐  │
│   │ Projection Helpers (domain ↔ DTO)     │  │
│   └───────────────────────────────────────┘  │
│   ┌───────────────────────────────────────┐  │
│   │ DefaultAgentControlPlaneToolService   │  │
│   └───────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
         ↓
┌─────────────────────────────────────────────┐
│   Descriptor Draft Runtime (Phase 7a)        │
│   Metadata / Schema / Event / etc.           │
└─────────────────────────────────────────────┘
```

### 2.1 组件归属

| 项目 | 内容 |
|------|------|
| `CrestCreates.Agent.ControlPlane.Abstractions` | DTO 定义、JSON 序列化上下文、契约版本、工具描述符、工具名称常量 |
| `CrestCreates.Agent.ControlPlane` | Projection Helpers（领域 → DTO / DTO → 领域）、工具服务实现、权限/审计/可见性基础设施 |
| `CrestCreates.Agent.ControlPlane.Tests` | 280 个测试覆盖 DTO 边界约束、语义保持、可见性闭包、契约覆盖、区分器一致性 |

---

## 3. 架构 (Architecture)

### 3.1 组件图

```
┌──────────────────────────────────────────────────────────────────┐
│                  Tool DTO Serialization Boundary                   │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  AgentControlPlaneToolJsonSerializerContext ([JsonCtx])   │    │
│  │  ┌──────────────┐ ┌────────────────┐ ┌──────────────┐    │    │
│  │  │ Root DTOs    │ │ Stable VOs     │ │ Base Types   │    │    │
│  │  │ (32 tools)   │ │ (DescriptorRef,│ │ (AgentTool-  │    │    │
│  │  │              │ │  DescriptorKind,│ │  Result<T>,   │    │    │
│  │  │              │ │  ...)          │ │  Diagnostic)  │    │    │
│  │  └──────────────┘ └────────────────┘ └──────────────┘    │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  AgentControlPlaneToolJsonSerializerOptions               │    │
│  │  CreateDefault() → TypeInfoResolver = Combine(SG Ctx)    │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  Contract Version                                         │    │
│  │  AgentControlPlaneContractVersion.Current = "7c.v1"       │    │
│  └──────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘
                           ↓ (deserialize)
┌──────────────────────────────────────────────────────────────────┐
│  AgentControlPlane  (Projection Layer)                           │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  AgentDescriptorDraftDtoProjection (bidirectional)         │    │
│  │  ├── FromDraft(DescriptorDraft) → AgentDescriptorDraftDto │    │
│  │  ├── ToDomainPayload(AgentDraftPayloadDto) → Payload     │    │
│  │  └── MergeToDomainPayload(existing, dto) → Payload       │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  DescriptorSummaryDtoProjection                           │    │
│  │  └── FromDescriptor(IDescriptor?) → DescriptorSummaryDto   │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  AgentReviewResultDtoProjection                           │    │
│  │  └── Project(source, deniedKinds?) → AgentReviewResultDto │    │
│  └──────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘
```

### 3.2 数据流

Protocol Adapter → JSON deserialize → `AgentToolResult<T>` DTO
    → `DefaultAgentControlPlaneToolService` (validates, authorizes, audits)
    → Domain types (via projection helpers)
    → Domain execution (DescriptorDraft Store, Metadata Registry, etc.)
    → Domain result → Projection helper → DTO → JSON serialize → Protocol Adapter

---

## 4. 关键组件 (Key Components)

| 组件 | 所在项目 | 职责 | 状态 |
|------|---------|------|------|
| `AgentDraftPayloadDto` | Abstractions/ToolDtos | 嵌套 one-of 负载 DTO，6 个子 record | **Implemented** |
| `AgentCapabilityDraftPayloadDto` | Abstractions/ToolDtos | Capability 元数据 DTO | **Implemented** |
| `AgentWorkflowDraftPayloadDto` | Abstractions/ToolDtos | Workflow 元数据 DTO | **Implemented** |
| `AgentHumanTaskDraftPayloadDto` | Abstractions/ToolDtos | HumanTask 元数据 DTO | **Implemented** |
| `AgentFormDraftPayloadDto` | Abstractions/ToolDtos | Form 元数据 DTO | **Implemented** |
| `AgentEventDraftPayloadDto` | Abstractions/ToolDtos | Event 元数据 DTO | **Implemented** |
| `AgentSchemaDraftPayloadDto` | Abstractions/ToolDtos | Schema 元数据 DTO | **Implemented** |
| `AgentDescriptorDraftDto` | Abstractions/ToolDtos | DescriptorDraft 投影 DTO，替换 DraftComparisonResult 中的 DescriptorDraft | **Implemented** |
| `DescriptorSummaryDto` | Abstractions/ToolDtos | IDescriptor 摘要 DTO，替换 DraftComparisonResult 中的 IDescriptor? | **Implemented** |
| `AgentReviewResultDto` | Abstractions/ToolDtos | ReviewResult 投影 DTO，含 6 个摘要子 DTO | **Implemented** |
| `AgentProposedInventorySummaryDto` | Abstractions/ToolDtos | 提议清单摘要 | **Implemented** |
| `AgentTopologySummaryDto` | Abstractions/ToolDtos | 拓扑摘要 | **Implemented** |
| `AgentMaterializationSummaryDto` | Abstractions/ToolDtos | 物化摘要 | **Implemented** |
| `AgentImpactAnalysisSummaryDto` | Abstractions/ToolDtos | 影响分析摘要 | **Implemented** |
| `AgentCompatibilitySummaryDto` | Abstractions/ToolDtos | 兼容性摘要 | **Implemented** |
| `AgentGovernanceSummaryDto` | Abstractions/ToolDtos | 治理决策摘要 | **Implemented** |
| `CreateDescriptorDraftRequest` | Abstractions | 创建草稿请求（含 `AgentDraftPayloadDto`） | **Implemented** |
| `UpdateDescriptorDraftRequest` | Abstractions | 更新草稿请求（含 `AgentDraftPayloadDto?`） | **Implemented** |
| `AgentControlPlaneToolJsonSerializerContext` | Abstractions/Json | Source-generated JSON 序列化上下文，注册所有 Root DTO + 稳定值对象 | **Implemented** |
| `AgentControlPlaneToolJsonSerializerOptions` | Abstractions/Json | `CreateDefault()` 工厂方法 | **Implemented** |
| `AgentControlPlaneContractVersion` | Abstractions/Json | `Current = "7c.v1"` | **Implemented** |
| `AgentToolDescriptor.ContractVersion` | Abstractions | 工具描述符上的契约版本字段 | **Implemented** |
| `AgentToolName` | Abstractions | 32 个工具名称常量 | **Implemented** |
| `AgentToolCategory` | Abstractions | 7 个工具分类枚举 | **Implemented** |
| `AgentToolResult<T>` | Abstractions | 泛型工具结果包装（Success/Denied/Failed/InvalidRequest/NotFound） | **Implemented** |
| `DescriptorSummaryDtoProjection` | ControlPlane/Projections | `FromDescriptor(IDescriptor)` | **Implemented** |
| `AgentDescriptorDraftDtoProjection` | ControlPlane/Projections | `FromDraft` / `ToDomainPayload` / `MergeToDomainPayload` | **Implemented** |
| `AgentReviewResultDtoProjection` | ControlPlane/Projections | `Project`（含可见性过滤） | **Implemented** |
| `StaticAgentToolManifestProvider` | ControlPlane | 静态工具描述符声明（32 个工具） | **Implemented** |

### 4.1 工具覆盖 (32 Tools)

| Wave | 分类 | 工具数 | 范围 |
|------|------|--------|------|
| Wave 1 | Context / Read | 6 | BuildMetadataContextPack, BuildRuntimeScenarioContextPack, GetDescriptorByRef, SearchDescriptors, ListDescriptorRelationships, GetTopologySummary |
| Wave 2 | Draft | 6 | CreateDescriptorDraft, UpdateDescriptorDraft, GetDescriptorDraft, ListDescriptorDrafts, CancelDescriptorDraft, CompareDescriptorDraft |
| Wave 3 | Review | 5 | ValidateDescriptorDraft, ReviewDescriptorDraft, GetDraftReviewResult, ListDraftReviewResults, ExplainDiagnostics |
| Wave 4 | Fix Proposal | 4 | SuggestDescriptorDraftFixes, GetFixProposal, ListFixProposals, ApplyFixProposalToDraft |
| Wave 5 | Package Preview | 4 | PreviewDescriptorPackage, BuildPackageEvidencePreview, BuildActivationReadinessPreview, GetPackagePreview |
| Wave 6 | Activation Handoff | 3 | SubmitActivationRequest, GetActivationRequestStatus, CancelActivationRequest |
| Wave 7 | Manifest | 2 | ListAgentTools, GetAgentToolDescriptor |

---

## 5. DTO 设计规则 (DTO Design Rules)

### 5.1 边界规则

DTO 必须遵守以下约束，确保任何协议适配器都能安全使用：

| 规则 | 说明 |
|------|------|
| ❌ 不暴露 `IDescriptor` | 使用 `DescriptorSummaryDto` 或 `DescriptorRef` 代替 |
| ❌ 不暴露 `IServiceProvider` | DTO 是纯数据，不承载 DI 容器 |
| ❌ 不暴露 `object`/`dynamic`/`JsonElement` | 所有字段类型是确定的密封 record 或基元类型 |
| ❌ 不暴露运行时处理程序类型 | 如 `IDescriptorHandler`、`IWorkflowEngine` |
| ❌ 不暴露注册表实例 | 如 `IDescriptorRegistry`、`IDescriptorDraftStore` |
| ✅ 使用密封 record | 所有 DTO 是 `sealed record`，支持值语义 |
| ✅ 使用 `required` 属性 | 关键字段使用 `required` 确保构造时赋值 |
| ✅ 可选字段可为 null | 非关键字段使用 `?` 可空标记 |
| ✅ AoT 友好 | 无反射、无动态代码路径 |

### 5.2 类型映射规则

| 领域类型 | DTO 类型 |
|---------|---------|
| `IDescriptor?` | `DescriptorSummaryDto?` |
| `DescriptorDraft` | `AgentDescriptorDraftDto` |
| `DescriptorDraftPayload` (抽象) | `AgentDraftPayloadDto` (one-of) |
| `CapabilityDescriptorDraftPayload` | `AgentCapabilityDraftPayloadDto` |
| `WorkflowDescriptorDraftPayload` | `AgentWorkflowDraftPayloadDto` |
| `HumanTaskDescriptorDraftPayload` | `AgentHumanTaskDraftPayloadDto` |
| `FormDescriptorDraftPayload` | `AgentFormDraftPayloadDto` |
| `EventDescriptorDraftPayload` | `AgentEventDraftPayloadDto` |
| `SchemaDescriptorDraftPayload` | `AgentSchemaDraftPayloadDto` |
| `DescriptorDraftReviewResult` | `AgentReviewResultDto` |
| `CapabilityDescriptor` (引用) | `DescriptorRef?` |
| `VersionedDescriptorRef<T>` | `DescriptorRef?` |
| `TimeSpan?` | `string?` (因为 JSON 序列化兼容性) |

---

## 6. AgentDraftPayloadDto 设计 (Nested One-Of Pattern)

### 6.1 结构

```csharp
public sealed record AgentDraftPayloadDto
{
    public required DescriptorKind Discriminator { get; init; }
    public AgentCapabilityDraftPayloadDto? Capability { get; init; }
    public AgentWorkflowDraftPayloadDto? Workflow { get; init; }
    public AgentHumanTaskDraftPayloadDto? HumanTask { get; init; }
    public AgentFormDraftPayloadDto? Form { get; init; }
    public AgentEventDraftPayloadDto? Event { get; init; }
    public AgentSchemaDraftPayloadDto? Schema { get; init; }
}
```

### 6.2 不变式

**区分器必须匹配唯一非空的子 record**。如果 `Discriminator` 与填充的子 record 不一致，则视为无效请求。投影层在 `ToDomainPayload` 和 `MergeToDomainPayload` 中执行验证：

```csharp
// ValidateDiscriminator 内部的逻辑（每个 Kind 独立检查）
DescriptorKind.Capability → hasCap && !hasWorkflow && !hasHumanTask && !hasForm && !hasEvent && !hasSchema
DescriptorKind.Workflow  → hasWorkflow && !hasCap && !hasHumanTask && !hasForm && !hasEvent && !hasSchema
// ... 其余 Kind 同理
```

违反不变式时抛出 `InvalidOperationException`，消息包含区分器信息。在工具服务层面，转化为 `AgentToolResult<T>.InvalidRequest` 并附带 `KindDiscriminatorMismatch` 诊断码。

### 6.3 子 record 字段（元数据级契约）

每个子 record 承载的是适配器相关的元数据和引用，**不是全量领域模型**。子结构（WorkflowStep、FormFieldDescriptor、SchemaFieldDescriptor、ValidationRules、Outcomes 等）不在 7c.v1 契约范围内。

| Sub-record | 关键字段 |
|-----------|---------|
| `AgentCapabilityDraftPayloadDto` | DescriptorRef, Name, DisplayName, State, InputSchema/OutputSchema (DescriptorRef?), CapabilityKind(string?), Categories(string[]), Produces/Consumes(DescriptorRef[]), SemanticTags(string[]), Permissions(string[]), RiskLevel(string?), ContractHash, DefinitionHash, Version(int?) |
| `AgentWorkflowDraftPayloadDto` | DescriptorRef, Name, DisplayName, State, VariableSchema(DescriptorRef?), ContractHash, DefinitionHash, Version(int?) |
| `AgentHumanTaskDraftPayloadDto` | DescriptorRef, Name, DisplayName, State, AssignmentStrategy(string?), InputSchema/OutputSchema/Interaction(DescriptorRef?), Timeout(string?), ContractHash, DefinitionHash, Version(int?) |
| `AgentFormDraftPayloadDto` | DescriptorRef, Name, DisplayName, State, FormSchema(DescriptorRef?), ContractHash, DefinitionHash, Version(int?) |
| `AgentEventDraftPayloadDto` | DescriptorRef, Name, DisplayName, State, EventKind(string?), EventType(string?), PayloadSchema(DescriptorRef?), Importance(string?), ChangeKind(string?), ContractHash, DefinitionHash, Version(int?) |
| `AgentSchemaDraftPayloadDto` | DescriptorRef, Name, DisplayName, State, SchemaKind(string?), ContractHash, DefinitionHash, Version(int?) |

> 所有枚举值在 DTO 层使用 `string?` 表示，避免协议适配器依赖领域枚举程序集。投影层在 domain ↔ DTO 转换时进行 string ↔ enum 转换。

---

## 7. 投影策略 (Projection Strategy)

### 7.1 设计原则

- **无统一映射器接口** — 不引入 `IMapper<TSource, TDest>`。投影是静态方法，在真实的边界交叉点书写。
- **投影在 ControlPlane 项目**，不在 Abstractions — 因为投影依赖领域类型（CapabilityDescriptor、WorkflowDescriptor 等），这些在 Abstractions 不可见。
- **C+ 方法** — 既有代码生成（DTO 定义在 Abstractions）又有静态映射（投影在 ControlPlane），但不存在"双轨"运行时反射回退。

### 7.2 FromDraft（领域 → DTO）

```csharp
// DescriptorDraft → AgentDescriptorDraftDto
public static AgentDescriptorDraftDto FromDraft(DescriptorDraft draft)

// IDescriptor → DescriptorSummaryDto
public static DescriptorSummaryDto? FromDescriptor(IDescriptor? descriptor)

// DescriptorDraftReviewResult → AgentReviewResultDto（含可见性过滤）
public static AgentReviewResultDto Project(DescriptorDraftReviewResult source, IReadOnlySet<DescriptorKind>? deniedKinds)
```

MapPayload 内部使用 `IDescriptor` 接口（非具体类型转换），通过 `kind switch` 分发到具体子映射器，确保对每种 DescriptorKind 都有确定性的映射路径。

### 7.3 ToDomainPayload（DTO → 领域，用于 Create）

```csharp
// AgentDraftPayloadDto → DescriptorDraftPayload（全新构造）
public static DescriptorDraftPayload ToDomainPayload(AgentDraftPayloadDto dto)
```

验证区分器，然后根据 `Discriminator` 创建对应的领域 payload。DTO 中的 `DescriptorRef` 映射为领域实体 ID 和 `VersionedDescriptorRef<T>`。

### 7.4 MergeToDomainPayload（DTO → 领域，用于 Update）

```csharp
// AgentDraftPayloadDto + 现有 payload → 合并后的 DescriptorDraftPayload
public static DescriptorDraftPayload MergeToDomainPayload(
    DescriptorDraftPayload existing,
    AgentDraftPayloadDto dto)
```

**合并语义**：DTO 只覆盖元数据级字段（Name、State、Schema 引用、ContractHash 等）。DTO 中未表示的领域子结构（Steps、Fields、ValidationRules、Outcomes、Permissions 等）从现有 payload 原样保留。

关键字段的空值处理：
- `dto.Name ?? existing.Name` — DTO 为 null 时保留现有值
- `dto.ContractHash ?? existing.ContractHash`
- `dto.InputSchema is { } ischema ? new VersionedDescriptorRef(...) : existing.InputSchema` — null 保留，非 null 替换

---

## 8. 契约版本化 (Contract Versioning)

### 8.1 版本标识

```csharp
public static class AgentControlPlaneContractVersion
{
    public const string Current = "7c.v1";
}
```

### 8.2 版本承载

每个 `AgentToolDescriptor` 实例携带 `ContractVersion` 字段：

```csharp
public sealed record AgentToolDescriptor
{
    // ... 其他字段
    public string ContractVersion { get; init; } = AgentControlPlaneContractVersion.Current;
}
```

适配器可以通过 `ListAgentTools` 或 `GetAgentToolDescriptor` 查询契约版本，决定序列化和行为期望。

### 8.3 契约演进策略

| 场景 | 版本升级 | 说明 |
|------|---------|------|
| 兼容新增（添加可选字段） | 次要升级 (`7c.v2`) | 现有适配器无感知 |
| 兼容变更（添加新 DTO） | 次要升级 (`7c.v2`) | 需要新的 JSON 上下文注册 |
| 破坏性变更（移除/重命名字段） | 主要升级 (`7d.v1`) | 适配器必须同步升级 |
| 引入全量子结构 | 主要升级（`7d.v1` 或更高） | Sub-record 字段扩展 |

---

## 9. 边界规则 (Boundary Rules)

| 规则 | 理由 |
|------|------|
| DTO 不暴露 `IDescriptor` 接口 | 适配器不应依赖领域抽象 |
| DTO 是密封 record | 支持值语义、AoT 安全、JSON 友好 |
| 所有枚举在 DTO 层是 `string?` | 避免适配器引用领域枚举程序集 |
| 子结构的 Steps/Fields/ValidationRules 不在 7c.v1 中 | 这是元数据级契约，全量子结构在后续契约扩展 |
| 投影在 ControlPlane，不在 Abstractions | 投影依赖领域类型，Abstractions 是零依赖层 |
| 区分器必须匹配子 record | 防止歧义：一个 payload 只能有一个非空子 record |
| Update 使用合并语义 | DTO 不表示的内容必须从领域保留 |
| 静态投影，无反射 | 每个 DescriptorKind 有确定性 switch 分支 |
| JSON Context 注册所有 Root DTO | 确保 Source Generator 覆盖完整的工具表面 |
| ContractVersion 在每个工具描述符中 | 适配器可以在运行时检查兼容性 |

---

## 10. 未来阶段 (Future Phases)

| Phase | 能力 | 状态 |
|-------|------|------|
| **7c** | Tool DTO & JSON Contract（当前） | **Implemented** |
| 7b | LLM Bootstrap（Prompt 模板、LLM Provider、Draft Builder） | Future |
| 7d | MCP Projection（将 Phase 7c DTO 投影到 Model Context Protocol） | Future |
| 7e | Activation Workflow（已审查的草稿 → 运行时激活） | Future |
| 7a | Descriptor Draft Runtime（存储、验证、物化、审查） | Implemented |
| 7f | Continuous Improvement Loop（运行时反馈 → prompt 优化） | Future |

### 10.1 Phase 7b — LLM Bootstrap Plane

Phase 7b 将引入 LLM 驱动的描述符草稿生成。核心组件已设计但尚未实现：

- `PromptTemplate` — 结构化提示模板，带描述符上下文注入
- `ILLMProvider` — 可插拔的 LLM 后端抽象（OpenAI、Anthropic、本地模型）
- `PromptTemplateRegistry` — 按描述符类型存储和解析提示模板
- `DescriptorDraftBuilder` — 将 LLM 结构化输出转换为 `DescriptorDraft` 实例

LLM 生成的草稿将经过与人工草稿相同的 `IDescriptorDraftValidator` 验证管道，确保一致性。
