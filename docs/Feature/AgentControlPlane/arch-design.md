# Tool DTO & JSON Contract — Architecture Design

> **Date:** 2026-06-22 | **Status:** Implemented | **Phase 7c (#41 DTO Design + #42 Source Generator)**

## 1. 概述 (Overview)

Phase 7c 在 Agent Control Plane 与外部协议适配器（MCP、HTTP、SignalR）之间引入了一个纯 DTO 序列化边界。所有工具请求/结果类型都通过**密封 record DTO** 暴露，不再暴露领域内部的 `IDescriptor`、`IServiceProvider`、`JsonElement` 或运行时处理程序类型。

核心交付物：
- **32 个 Tool DTO** — 统一的密封 record 类型，代替所有领域类型
- **Source-Generated JSON Contract** — `AgentControlPlaneToolJsonSerializerContext`，AoT 兼容
- **Source-Generated Payload DTOs (New in #42)** — `AgentDraftPayloadDto` 和 6 个子 record 由 `AgentDraftContractGenerator` 在 `CrestCreates.CodeGenerator` 中生成
- **Source-Generated Patch DTOs (New in #42)** — `AgentDraftPayloadPatchDto` 和 `Agent{Kind}DraftChangedField` 枚举用于 Update 操作的字段级合并
- **Source-Generated Projection (New in #42)** — `AgentDraftPayloadProjection` 提供 `Create`、`FromDomain`、`Merge` 方法，替代手写 payload 投影
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
| `CrestCreates.Agent.DraftContracts` | Spec 文件（`[AgentDraftContractSpec]`、`[AgentDraftField]` 等）、引用 `CrestCreates.CodeGenerator` → 生成 payload DTO、patch DTO、changed-field 枚举、投影类、manifest |
| `CrestCreates.Agent.ControlPlane.Abstractions` | Tool DTO（请求/结果类型）、JSON 序列化上下文、契约版本、工具描述符、工具名称常量；payload DTO 类型通过 global using 别名映射到 DraftContracts 生成类型 |
| `CrestCreates.Agent.ControlPlane` | Wrapper 投影（`AgentDescriptorDraftDtoProjection`）、`AgentDraftPayloadProjection` 的调用者、工具服务实现、权限/审计/可见性基础设施 |
| `CrestCreates.Agent.ControlPlane.Tests` | 298 个 ControlPlane 测试 + 34 个 DraftContracts 测试 + 15 个 Generator 测试 + 7 个 Boundary 测试 = **354 个测试** |

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
│  │  AgentDescriptorDraftDtoProjection (wrapper)               │    │
│  │  ├── FromDraft(DescriptorDraft) → AgentDescriptorDraftDto │    │
│  │  └── delegates payload ops to AgentDraftPayloadProjection │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  AgentDraftPayloadProjection (source-generated) [New!]    │    │
│  │  ├── FromDomain(payload) → AgentDraftPayloadDto          │    │
│  │  ├── Create(dto) → DescriptorDraftPayload                │    │
│  │  ├── Merge(patch, existing) → ContractResult<Payload>    │    │
│  │  └── TryValidatePayload(dto) → (bool, Error?)            │    │
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
                           ↑ (references generated types)
┌──────────────────────────────────────────────────────────────────┐
│  CrestCreates.Agent.DraftContracts  (Source-Generated Types)      │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  Spec Files (attributes on descriptor types)               │    │
│  │  └── [AgentDraftContractSpec], [AgentDraftField], ...      │    │
│  └──────────────────────────────────────────────────────────┘    │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  AgentDraftContractGenerator (in CrestCreates.CodeGenerator)│    │
│  │  └── Reads spec files → generates:                        │    │
│  │      ├── AgentDraftPayloadDto (+ 6 sub-records)           │    │
│  │      ├── AgentDraftPayloadPatchDto (+ 6 patch branches)   │    │
│  │      ├── Agent{Kind}DraftChangedField ([Flags] enums)     │    │
│  │      ├── AgentDraftPayloadProjection                      │    │
│  │      └── AgentDraftContractManifest                       │    │
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
| `AgentDraftPayloadDto` | DraftContracts/Dto (Generated) | 嵌套 one-of 负载 DTO，6 个子 record，由 `AgentDraftContractGenerator` 生成 | **Implemented (#42)** |
| `AgentCapabilityDraftPayloadDto` | DraftContracts/Dto (Generated) | Capability 元数据 DTO，由 Spec 文件驱动生成 | **Implemented (#42)** |
| `AgentWorkflowDraftPayloadDto` | DraftContracts/Dto (Generated) | Workflow 元数据 DTO，由 Spec 文件驱动生成 | **Implemented (#42)** |
| `AgentHumanTaskDraftPayloadDto` | DraftContracts/Dto (Generated) | HumanTask 元数据 DTO，由 Spec 文件驱动生成 | **Implemented (#42)** |
| `AgentFormDraftPayloadDto` | DraftContracts/Dto (Generated) | Form 元数据 DTO，由 Spec 文件驱动生成 | **Implemented (#42)** |
| `AgentEventDraftPayloadDto` | DraftContracts/Dto (Generated) | Event 元数据 DTO，由 Spec 文件驱动生成 | **Implemented (#42)** |
| `AgentSchemaDraftPayloadDto` | DraftContracts/Dto (Generated) | Schema 元数据 DTO，由 Spec 文件驱动生成 | **Implemented (#42)** |
| `AgentDraftPayloadPatchDto` | DraftContracts/Dto (Generated) | Update 用 patch DTO，6 个分支全部 nullable，附带 ChangedFields | **Implemented (#42)** |
| `Agent{Kind}DraftChangedField` | DraftContracts/Dto (Generated) | `[Flags]` 枚举，指定哪些字段在 patch 中被修改 | **Implemented (#42)** |
| `AgentDraftPayloadProjection` | DraftContracts/Projection (Generated) | payload 操作投影：`FromDomain`、`Create`、`Merge`、`TryValidatePayload` | **Implemented (#42)** |
| `AgentDraftContractManifest` | DraftContracts/Manifest (Generated) | `SupportedKinds`、`ContractVersion`("7c.v1")、per-kind 字段元数据 | **Implemented (#42)** |
| `AgentDraftContractSpec` / `AgentDraftField` / etc. | DraftContracts/Specs | Spec 特性属性，声明 descriptor 属性在契约中的分类 | **Implemented (#42)** |
| `CapabilityContractSpec` / `WorkflowContractSpec` / etc. | DraftContracts/Specs | 每种 descriptor kind 的 spec 文件，使用 spec 属性描述字段 | **Implemented (#42)** |
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
| `UpdateDescriptorDraftRequest` | Abstractions | 更新草稿请求（含 `AgentDraftPayloadPatchDto?`） | **Implemented (#42)** |
| `AgentControlPlaneToolJsonSerializerContext` | Abstractions/Json | Source-generated JSON 序列化上下文，注册所有 Root DTO + 稳定值对象 | **Implemented** |
| `AgentControlPlaneToolJsonSerializerOptions` | Abstractions/Json | `CreateDefault()` 工厂方法 | **Implemented** |
| `AgentControlPlaneContractVersion` | Abstractions/Json | `Current = "7c.v1"` | **Implemented** |
| `AgentToolDescriptor.ContractVersion` | Abstractions | 工具描述符上的契约版本字段 | **Implemented** |
| `AgentToolName` | Abstractions | 32 个工具名称常量 | **Implemented** |
| `AgentToolCategory` | Abstractions | 7 个工具分类枚举 | **Implemented** |
| `AgentToolResult<T>` | Abstractions | 泛型工具结果包装（Success/Denied/Failed/InvalidRequest/NotFound） | **Implemented** |
| `DescriptorSummaryDtoProjection` | ControlPlane/Projections | `FromDescriptor(IDescriptor)` | **Implemented** |
| `AgentDescriptorDraftDtoProjection` | ControlPlane/Projections | `FromDraft` / 委托 `AgentDraftPayloadProjection` 处理 payload 操作 | **Implemented** |
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

> **注意 (#42)**：`AgentDraftPayloadDto` 及其 6 个子 record 现在是 source-generated 类型，由 `CrestCreates.CodeGenerator` 中的 `AgentDraftContractGenerator` 生成。生成器读取 `DraftContracts/Specs/` 下的 spec 文件（如 `CapabilityContractSpec.cs`），这些文件通过 `[AgentDraftField]`、`[AgentDraftReference]`、`[AgentDraftPreserve]` 等特性声明每个 descriptor 类型中属于契约的字段。

### 6.1 结构

生成的 `AgentDraftPayloadDto` 结构与手写时一致：

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

类型通过 `CrestCreates.Agent.ControlPlane.Abstractions` 中的 global using 别名暴露给消费者，因此使用 `new AgentDraftPayloadDto()` 的代码不需要修改。

### 6.2 不变式

**区分器必须匹配唯一非空的子 record**。如果 `Discriminator` 与填充的子 record 不一致，则视为无效请求。投影层通过 `TryValidatePayload` 执行验证：

```csharp
// ValidateDiscriminator 内部的逻辑（每个 Kind 独立检查）
DescriptorKind.Capability → hasCap && !hasWorkflow && !hasHumanTask && !hasForm && !hasEvent && !hasSchema
DescriptorKind.Workflow  → hasWorkflow && !hasCap && !hasHumanTask && !hasForm && !hasEvent && !hasSchema
// ... 其余 Kind 同理
```

违反不变式时 `TryValidatePayload` 返回 `(false, AgentDraftContractError)` 并附带 `ADPC002 (DiscriminatorMismatch)` 错误码。在工具服务层面，转化为 `AgentToolResult<T>.InvalidRequest` 并附带 `KindDiscriminatorMismatch` 诊断码。

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

### 6.4 AgentDraftPayloadPatchDto — Update 用 Patch DTO

> **New in #42**：Update 操作不再使用 `AgentDraftPayloadDto`，改用 `AgentDraftPayloadPatchDto`。

Patch DTO 与 create DTO 结构相同，但所有字段均为 nullable，并且在构造时需要同时指定 `ChangedFields`（使用 `[Flags]` 枚举）：

```csharp
// 生成的 Patch DTO 结构（每个描述符 kind 均有独立分支）
public sealed record AgentDraftPayloadPatchDto
{
    public required DescriptorKind Discriminator { get; init; }
    public required IReadOnlySet<Enum> ChangedFields { get; init; }
    public AgentCapabilityDraftPayloadPatchDto? Capability { get; init; }
    public AgentWorkflowDraftPayloadPatchDto? Workflow { get; init; }
    public AgentHumanTaskDraftPayloadPatchDto? HumanTask { get; init; }
    public AgentFormDraftPayloadPatchDto? Form { get; init; }
    public AgentEventDraftPayloadPatchDto? Event { get; init; }
    public AgentSchemaDraftPayloadPatchDto? Schema { get; init; }
}
```

每个分支 DTO 的字段与 create DTO 同名但全部可空。例如 `AgentCapabilityDraftPayloadPatchDto` 中 `Name` 为 `string?`（create DTO 中为 `required string`）。

#### Patch 合并语义

投影层 `Merge` 方法执行以下逻辑：

| 场景 | 行为 |
|------|------|
| 字段在 `ChangedFields` 中 + DTO 值非 null | 更新为该值 |
| 字段在 `ChangedFields` 中 + DTO 值为 null（可空字段） | 清除为 null |
| 字段在 `ChangedFields` 中 + DTO 值为 null（非可空字段） | 返回 `ADPC007` 错误 |
| 字段**不在** `ChangedFields` 中 | 保留现有值 |
| `ChangedFields` 包含未知 bit | 返回 `ADPC005` 错误 |
| `ChangedFields` 为空 | 返回 `ADPC004` 错误 |
| Preserve 字段（在 spec 中标记为 `[AgentDraftPreserve]`） | 忽略 `ChangedFields`，始终从现有值复制 |

### 6.5 ChangedField 枚举

每个描述符 kind 有独立 `[Flags]` 枚举：

| 枚举 | 字段 |
|------|------|
| `AgentCapabilityDraftChangedField` | `Name`, `State`, `ContractHash`, `DefinitionHash`, `Version`, `CapabilityKind`, `InputSchema`, `OutputSchema`, `Categories`, `Produces`, `Consumes`, `SemanticTags`, `Permissions`, `RiskLevel` |
| `AgentWorkflowDraftChangedField` | `Name`, `State`, `ContractHash`, `DefinitionHash`, `Version`, `VariableSchema` |
| `AgentHumanTaskDraftChangedField` | `Name`, `State`, `ContractHash`, `DefinitionHash`, `Version`, `AssignmentStrategy`, `InputSchema`, `OutputSchema`, `Interaction`, `Timeout` |
| `AgentFormDraftChangedField` | `Name`, `State`, `ContractHash`, `DefinitionHash`, `Version`, `FormSchema` |
| `AgentEventDraftChangedField` | `Name`, `State`, `ContractHash`, `DefinitionHash`, `Version`, `EventKind`, `EventType`, `PayloadSchema`, `Importance`, `ChangeKind` |
| `AgentSchemaDraftChangedField` | `Name`, `State`, `ContractHash`, `DefinitionHash`, `Version`, `SchemaKind` |

所有 kind 共享的公共字段：`Name`, `State`, `ContractHash`, `DefinitionHash`, `Version`。Kind 特有字段如 `CapabilityKind`、`InputSchema`、`Steps`、`PayloadSchema` 等仅出现在对应枚举中。

---

## 7. 投影策略 (Projection Strategy)

### 7.1 设计原则

- **无统一映射器接口** — 不引入 `IMapper<TSource, TDest>`。投影是静态方法，在真实的边界交叉点书写。
- **Payload 投影在 DraftContracts (Generated)** — payload DTO 的投影由 `AgentDraftContractGenerator` 在编译期生成，输出为 `AgentDraftPayloadProjection`。这消除了 payload DTO 投影的手写维护。
- **Wrapper 投影在 ControlPlane** — `AgentDescriptorDraftDtoProjection`（包装 DTO 投影）仍位于 ControlPlane 项目，因为其依赖 `DescriptorDraft` 等领域类型。但它将 payload 操作委托给生成的 `AgentDraftPayloadProjection`。
- **无运行时反射** —所有投影路径在编译期完全确定。

### 7.2 FromDraft（领域 → DTO）

领域 → DTO 的投影仍然由 ControlPlane 中的 hand-written projection 处理：

```csharp
// DescriptorDraft → AgentDescriptorDraftDto
public static AgentDescriptorDraftDto FromDraft(DescriptorDraft draft)

// IDescriptor → DescriptorSummaryDto
public static DescriptorSummaryDto? FromDescriptor(IDescriptor? descriptor)

// DescriptorDraftReviewResult → AgentReviewResultDto（含可见性过滤）
public static AgentReviewResultDto Project(DescriptorDraftReviewResult source, IReadOnlySet<DescriptorKind>? deniedKinds)
```

Payload 子映射通过 `kind switch` 分发，但现在 DTO 是 source-generated 类型。`FromDraft` 内部调用 `AgentDraftPayloadProjection.FromDomain(payload)` 将领域 payload 转换为 DTO。

### 7.3 Create — DTO → 领域负载（用于 Create）

生成的 `AgentDraftPayloadProjection` 提供 Create 方法，替代原来的 `ToDomainPayload`：

```csharp
// AgentDraftPayloadDto → DescriptorDraftPayload（全新构造）
public static DescriptorDraftPayload Create(AgentDraftPayloadDto dto)
```

验证区分器，然后根据 `Discriminator` 创建对应的领域 payload。DTO 中的 `DescriptorRef` 映射为领域实体 ID 和 `VersionedDescriptorRef<T>`。如果区分器不匹配，返回 `ADPC002` 错误（而非抛异常）。

### 7.4 Merge — DTO → 领域负载（用于 Update）

生成的 `AgentDraftPayloadProjection` 提供 Merge 方法，替代原来的 `MergeToDomainPayload`：

```csharp
// AgentDraftPayloadPatchDto + 现有 payload → 合并结果
public static AgentDraftContractResult<DescriptorDraftPayload> Merge(
    DescriptorDraftPayload existing,
    AgentDraftPayloadPatchDto patch)
```

**返回类型 `AgentDraftContractResult<T>`** 是一个 sealed record，包含：
- `T? Value` — 合并成功时的领域 payload
- `AgentDraftContractError? Error` — 合并失败时的错误信息（含错误码）

**合并语义**：只有 `ChangedFields` 中标记的字段才被更新。领域子结构（Steps、Fields、ValidationRules、Outcomes、Permissions 等）从现有 payload 原样保留。

| 场景 | 行为 |
|------|------|
| 字段在 `ChangedFields` 中 + DTO 值非 null | 更新为该值 |
| 字段在 `ChangedFields` 中 + DTO 值为 null（可空字段） | 清除为 null |
| 字段在 `ChangedFields` 中 + DTO 值为 null（非可空字段） | 返回 `ADPC007` (NonNullableFieldNull) |
| 字段不在 `ChangedFields` 中 | 现有值保持不变 |
| `ChangedFields` 包含未知 bit | 返回 `ADPC005` (UnknownChangedField) |
| `ChangedFields` 为空 | 返回 `ADPC004` (EmptyChangedFields) |
| Preserve 字段 | 始终从现有值复制，忽略 `ChangedFields` |

### 7.5 TryValidatePayload — 区分器验证

```csharp
// 验证 payload 区分器匹配且无歧义
public static (bool IsValid, AgentDraftContractError? Error) TryValidatePayload(
    AgentDraftPayloadDto dto)
```

与 Create 不同，此方法仅执行验证而不创建领域对象，适用于需要在创建前先验证 payload 的场景。

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
| Payload DTO 由 Source Generator 生成 | 通过 Spec 文件声明式定义，消除手写 DTO 与领域模型的不一致 |
| Patch DTO 在 Update 中使用 | `ChangedFields` 枚举标记受影响的字段，非标记字段保留现有值 |
| 投影分层：payload 投影生成的，wrapper 投影手写 | Generated `AgentDraftPayloadProjection` 在 DraftContracts；手写 `AgentDescriptorDraftDtoProjection` 在 ControlPlane |
| 区分器必须匹配子 record | 防止歧义：一个 payload 只能有一个非空子 record |
| Update 使用字段级合并语义（PatchDto） | 只有 `ChangedFields` 中标记的字段被更新，其他保留 |
| Preserve 字段始终从现有值复制 | 标记为 `[AgentDraftPreserve]` 的字段不受 `ChangedFields` 影响 |
| 静态投影，无反射 | 每个 DescriptorKind 有确定性 switch 分支 |
| JSON Context 注册所有 Root DTO | 确保 Source Generator 覆盖完整的工具表面 |
| ContractVersion 在每个工具描述符中 | 适配器可以在运行时检查兼容性 |

---

## 10. 未来阶段 (Future Phases)

| Phase | 能力 | 状态 |
|-------|------|------|
| **7c** | Tool DTO & JSON Contract + AgentDraftContract Generator | **Implemented (#41 DTO Design + #42 Source Generator)** |
| 7c-sub | #41: Tool DTO, JSON Context, Projection 基础 | Implemented |
| 7c-sub | #42: AgentDraftContract Generator, Patch DTO, ChangedField enums, Projection 生成 | Implemented |
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
