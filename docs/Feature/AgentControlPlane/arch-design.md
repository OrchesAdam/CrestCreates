# Tool DTO, JSON Contract & Review Report — Architecture Design

> **Date:** 2026-07-02 | **Status:** Implemented | **Phase 7c (#41 DTO Design + #42 Source Generator) + Phase 7d (#16 Review Report & Fix Proposal) + Phase 7e (#17 Safe Activation Workflow) + Phase 7e.1: Canonical Evidence Hashing + Phase 7e+ (#43 Agent Memory & Context Compression Runtime) + Phase 7f (#32 AI-assisted Descriptor Authoring Golden Scenario)**

## 1. 概述 (Overview)

Phase 7c 在 Agent Control Plane 与外部协议适配器（MCP、HTTP、SignalR）之间引入了一个纯 DTO 序列化边界。所有工具请求/结果类型都通过**密封 record DTO** 暴露，不再暴露领域内部的 `IDescriptor`、`IServiceProvider`、`JsonElement` 或运行时处理程序类型。

Phase 7d 在 Phase 7c DTO 边界之上增加了审查报告（Review Report）与修复提案（Fix Proposal）契约层。审查报告将审查结果转换为结构化、确定性、人工/Agent 可读的报告；修复提案契约升级使提案能表达结构化值变更、种类标签和安全等级。审查报告无治理决定权、无激活决定权、无运行时注册表变异能力。

Phase 7e 实现了安全激活工作流（Safe Activation Workflow）——从已审查的描述符草稿到运行时激活的完整路径。核心能力包括：激活请求生命周期管理（提交/审批/拒绝/取消/状态查询）、基于 HumanTask 的人工审核编排、证据重校验（通过 7 字段 CanonicalHash 比较检测激活请求创建后的漂移）、治理决策传递、以及 `IRuntimeActivationGate` 作为唯一运行时状态变异入口的架构不变量。合约版本迭代至 `7e.v1`，Phase 7e.1 将哈希生产与验证迁移到 canonical hash 基础设施，替换了 ad-hoc SHA256 管道拼接。

Phase 7e+ 实现了 Agent Memory & Context Compression Runtime——从对话/任务历史到压缩上下文、记忆候选提取、晋升/拒绝/替代/归档、召回与源扩展的完整链路。核心能力包括：内容脱敏（Regex-based redaction + 全量拒绝）、上下文压缩（对话/任务 → 压缩块）、记忆候选提取、记忆生命周期管理（Promote/Reject/Supersede/Archive）、基于查询的记忆召回（含置信度排序、字符预算、可见描述符过滤、ScopeFingerprint）、源扩展（按 SourceKind 分发到对应 Store）、以及 AgentAuthoringContext 组装（Request + MetadataContextPack + MemoryPack → AuthoringContext）。所有合约类型使用 CanonicalHash 标识内容身份和完整性，AgentMemoryPack.IsAuthoritative 始终为 false——元数据优先于冲突记忆。依赖边界：Memory.Abstractions 不引用 ControlPlane.Abstractions。

Phase 7f 实现了 AI-assisted Descriptor Authoring Golden Scenario——从意图文本到描述符草稿创作、审查、治理、激活绑定、运行时证明的完整端到端链路。核心交付物包括：IDescriptorAuthoringAgent 接口（消费 AgentAuthoringContext，产出 DescriptorDraftSet）、FakeCompanyCertificationAuthoringAgent（确定性假 Agent，无 LLM 依赖）、CompanyCertificationAuthoringGoldenScenarioRunner（三方法编排：RunUntilDraftSetReviewAsync / RunAsync / RunActivationOnlyAsync）、ActivationBindingReferenceRegistry（绑定引用在创建点注册，激活前只读验证）、以及 CompanyCertificationAuthoringGoldenScenarioReport。Draft set 实现原子性——全部成功或全部 block。Final scenario-level decision 基于 startingInventory → finalProposedInventory 的完整 inventory diff（IDescriptorChangeSetBuilder → IDescriptorImpactAnalyzer → IDescriptorCompatibilityAnalyzer），不取最后一个 draft review 的结果。激活绑定使用真实 review hash（IDescriptorDraftReviewHashService）、真实 package hash（IDescriptorPackageBuilder + IDescriptorPackageCanonicalHashComputer）、真实 descriptor hash（IDescriptorStableHashBuilder），无 placeholder fallback。运行时证明从 fresh host 构建，使用 approved final inventory。

核心交付物：
- **34 个 Tool DTO** — 统一的密封 record 类型，代替所有领域类型（新增 2 个：BuildDescriptorReviewReport、RenderDescriptorReviewReport）
- **Source-Generated JSON Contract** — `AgentControlPlaneToolJsonSerializerContext`，AoT 兼容
- **Source-Generated Payload DTOs (New in #42)** — `AgentDraftPayloadDto` 和 6 个子 record 由 `AgentDraftContractGenerator` 在 `CrestCreates.CodeGenerator` 中生成
- **Source-Generated Patch DTOs (New in #42)** — `AgentDraftPayloadPatchDto` 和 `Agent{Kind}DraftChangedField` 枚举用于 Update 操作的字段级合并
- **Source-Generated Projection (New in #42)** — `AgentDraftPayloadProjection` 提供 `Create`、`FromDomain`、`Merge` 方法，替代手写 payload 投影
- **Projection Helpers** — 领域 ←→ DTO 的双向投影，位于 ControlPlane 项目
- **Contract Version** — `AgentControlPlaneContractVersion.Current = "7e.v1"`
- **Review Report DTO & Builder (Phase 7d)** — `DescriptorReviewReportDto`（13 个固定 Section）、`IDescriptorReviewReportBuilder` + `DefaultDescriptorReviewReportBuilder`
- **Review Report Renderer (Phase 7d)** — `IDescriptorReviewReportRenderer` + `DefaultDescriptorReviewReportRenderer`，Markdown/PlainText 确定性投影
- **Message Template Catalog (Phase 7d)** — `IDescriptorReviewMessageTemplateCatalog` + `DefaultDescriptorReviewMessageTemplateCatalog`，31 个确定性模板
- **Fix Proposal Contract Upgrade (Phase 7d)** — `FixProposalKind`（9 值）、`FixProposalActionKind`（10 值）、`FixProposalApplicability`（4 值）、`FixProposalActionSafetyLevel`（4 值）、`JsonElement?` 值类型
- **Activation Abstractions (Phase 7e)** — 15 个激活契约类型：`ActivationRequest`、`ActivationBindingSnapshot`、`BindingHashes`（7 字段 CanonicalHash：SourceReviewHash、ReviewManifestHash、PackageManifestHash、PackageEvidenceHash、PackageEvidenceEnvelopeHash、ContractHash、DefinitionHash）、`ActivationRequestStatus`、`DescriptorActivationReviewDecision`、`DescriptorActivationReviewTaskInput`、`DescriptorActivationPolicy`、`DescriptorActivationEligibility` 等
- **Activation Services (Phase 7e)** — 7 个核心接口 + 8 个实现：`IDescriptorActivationRequestService`（生命周期）、`IActivationEvidenceRechecker`（证据重校验）、`IRuntimeActivationGate`（唯一运行时状态变异入口）、`IActivationReviewOrchestrator`（HumanTask 编排）、`IActivationBindingArtifactResolver`（绑定引用哈希解析）、`IDescriptorActivationPolicyProvider`（策略）、`IDescriptorActivationAuditor`（审计）
- **HumanTask + EventBus Integration (Phase 7e)** — `DescriptorActivationReviewHumanTaskEventHandler` 处理 HumanTask 完成回调，触发审查决策处理
- **Governance Integration (Phase 7e)** — 治理决策从审查结果流向 `SubmitActivationRequestRequest.GovernanceDecision`，绑定到激活请求快照
- **Agent Memory Contracts (Phase 7e+)** — 27 个 sealed record/enum + 11 个接口 + DiagnosticCodes + CanonicalShapeVersions，覆盖对话存储、任务历史、压缩上下文、记忆存储/提取/晋升/召回、源扩展、内容脱敏、AuthoringContext 组装
- **Agent Memory Implementations (Phase 7e+)** — 11 个默认服务：4 个 InMemory Store、Sanitizer、Compressor、Extractor、PromotionService、Retriever、SourceExpander、AuthoringContextBuilder
- **Agent Memory JSON Context (Phase 7e+)** — `AgentMemoryJsonSerializerContext`，AoT 兼容，注册 19 个 Root 类型
- **Authoring Agent Contracts (Phase 7f)** — `IDescriptorAuthoringAgent`、`DescriptorDraftSet`、`DescriptorAuthoringResult`
- **Fake Authoring Agent (Phase 7f)** — `FakeCompanyCertificationAuthoringAgent`，确定性，仅消费 `AgentAuthoringContext`
- **Authoring Golden Scenario Runner (Phase 7f)** — `CompanyCertificationAuthoringGoldenScenarioRunner`，三方法编排 + `ActivationBindingReferenceRegistry`
- **Authoring Golden Scenario Report (Phase 7f)** — `CompanyCertificationAuthoringGoldenScenarioReport`

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
│   │ (34 sealed records)                   │  │
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
| `CrestCreates.Agent.Memory.Abstractions` | Agent Memory 合约（27 个 sealed record/enum + 11 个接口 + DiagnosticCodes + CanonicalShapeVersions + AgentMemoryJsonSerializerContext）；不引用 ControlPlane.Abstractions |
| `CrestCreates.Agent.Memory` | 11 个默认服务实现：4 个 InMemory Store、Sanitizer、Compressor、Extractor、PromotionService、Retriever、SourceExpander、AuthoringContextBuilder、AgentMemoryCanonicalHashProjector |
| `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring` | IDescriptorAuthoringAgent、FakeCompanyCertificationAuthoringAgent、CompanyCertificationAuthoringGoldenScenarioRunner/Report、ActivationBindingReferenceRegistry |
| `CrestCreates.Agent.ControlPlane.Tests` | 423 个 ControlPlane 测试（含 DraftContracts + Generator + Activation）+ 8 个 Boundary 测试 = **431 个测试** |

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
│  │  │ (34 tools)   │ │ (DescriptorRef,│ │ (AgentTool-  │    │    │
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
│  │  AgentControlPlaneContractVersion.Current = "7e.v1"       │    │
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
| `AgentDraftContractManifest` | DraftContracts/Manifest (Generated) | `SupportedKinds`、`ContractVersion`("7d.v1")、per-kind 字段元数据 | **Implemented (#42)** |
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
| `AgentControlPlaneContractVersion` | Abstractions/Json | `Current = "7e.v1"` | **Implemented** |
| `AgentToolDescriptor.ContractVersion` | Abstractions | 工具描述符上的契约版本字段 | **Implemented** |
| `AgentToolName` | Abstractions | 34 个工具名称常量 | **Implemented** |
| `AgentToolCategory` | Abstractions | 8 个工具分类枚举 | **Implemented** |
| `AgentToolResult<T>` | Abstractions | 泛型工具结果包装（Success/Denied/Failed/InvalidRequest/NotFound） | **Implemented** |
| `DescriptorSummaryDtoProjection` | ControlPlane/Projections | `FromDescriptor(IDescriptor)` | **Implemented** |
| `AgentDescriptorDraftDtoProjection` | ControlPlane/Projections | `FromDraft` / 委托 `AgentDraftPayloadProjection` 处理 payload 操作 | **Implemented** |
| `AgentReviewResultDtoProjection` | ControlPlane/Projections | `Project`（含可见性过滤） | **Implemented** |
| `StaticAgentToolManifestProvider` | ControlPlane | 静态工具描述符声明（34 个工具） | **Implemented** |
| `DescriptorReviewReportDto` | Abstractions/ToolDtos | 13 固定 Section 审查报告 DTO，含 Recommendations、源绑定字段 | **Implemented (Phase 7d)** |
| `DescriptorReviewReportSectionDto` | Abstractions/ToolDtos | 审查报告 Section DTO，含 Kind、SectionId、IsEmpty、Items | **Implemented (Phase 7d)** |
| `DescriptorReviewReportItemDto` | Abstractions/ToolDtos | 审查报告条目 DTO，含 ReasonCode、Message、Severity、Parameters | **Implemented (Phase 7d)** |
| `DescriptorReviewRecommendationDto` | Abstractions/ToolDtos | 机器可解析的推荐动作 DTO，含 Kind、IsActionable、RelatedItemIds | **Implemented (Phase 7d)** |
| `DescriptorReviewReportBuildRequest` | ControlPlane | Builder 请求包装，含 ReviewResult、Draft、VisibilityApplied | **Implemented (Phase 7d)** |
| `DescriptorReviewReportSectionKind` | Abstractions | 13 值枚举：Summary=1 至 StableHashes=13 | **Implemented (Phase 7d)** |
| `DescriptorReviewSeverity` | Abstractions | 4 值枚举：Info=1, Warning=2, Error=3, Blocker=4 | **Implemented (Phase 7d)** |
| `DescriptorReviewRecommendationKind` | Abstractions | 6 值枚举：RequestActivationHandoff=1 至 NoAction=6 | **Implemented (Phase 7d)** |
| `DescriptorReviewReportFormat` | Abstractions | 2 值枚举：Markdown=1, PlainText=2 | **Implemented (Phase 7d)** |
| `IDescriptorReviewReportBuilder` | ControlPlane | Builder 接口，`Build(DescriptorReviewReportBuildRequest)` | **Implemented (Phase 7d)** |
| `DefaultDescriptorReviewReportBuilder` | ControlPlane | 默认 Builder，13 个 Build*Section 方法，TimeProvider 注入，SHA256 ReportId | **Implemented (Phase 7d)** |
| `IDescriptorReviewReportRenderer` | ControlPlane | Renderer 接口，`RenderMarkdown` + `RenderPlainText` | **Implemented (Phase 7d)** |
| `DefaultDescriptorReviewReportRenderer` | ControlPlane | 默认 Renderer，DTO-only，ContractVersion 验证，无外部依赖 | **Implemented (Phase 7d)** |
| `IDescriptorReviewMessageTemplateCatalog` | ControlPlane | 消息模板目录接口，`Format(templateId, parameters)` | **Implemented (Phase 7d)** |
| `DefaultDescriptorReviewMessageTemplateCatalog` | ControlPlane | 31 个确定性模板，regex 替换，TemplateVersion="7d.v1" | **Implemented (Phase 7d)** |
| `FixProposal (upgraded)` | Abstractions | 新增 Kind、Title、Explanation、Applicability、IsExecutable、ContractVersion 等；ProposalId→Id | **Implemented (Phase 7d)** |
| `FixProposalAction (upgraded)` | Abstractions | Path→TargetPath；CurrentValue/ProposedValue string→JsonElement?；新增 IsExecutable、SafetyLevel | **Implemented (Phase 7d)** |
| `FixProposalKind` | Abstractions | 9 值枚举：CreateMissingDescriptor=1 至 SetRequiredField=9 | **Implemented (Phase 7d)** |
| `FixProposalActionKind` | Abstractions | 10 值枚举：SetValue=1 至 ManualActionRequired=10 | **Implemented (Phase 7d)** |
| `FixProposalApplicability` | Abstractions | 4 值枚举：CurrentMutableDraft=1 至 NotApplicable=4 | **Implemented (Phase 7d)** |
| `FixProposalActionSafetyLevel` | Abstractions | 4 值枚举：Safe=1 至 Unsafe=4 | **Implemented (Phase 7d)** |
| `ActivationRequest` | Abstractions/Activation | 激活请求记录，含 RequestId、DraftId、TenantId、Status、BindingSnapshot、Policy、CreatedAt、GovernanceDecision | **Implemented (Phase 7e)** |
| `ActivationBindingSnapshot` | Abstractions/Activation | 激活请求时的绑定引用与哈希快照，`required` 字段：ReviewResultId、DraftVersion、PackagePreviewId、EvidencePreviewId、Hashes | **Implemented (Phase 7e)** |
| `BindingHashes` | Abstractions/Activation | 7 个 CanonicalHash 字段：SourceReviewHash、ReviewManifestHash、PackageManifestHash、PackageEvidenceHash、PackageEvidenceEnvelopeHash、ContractHash、DefinitionHash；PackageHashes 便捷访问器返回 DescriptorPackageHashSet | **Implemented (Phase 7e → 7e.1 升级)** |
| `ActivationRequestStatus` | Abstractions/Activation | 6 值枚举：Submitted、UnderReview、Approved、Rejected、Cancelled、Expired | **Implemented (Phase 7e)** |
| `DescriptorActivationReviewDecision` | Abstractions/Activation | 审查决策 DTO，含 ActivationRequestId、ActorId、ActorKind、Decision、BoundEvidenceHash、BoundEnvelopeHash | **Implemented (Phase 7e)** |
| `DescriptorActivationReviewTaskInput` | Abstractions/Activation | HumanTask 负载，含 ActivationRequestId、DraftId、DescriptorKind、ReviewSummary、EvidenceSummary、BindingHashes?、PackageManifestSummary、ImpactContext 等 | **Implemented (Phase 7e)** |
| `DescriptorActivationPolicy` | Abstractions/Activation | 策略快照：AllowSelfApproval、ForbidSelfApproval、[Obsolete]RequireEvidenceBinding、MaxConcurrentActivations、PolicySummary | **Implemented (Phase 7e)** |
| `DescriptorActivationEligibility` | Abstractions/Activation | 派生资格：AutoActivatable、RequiresHumanReview、Blocked | **Implemented (Phase 7e)** |
| `DescriptorActivationAuditRecord` | Abstractions/Activation | 激活事件审计跟踪 | **Implemented (Phase 7e)** |
| `DescriptorActivationAuditAction` | Abstractions/Activation | 审计动作枚举：Submit、Approve、Reject、Cancel、Block、GateDenied、Expire | **Implemented (Phase 7e)** |
| `DescriptorActivationActorKind` | Abstractions/Activation | 请求者种类：Agent、Human、System | **Implemented (Phase 7e)** |
| `DescriptorActivationReviewOutcome` | Abstractions/Activation | 审查结果：Approved、Rejected、Deferred | **Implemented (Phase 7e)** |
| `SubmitActivationRequestRequest` | Abstractions | 提交激活请求 DTO，新增 GovernanceDecision 字段（DescriptorLifecycleDecisionKind?） | **Implemented (Phase 7e)** |
| `DescriptorActivationReviewDecisionParser` | Abstractions/Activation | AoT 安全 TryParse，从 JSON 解析审查决策 | **Implemented (Phase 7e)** |
| `IDescriptorActivationRequestService` | ControlPlane/Activation | 激活请求生命周期接口：Create、Approve、Reject、Cancel、GetStatus | **Implemented (Phase 7e)** |
| `DefaultDescriptorActivationRequestService` | ControlPlane/Activation | 默认实现，含策略快照、证据绑定验证、fail-closed 空值守卫 | **Implemented (Phase 7e)** |
| `IActivationEvidenceRechecker` | ControlPlane/Activation | 证据重校验接口：验证 BindingHashes 7 字段 CanonicalHash 比较 | **Implemented (Phase 7e → 7e.1 升级)** |
| `DefaultActivationEvidenceRechecker` | ControlPlane/Activation | 默认实现，全字段 CanonicalHash 记录相等性比较 | **Implemented (Phase 7e → 7e.1 升级)** |
| `IRuntimeActivationGate` | ControlPlane/Activation | 运行时激活门接口：唯一运行时状态变异入口（架构不变量） | **Implemented (Phase 7e)** |
| `DefaultRuntimeActivationGate` | ControlPlane/Activation | 默认实现，激活时变异运行时描述符注册表 | **Implemented (Phase 7e)** |
| `IActivationReviewOrchestrator` | ControlPlane/Activation | HumanTask 审查编排接口：CreateTask、HandleCompletion | **Implemented (Phase 7e)** |
| `DefaultActivationReviewOrchestrator` | ControlPlane/Activation | 默认实现，通过 IHumanTaskRuntime 创建任务 + DescriptorActivationReviewHumanTaskEventHandler 回调 | **Implemented (Phase 7e)** |
| `IActivationBindingArtifactResolver` | ControlPlane/Activation | 绑定引用哈希解析接口：从绑定引用解析当前制品哈希 | **Implemented (Phase 7e)** |
| `DefaultActivationBindingArtifactResolver` | ControlPlane/Activation | 默认实现，用于证据重校验时获取当前哈希 | **Implemented (Phase 7e)** |
| `IDescriptorActivationPolicyProvider` | ControlPlane/Activation | 激活策略提供者接口：按租户/描述符种类提供策略 | **Implemented (Phase 7e)** |
| `DefaultDescriptorActivationPolicyProvider` | ControlPlane/Activation | 默认策略提供者实现 | **Implemented (Phase 7e)** |
| `IDescriptorActivationAuditor` | ControlPlane/Activation | 激活审计接口：记录激活审计事件 | **Implemented (Phase 7e)** |
| `DefaultDescriptorActivationAuditor` | ControlPlane/Activation | 默认审计实现 | **Implemented (Phase 7e)** |
| `DescriptorActivationReviewHumanTaskEventHandler` | ControlPlane/Activation | EventBus 事件处理程序：处理 HumanTaskCompletedEvent，将决策路由到 RequestService | **Implemented (Phase 7e)** |

### 4.1 工具覆盖 (34 Tools)

| Wave | 分类 | 工具数 | 范围 |
|------|------|--------|------|
| Wave 1 | Context / Read | 6 | BuildMetadataContextPack, BuildRuntimeScenarioContextPack, GetDescriptorByRef, SearchDescriptors, ListDescriptorRelationships, GetTopologySummary |
| Wave 2 | Draft | 6 | CreateDescriptorDraft, UpdateDescriptorDraft, GetDescriptorDraft, ListDescriptorDrafts, CancelDescriptorDraft, CompareDescriptorDraft |
| Wave 3 | Review | 5 | ValidateDescriptorDraft, ReviewDescriptorDraft, GetDraftReviewResult, ListDraftReviewResults, ExplainDiagnostics |
| Wave 3.5 | ReviewReport | 2 | BuildDescriptorReviewReport, RenderDescriptorReviewReport |
| Wave 4 | Fix Proposal | 4 | SuggestDescriptorDraftFixes, GetFixProposal, ListFixProposals, ApplyFixProposalToDraft |
| Wave 5 | Package Preview | 4 | PreviewDescriptorPackage, BuildPackageEvidencePreview, BuildActivationReadinessPreview, GetPackagePreview |
| Wave 6 | Activation Handoff | 3 | SubmitActivationRequest, GetActivationRequestStatus, CancelActivationRequest（Phase 7e 完整实现激活工作流：HumanTask 审查编排、证据重校验、IRuntimeActivationGate 唯一运行时状态变异入口） |
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
    public const string Current = "7e.v1";
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
| 兼容新增（添加可选字段） | 次要升级 (`7d.v2`) | 现有适配器无感知 |
| 兼容变更（添加新 DTO） | 次要升级 (`7d.v2`) | 需要新的 JSON 上下文注册 |
| 破坏性变更（移除/重命名字段） | 主要升级 (`7e.v1`) | 适配器必须同步升级 |
| 引入全量子结构 | 主要升级（`7e.v1` 或更高） | Sub-record 字段扩展 |

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
| **7d** | Tool DTO, JSON Contract & Review Report — Review Report DTO, Builder, Renderer, Fix Proposal Contract Upgrade (#16) | **Implemented** |
| 7e | Activation Workflow（已审查的草稿 → 运行时激活） | **Implemented (#17)** |
| **7e+** | Agent Memory & Context Compression Runtime（对话/任务历史→压缩→提取→晋升→召回→AuthoringContext） | **Implemented (#43)** |
| **7f** | AI-assisted Descriptor Authoring Golden Scenario（意图→创作→审查→激活→运行时证明完整链路） | **Implemented (#32)** |
| 7a | Descriptor Draft Runtime（存储、验证、物化、审查） | Implemented |

### 10.1 Phase 7e — Safe Activation Workflow (#17)

Phase 7e 实现了从已审查的描述符草稿到运行时激活的完整工作流。核心链路：`IDescriptorActivationRequestService`（生命周期管理）→ `IActivationEvidenceRechecker`（证据重校验）→ `IRuntimeActivationGate`（唯一运行时状态变异入口）。

**关键架构决策**：

1. **IRuntimeActivationGate 是唯一运行时状态变异入口** — 除 Gate 外，没有其他代码路径可以修改运行时描述符注册表
2. **证据重校验比较全部 6 个 BindingHashes 字段** — 使用完整的 CanonicalHash 记录相等性（不仅仅是 .Value 摘要）
3. **治理决策从审查结果流向激活请求** — ToolService 提取 `reviewRef.Review.GovernanceDecision?.MaxDecision`，通过 `SubmitActivationRequestRequest.GovernanceDecision` 传递给 RequestService
4. **绑定完整性由编译期 + 运行时共同强制执行** — `required string` 在 `PackagePreviewId`/`EvidencePreviewId` 上 + `IsNullOrWhiteSpace` 运行时守卫 + `ACTIVATION_INCOMPLETE_BINDING` 次级 fail-closed
5. **审批绑定到证据快照** — `ApproveActivationRequestAsync` 验证 `reviewDecision.BoundEvidenceHash`/`BoundEnvelopeHash` 与 `request.BindingSnapshot.Hashes.EvidenceHash`/`EnvelopeHash`
6. **审批/拒绝验证 ActivationRequestId** — 两条路径均检查 `reviewDecision.ActivationRequestId == request.RequestId`
7. **自我审批使用快照策略** — `request.Policy` 在创建时捕获，不使用实时查找；回退使用 `snapshot.Owner.DescriptorKind`
8. **Fail-closed 空值守卫** — `BindingSnapshot` 和 `BindingSnapshot.Hashes` 在所有入口点执行空值检查，附带结构化诊断
9. **审计动作语义** — 验证失败使用 `GateDenied`/`Block`，而非 `Reject`（后者表示人工拒绝）
10. **HumanTask 集成** — 审查通过 `IHumanTaskRuntime.CreateAsync` 创建任务 + `DescriptorActivationReviewHumanTaskEventHandler` 处理完成回调
11. **EventBus 集成** — `HumanTaskCompletedEvent` 触发审查决策处理
12. **ToolService 委托给 RequestService** — 不在 ToolService 中维护双轨激活逻辑

**新增 15 个 Abstractions 类型 + 8 个 ControlPlane 类型 + 9 个激活诊断码（SCREAMING_SNAKE_CASE）**。

**激活诊断码**：`ACTIVATION_BINDING_SNAPSHOT_REQUIRED`、`ACTIVATION_BINDING_HASHES_REQUIRED`、`ACTIVATION_INCOMPLETE_BINDING`、`ACTIVATION_INCOMPLETE_EVIDENCE_BINDING`、`ACTIVATION_REVIEW_DECISION_MISMATCH`、`ACTIVATION_REVIEW_REQUEST_MISMATCH`、`ACTIVATION_REVIEW_EVIDENCE_MISMATCH`、`ACTIVATION_REVIEW_ENVELOPE_MISMATCH`、`ACTIVATION_INVALID_STATUS_FOR_REJECTION`。

**测试覆盖**：431 个 ControlPlane 测试 + 8 个 Boundary 测试，包括完整的激活请求生命周期、证据重校验、HumanTask 回调、策略快照和审计追踪覆盖。

### 10.2 Phase 7b — LLM Bootstrap Plane

Phase 7b 将引入 LLM 驱动的描述符草稿生成。核心组件已设计但尚未实现：

- `PromptTemplate` — 结构化提示模板，带描述符上下文注入
- `ILLMProvider` — 可插拔的 LLM 后端抽象（OpenAI、Anthropic、本地模型）
- `PromptTemplateRegistry` — 按描述符类型存储和解析提示模板
- `DescriptorDraftBuilder` — 将 LLM 结构化输出转换为 `DescriptorDraft` 实例

LLM 生成的草稿将经过与人工草稿相同的 `IDescriptorDraftValidator` 验证管道，确保一致性。

---

## 11. Review Report & Fix Proposal (Phase 7d)

> **Phase 7d** 在 Phase 7c DTO 边界之上新增了审查报告（Review Report）与修复提案（Fix Proposal）契约层。审查报告是将审查结果转换为结构化、确定性、可读报告的构建+渲染管道；修复提案契约升级使提案能表达结构化值变更、种类标签和安全等级。

### 11.1 Review Report 架构

审查报告的核心原则是**结构化 DTO 是权威制品**，Markdown/PlainText 是确定性投影，不是决策输入。

#### DescriptorReviewReportDto

```csharp
public sealed record DescriptorReviewReportDto
{
    public required string ReportId { get; init; }                // 稳定 hash
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required string ReviewResultId { get; init; }          // 源绑定：绑定到哪个审查结果
    public required string DraftVersion { get; init; }            // 源绑定：绑定到哪个草稿修订版
    public required string SourceReviewHash { get; init; }        // 审查结果的稳定身份
    public required string TemplateVersion { get; init; }         // 消息模板目录版本
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string ContractVersion { get; init; } = AgentControlPlaneContractVersion.Current;

    // 机器可解析的推荐动作 — Agent 读取此字段，而非 Section Items
    public required IReadOnlyList<DescriptorReviewRecommendationDto> Recommendations { get; init; }

    // 13 个固定 Section — 始终存在，可为空 (IsEmpty = true)
    public required DescriptorReviewReportSectionDto SummarySection { get; init; }
    public required DescriptorReviewReportSectionDto DraftIdentitySection { get; init; }
    public required DescriptorReviewReportSectionDto ProposedChangesSection { get; init; }
    public required DescriptorReviewReportSectionDto ImpactAnalysisSection { get; init; }
    public required DescriptorReviewReportSectionDto DependencySummarySection { get; init; }
    public required DescriptorReviewReportSectionDto CompatibilitySection { get; init; }
    public required DescriptorReviewReportSectionDto GovernanceSection { get; init; }
    public required DescriptorReviewReportSectionDto RequiredHumanReviewSection { get; init; }
    public required DescriptorReviewReportSectionDto ActivationEligibilitySection { get; init; }
    public required DescriptorReviewReportSectionDto DiagnosticsSection { get; init; }
    public required DescriptorReviewReportSectionDto RecommendationsSection { get; init; }
    public required DescriptorReviewReportSectionDto PackagePreviewSection { get; init; }
    public required DescriptorReviewReportSectionDto StableHashesSection { get; init; }
}
```

#### DescriptorReviewReportSectionDto

```csharp
public sealed record DescriptorReviewReportSectionDto
{
    public required DescriptorReviewReportSectionKind Kind { get; init; }
    public required string SectionId { get; init; }               // 稳定小写外部 ID（如 "summary"、"draft_identity"）
    public required string Title { get; init; }
    public required int Order { get; init; }                      // 确定性规范顺序
    public required bool IsEmpty { get; init; }                   // Renderer 可隐藏空 Section
    public required DescriptorReviewSeverity OverallSeverity { get; init; }
    public required IReadOnlyList<DescriptorReviewReportItemDto> Items { get; init; }
}
```

#### DescriptorReviewReportItemDto

```csharp
public sealed record DescriptorReviewReportItemDto
{
    public required string ItemId { get; init; }
    public required string ReasonCode { get; init; }
    public required string MessageTemplateId { get; init; }
    public required string Message { get; init; }                 // 确定性规范文本
    public required DescriptorReviewSeverity Severity { get; init; }
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
    public IReadOnlyList<string> RelatedDiagnosticIds { get; init; } = [];
    public IReadOnlyList<string> RelatedDescriptorIds { get; init; } = [];
}
```

#### DescriptorReviewRecommendationDto

```csharp
public sealed record DescriptorReviewRecommendationDto
{
    public required string RecommendationId { get; init; }
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public required DescriptorReviewRecommendationKind Kind { get; init; }
    public required bool IsActionable { get; init; }
    public IReadOnlyList<string> RelatedItemIds { get; init; } = [];
}
```

#### 关键枚举

| 枚举 | 值 | 说明 |
|------|-----|------|
| `DescriptorReviewReportSectionKind` | Summary=1, DraftIdentity=2, ProposedChanges=3, ImpactAnalysis=4, DependencySummary=5, Compatibility=6, Governance=7, RequiredHumanReview=8, ActivationEligibility=9, Diagnostics=10, Recommendations=11, PackagePreview=12, StableHashes=13 | 13 种固定 Section 类型 |
| `DescriptorReviewSeverity` | Info=1, Warning=2, Error=3, Blocker=4 | 严重级别 |
| `DescriptorReviewRecommendationKind` | RequestActivationHandoff=1, RequestHumanReview=2, ApplyFixProposal=3, ReviseDraft=4, CancelDraft=5, NoAction=6 | 推荐动作种类 |
| `DescriptorReviewReportFormat` | Markdown=1, PlainText=2 | 渲染输出格式 |

#### 关键设计决策

1. **结构化 DTO 是权威制品**。Markdown/PlainText 是确定性投影，不是决策输入。
2. **13 个固定 Section** 始终存在。`IsEmpty` 标记允许 Renderer 隐藏空 Section。
3. **ReasonCode + MessageTemplateId + Parameters** 实现确定性文本生成，无需 LLM。
4. **RelatedDiagnosticIds / RelatedDescriptorIds** 支持修复提案关联和追溯。
5. **DescriptorReviewRecommendationKind.RequestActivationHandoff** — Phase 7d 不拥有激活权限；这是交接请求，不是激活决策。
6. **Section 顺序**由 `DescriptorReviewReportSectionKind` 声明顺序决定（Summary=1 至 StableHashes=13）。Builder 按此顺序发出 Section；测试验证顺序稳定性。
7. **SectionId 是小写稳定 ID**（如 `summary`、`draft_identity`），与枚举名称解耦。
8. **源绑定**：`ReviewResultId`、`DraftVersion`、`SourceReviewHash` 将报告绑定到特定审查结果和草稿修订版，防止过期报告与当前状态混淆。
9. **推荐位于顶层**：`Recommendations` 列表是报告 DTO 的一等字段，供机器解析。`RecommendationsSection` 提供人读条目。
10. **ReportId 生成**：`(TenantId + DraftId + DraftVersion + ReviewResultId + ContractVersion + TemplateVersion)` 的稳定 SHA256 哈希，使用 `IDescriptorStableHashBuilder` 模式。

### 11.2 Builder/Renderer 架构

#### Request 对象

```csharp
public sealed record DescriptorReviewReportBuildRequest
{
    public required DescriptorDraftReviewResult ReviewResult { get; init; }
    public required DescriptorDraft Draft { get; init; }
    public required bool VisibilityApplied { get; init; }
}
```

#### Builder 接口与实现

```csharp
public interface IDescriptorReviewReportBuilder
{
    DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request);
}

// CrestCreates.Agent.ControlPlane 中：
internal sealed class DefaultDescriptorReviewReportBuilder : IDescriptorReviewReportBuilder
{
    private readonly TimeProvider _clock;
    private readonly IDescriptorReviewMessageTemplateCatalog _templateCatalog;

    public DefaultDescriptorReviewReportBuilder(
        TimeProvider clock,
        IDescriptorReviewMessageTemplateCatalog templateCatalog)
    {
        _clock = clock;
        _templateCatalog = templateCatalog;
    }

    public DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request)
    {
        // Fail-fast: Builder 是投影层，不是可见性/编辑层
        if (!request.VisibilityApplied)
        {
            throw new InvalidOperationException(
                "DescriptorReviewReportBuilder requires a visibility-projected review result.");
        }

        // 构建 13 个 Section
        // 派生 Recommendations
        // 通过 _templateCatalog.Format(templateId, parameters) 填充 Message
        // ReportId = 稳定 hash(TenantId + DraftId + DraftVersion + ReviewResultId + ContractVersion + TemplateVersion)
    }
}
```

#### 13 个 Section 映射

| Section Kind | 数据源 | 逻辑 |
|---|---|---|
| Summary | reviewResult 整体 | 聚合严重级别计数、激活资格、治理决策 |
| DraftIdentity | draft | DraftId、DescriptorKind、Operation、AuthorKind、Intent、Status |
| ProposedChanges | reviewResult.MaterializationResult | 提议库存引用、物化状态 |
| ImpactAnalysis | reviewResult.ImpactAnalysisResult | 受影响描述符数量/严重级别、依赖链 |
| DependencySummary | reviewResult.TopologySnapshot | 按种类节点/边计数、上游/下游摘要 |
| Compatibility | reviewResult.CompatibilityResult | 兼容/不兼容计数、不兼容详情 |
| Governance | reviewResult.GovernanceDecision | 决策、理由、批准状态 |
| RequiredHumanReview | reviewResult.Diagnostics + Governance | 需要人工关注的 Blocker/Error 诊断 |
| ActivationEligibility | reviewResult.IsActivationEligible | 资格状态、阻塞原因 — **仅解释，非门控** |
| Diagnostics | reviewResult.Diagnostics | 按严重级别分组的所有诊断 |
| Recommendations | 从所有 Section 派生 | 基于严重级别 + 治理 + 资格的下一步动作 |
| PackagePreview | reviewResult.PackagePreview | 哈希、描述符计数 |
| StableHashes | reviewResult.StableHashes | 所有哈希值 |

#### Message Template Catalog

```csharp
public interface IDescriptorReviewMessageTemplateCatalog
{
    string Format(string messageTemplateId, IReadOnlyDictionary<string, string> parameters);
}
```

Builder 决定 ReasonCode / MessageTemplateId / Parameters。MessageTemplateCatalog 将它们格式化为规范文本。这防止 Builder 变成投影 + 措辞 + 渲染器混合体。

31 个确定性模板示例：

| ReasonCode | MessageTemplateId | Message |
|---|---|---|
| `ACTIVATION_ELIGIBLE` | `report.activation.eligible` | `"Draft is eligible for activation handoff."` |
| `ACTIVATION_BLOCKED` | `report.activation.blocked` | `"Draft is not eligible: {BlockingReasons}."` |
| `GOVERNANCE_APPROVED` | `report.governance.approved` | `"Governance decision: approved. {Rationale}"` |
| `GOVERNANCE_REJECTED` | `report.governance.rejected` | `"Governance decision: rejected. {Rationale}"` |
| `GOVERNANCE_REVIEW_REQUIRED` | `report.governance.review_required` | `"Governance decision: review required. {Rationale}"` |
| `MISSING_REFERENCE` | `report.diagnostics.missing_ref` | `"Descriptor '{DescriptorId}' references missing '{ReferenceId}'."` |
| `SCHEMA_INCOMPATIBLE` | `report.compatibility.schema` | `"Schema change is incompatible: {Details}."` |
| `DRAFT_VALID` | `report.summary.valid` | `"Draft validation passed with {DiagnosticCount} diagnostics."` |
| `DRAFT_INVALID` | `report.summary.invalid` | `"Draft validation failed with {ErrorCount} errors and {BlockerCount} blockers."` |
| `HUMAN_REVIEW_REQUIRED` | `report.human_review.required` | `"Human review required: {Reason}."` |
| `NO_ACTION` | `report.recommendation.no_action` | `"No action required at this time."` |

#### Renderer 接口与约束

```csharp
public interface IDescriptorReviewReportRenderer
{
    string RenderMarkdown(DescriptorReviewReportDto report);
    string RenderPlainText(DescriptorReviewReportDto report);
}
```

**Renderer 硬性约束**：
- 仅读取 `DescriptorReviewReportDto` — 不访问注册表、目录或外部服务
- 使用 DTO 的 `Message` 字段 — 不通过 TemplateCatalog 重新生成文本
- **不执行**可见性过滤、治理决策、激活决策
- **不执行**运行时注册表变异、处理程序执行或 LLM 调用
- **确定性输出**：相同 DTO → 相同输出

#### 边界声明

> Builder 产生权威结构化报告和确定性条目消息。Renderer 从该 DTO 产生确定性 Markdown/PlainText 投影。MessageTemplateCatalog 将 ReasonCode+Parameters 格式化为规范 Message。Builder 和 Renderer 均不执行可见性过滤、治理决策、激活决策、运行时注册表变异、处理程序执行或 LLM 调用。

### 11.3 Fix Proposal 契约升级

#### 破坏性变更概览

| 类型 | 变更 |
|---|---|
| `FixProposal` | 新增 Kind、Title、Explanation、ReasonCode、Applicability、IsExecutable、RequiresManualAction、BlocksActivationUntilResolved、RelatedDiagnosticIds、RelatedDescriptorIds、ContractVersion；ProposalId → Id |
| `FixProposalAction` | Path → TargetPath；新增 TargetDescriptorId；CurrentValue/ProposedValue string → JsonElement?；新增 IsExecutable、SafetyLevel |
| `FixProposalActionKind` | 从 3 值扩展到 10 值 |
| 新增 `FixProposalKind` | 9 种修复种类 |
| 新增 `FixProposalApplicability` | 4 种适用性级别 |
| 新增 `FixProposalActionSafetyLevel` | 4 种安全级别 |

#### FixProposal（升级后）

```csharp
public sealed record FixProposal
{
    public required string Id { get; init; }                           // 原 ProposalId
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required FixProposalKind Kind { get; init; }                // NEW
    public required string Title { get; init; }                        // NEW
    public required string Explanation { get; init; }                  // NEW
    public required string ReasonCode { get; init; }                   // NEW
    public required FixProposalApplicability Applicability { get; init; } // NEW
    public required bool IsExecutable { get; init; }                   // NEW
    public required bool RequiresManualAction { get; init; }           // NEW
    public required bool RequiresHumanReview { get; init; }            // 保留
    public required bool BlocksActivationUntilResolved { get; init; }  // NEW — 仅解释，非门控
    public required FixProposalRiskLevel RiskLevel { get; init; }
    public IReadOnlyList<string> RelatedDiagnosticIds { get; init; } = [];  // NEW
    public IReadOnlyList<string> RelatedDescriptorIds { get; init; } = [];  // NEW
    public required IReadOnlyList<FixProposalAction> Actions { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Rationale { get; init; }
    public required string ContractVersion { get; init; } = AgentControlPlaneContractVersion.Current; // NEW
}
```

#### IsExecutable 聚合规则

```
FixProposal.IsExecutable =
    Applicability == FixProposalApplicability.CurrentMutableDraft
    && Actions.All(a => a.IsExecutable)
```

Builder 强制此规则。混合可执行/不可执行动作的提案为不可执行。

#### FixProposalAction（升级后）

```csharp
public sealed record FixProposalAction
{
    public required FixProposalActionKind Kind { get; init; }
    public required string TargetPath { get; init; }                // 原 Path
    public string? TargetDescriptorId { get; init; }                // NEW
    public JsonElement? CurrentValue { get; init; }                 // 原 string → JsonElement?
    public JsonElement? ProposedValue { get; init; }                // 原 string → JsonElement?
    public required bool IsExecutable { get; init; }                // NEW
    public required FixProposalActionSafetyLevel SafetyLevel { get; init; } // NEW
    public string? Description { get; init; }
}
```

**JsonElement 用法**：始终通过 `JsonSerializer.SerializeToElement(...)` 创建。必要时使用 `.Clone()` 避免 `JsonDocument` 生命周期问题。`FixProposalAction` 和 `JsonElement` 均需注册到 source-generated JSON 上下文。

#### 新增枚举

| 枚举 | 值 | 说明 |
|---|---|---|
| `FixProposalKind` | 9 值：CreateMissingDescriptor=1, ReplaceMissingReference=2, RemoveInvalidRelationship=3, AddRequiredBindingMetadata=4, SplitBreakingChangeIntoCompatibleChange=5, MarkRequiresReview=6, FlagUnsafeExpansion=7, SuggestVersionBump=8, SetRequiredField=9 | 修复提案种类。SetRequiredField 由 `MapDiagnosticToFixProposalKind` 对 RATIONALE_EMPTY/INTENT_EMPTY 诊断映射而来 |
| `FixProposalActionKind` | 10 值：SetValue=1, RemoveValue=2, AddValue=3, MergeObject=4, ReplaceReference=5, RemoveRelationship=6, AddRequiredBindingMetadata=7, SuggestVersionBump=8, MarkRequiresReview=9, ManualActionRequired=10 | 修复动作种类 |
| `FixProposalApplicability` | 4 值：CurrentMutableDraft=1, RequiresNewDraftRevision=2, ManualActionRequired=3, NotApplicable=4 | 提案适用性 |
| `FixProposalActionSafetyLevel` | 4 值：Safe=1, LowRisk=2, RequiresReview=3, Unsafe=4 | 动作安全级别 |

#### MapDiagnosticToFixProposalKind

| 诊断码 | 映射 FixProposalKind | 说明 |
|---|---|---|
| RATIONALE_EMPTY | SetRequiredField | 草稿缺少 Rationale 字段 |
| INTENT_EMPTY | SetRequiredField | 草稿缺少 Intent 字段 |
| (其他诊断) | MarkRequiresReview | 默认映射：标记为需要人工或系统审查 |

#### ApplyFixProposalToDraftAsync 运行时规则

| 条件 | 结果诊断码 |
|---|---|
| `action.IsExecutable == false` | NonExecutableFixAction |
| `proposal.Actions.Count > 1` | UnsupportedMultiActionFixProposal |
| `action.Kind` 不在支持的子集中 | UnsupportedFixActionKind |
| `action.SafetyLevel == Unsafe` | UnsafeFixActionRejected |
| 目标是活跃描述符 / 运行时注册表 | FixActionTargetBoundaryViolation |
| 目标路径不在允许集合中 | FixActionTargetNotAllowed |

**多动作策略**：Phase 7d 仅支持单动作可执行提案。`ApplyFixProposalToDraftAsync` 拒绝 `Actions.Count > 1` 的提案，通过 `UnsupportedMultiActionFixProposal` 诊断。多动作支持需要原子回滚（快照/克隆），延迟到后续阶段。这比在没有实现的情况下声称原子性更诚实。

**BlocksActivationUntilResolved**：这是**解释字段**，不是门控决策。它表示修复提案识别了会阻塞激活的问题，但实际激活门控属于 Phase 7e 或后续阶段。Phase 7d 不拥有激活阻塞权限。

### 11.4 工具表面更新

#### 2 个新增工具（Wave 3.5: ReviewReport）

| 工具名 | 请求类型 | 结果类型 | 权限 | 只读 |
|---|---|---|---|---|
| `BuildDescriptorReviewReport` | `string draftId` | `AgentToolResult<DescriptorReviewReportDto>` | agent.review.report | Yes |
| `RenderDescriptorReviewReport` | `DescriptorReviewReportDto` + `DescriptorReviewReportFormat` | `AgentToolResult<string>` | agent.review.render | Yes |

**注意**：`RenderDescriptorReviewReportAsync` 直接接受 `DescriptorReviewReportDto`，而非 `reportId`。DTO 是权威制品；`_reports` 字典是可选临时缓存。存在内部的便捷方法 `RenderStoredDescriptorReviewReportAsync(context, reportId, format)` 但**不作为工具暴露**。

#### BuildDescriptorReviewReportAsync 流程

```
context + draftId
  → ExecuteAsync (manifest → authorization → scope)
  → ResolveDraftAsync
  → DenyIfInvisible
  → Lookup reviewResult from _reviewResults
  → _reportBuilder.Build(request with reviewResult + draft + VisibilityApplied=true)
  → Store report in _reports dictionary (optional cache)
  → Return AgentToolResult<DescriptorReviewReportDto>
```

#### RenderDescriptorReviewReportAsync 流程

```
context + report DTO + format
  → ExecuteAsync (仅授权检查 — 不访问注册表/缓存)
  → Validate report.ContractVersion == AgentControlPlaneContractVersion.Current
    (不匹配 → UnsupportedReportContractVersion diagnostic)
  → format switch { Markdown → _renderer.RenderMarkdown, PlainText → _renderer.RenderPlainText }
  → Return AgentToolResult<string>
```

#### DI 注册

```csharp
services.AddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
services.AddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
services.AddSingleton<IDescriptorReviewMessageTemplateCatalog, DefaultDescriptorReviewMessageTemplateCatalog>();
```

### 11.5 JSON 上下文更新

`AgentControlPlaneToolJsonSerializerContext` 新增注册：

- `DescriptorReviewReportDto`
- `DescriptorReviewReportSectionDto`
- `DescriptorReviewReportItemDto`
- `DescriptorReviewRecommendationDto`
- `DescriptorReviewReportBuildRequest`
- `DescriptorReviewReportSectionKind`
- `DescriptorReviewSeverity`
- `DescriptorReviewRecommendationKind`
- `DescriptorReviewReportFormat`
- `FixProposalKind`
- `FixProposalApplicability`
- `FixProposalActionSafetyLevel`
- `JsonElement`（FixProposalAction 需要）
- 更新后的 `FixProposal` / `FixProposalAction` 注册（字段变更）

### 11.6 边界声明

| 边界 | 说明 |
|---|---|
| Builder 不执行可见性过滤 | Builder 需要预先过滤的 `DescriptorDraftReviewResult`，`VisibilityApplied=false` 时 fail-fast |
| Renderer 不调用 LLM 或外部服务 | Renderer 仅读取 DTO，不访问注册表、目录或网络 |
| FixProposal.BlocksActivationUntilResolved 是解释字段 | Phase 7d 不拥有激活门控权限 |
| Phase 7d 不拥有治理权限 | 治理决策仍来自 Control Plane |
| Phase 7d 不进行运行时注册表变异 | Fix Proposal 仅修改草稿，不变异活跃注册表 |
| IsExecutable 聚合规则 | `FixProposal.IsExecutable = Applicability==CurrentMutableDraft && Actions.All(IsExecutable)` |
| RequiresManualAction 一致性 | `FixProposal.RequiresManualAction == (Applicability == ManualActionRequired)` |

---

## 12. Safe Activation Workflow (Phase 7e)

> **Phase 7e (#17)** 实现了从已审查的描述符草稿到运行时激活的完整安全路径。核心链路：`IDescriptorActivationRequestService` → `IActivationEvidenceRechecker` → `IRuntimeActivationGate`，配合 HumanTask 审查编排和 EventBus 事件驱动回调。

### 12.1 架构概览

```
Protocol Adapters (MCP / HTTP / SignalR)
          ↓
┌─────────────────────────────────────────────────────────────┐
│  Tool Service (DefaultAgentControlPlaneToolService)          │
│  Wave 6: Submit / GetStatus / Cancel Activation Request      │
│                                                              │
│  SubmitActivationRequestAsync:                               │
│    1. Resolve draft → reviewResult                           │
│    2. Extract GovernanceDecision from reviewResult           │
│    3. Build ActivationBindingSnapshot (hashes + preview ids) │
│    4. → _requestService.SubmitAsync(request)                 │
└─────────────────────────────────────────────────────────────┘
          ↓
┌─────────────────────────────────────────────────────────────┐
│  IDescriptorActivationRequestService                         │
│  (DefaultDescriptorActivationRequestService)                 │
│                                                              │
│  SubmitAsync(request):                                       │
│    - Fail-closed null guards on BindingSnapshot              │
│    - Policy snapshot capture (AllowSelfApproval etc.)        │
│    - Build ActivationRequest record                          │
│    - Store + audit (Submit action)                           │
│    - → _reviewOrchestrator.CreateReviewTaskAsync(request)    │
│                                                              │
│  ApproveAsync(decision):                                     │
│    - Verify decision.ActivationRequestId == request.Id       │
│    - Verify decision.BoundEvidenceHash == snapshot.EvidenceHash │
│    - Verify decision.BoundEnvelopeHash == snapshot.EnvelopeHash │
│    - → _evidenceRechecker.RecheckAsync(request)              │
│    - → _runtimeActivationGate.ActivateAsync(request)         │
│    - Update status → Approved + audit                        │
│                                                              │
│  RejectAsync(decision):                                      │
│    - Verify decision.ActivationRequestId == request.Id       │
│    - Update status → Rejected + audit                        │
└─────────────────────────────────────────────────────────────┘
          ↓
┌─────────────────────────────────────────────────────────────┐
│  IActivationEvidenceRechecker                                │
│  (DefaultActivationEvidenceRechecker)                        │
│                                                              │
│  RecheckAsync(request):                                      │
│    - Resolve current artifact hashes via                     │
│      _artifactResolver                                        │
│    - Compare ALL 7 BindingHashes fields using CanonicalHash  │
│      equality (not just .Value digest)                       │
│    - Return result: { AllMatch, DriftedFields, Diagnosis }   │
└─────────────────────────────────────────────────────────────┘
          ↓
┌─────────────────────────────────────────────────────────────┐
│  IRuntimeActivationGate  ← THE ONLY runtime mutation point    │
│  (DefaultRuntimeActivationGate)                              │
│                                                              │
│  ActivateAsync(request):                                     │
│    - Validate request is in Approved status                  │
│    - Write descriptor to runtime registry                    │
│    - Mark draft as activated                                 │
│  Architectural invariant: ONLY this component mutates runtime │
└─────────────────────────────────────────────────────────────┘
```

### 12.2 HumanTask + EventBus 集成

激活审查请求创建 HumanTask 并通过 EventBus 处理完成回调：

```
SubmitAsync
  → _reviewOrchestrator.CreateReviewTaskAsync(request)
    → IHumanTaskRuntime.CreateAsync(taskInput)
    → HumanTask enters review queue

[Reviewer completes HumanTask]
  → HumanTaskCompletedEvent published on EventBus
  → DescriptorActivationReviewHumanTaskEventHandler handles event
    → Parses review decision via DescriptorActivationReviewDecisionParser (AoT-safe)
    → Routes decision to IDescriptorActivationRequestService:
        - Approval → ApproveActivationRequestAsync(reviewDecision)
        - Rejection → RejectActivationRequestAsync(reviewDecision)
        - Deferred  → no status change
```

**HumanTask 负载**：`DescriptorActivationReviewTaskInput` 包含完整的审查上下文：
- `ActivationRequestId` — 激活请求 ID
- `DraftId` — 草稿 ID
- `DescriptorKind` — 描述符种类
- `ReviewSummary` — 审查摘要
- `EvidenceSummary` — 证据摘要
- `BoundHashes` — 可选的绑定哈希
- `PackageManifestSummary` — 包清单摘要
- `ImpactContext` — 影响上下文
- `PackageManifestJson` — 包清单 JSON

### 12.3 Fail-Closed 守卫

所有激活入口点强制执行 fail-closed 空值检查：

| 守卫点 | 诊断码 | 说明 |
|--------|--------|------|
| SubmitAsync — BindingSnapshot 为 null | `ACTIVATION_BINDING_SNAPSHOT_REQUIRED` | 提交请求缺少绑定快照 |
| SubmitAsync — Hashes 为 null | `ACTIVATION_BINDING_HASHES_REQUIRED` | 绑定快照缺少哈希值 |
| SubmitAsync — PackagePreviewId 为空 | `ACTIVATION_INCOMPLETE_BINDING` | 包预览 ID 缺失（次级 fail-closed） |
| SubmitAsync — EvidencePreviewId 为空 | `ACTIVATION_INCOMPLETE_EVIDENCE_BINDING` | 证据预览 ID 缺失 |
| ApproveAsync — request.Id 不匹配 | `ACTIVATION_REVIEW_REQUEST_MISMATCH` | 审查决策引用了错误的激活请求 |
| ApproveAsync — 决策种类不匹配 | `ACTIVATION_REVIEW_DECISION_MISMATCH` | 审查决策种类不是 Approval |
| ApproveAsync — EvidenceHash 不匹配 | `ACTIVATION_REVIEW_EVIDENCE_MISMATCH` | 审批时证据哈希漂移 |
| ApproveAsync — EnvelopeHash 不匹配 | `ACTIVATION_REVIEW_ENVELOPE_MISMATCH` | 审批时信封哈希漂移 |
| RejectAsync — 状态不允许拒绝 | `ACTIVATION_INVALID_STATUS_FOR_REJECTION` | 请求状态不允许拒绝操作 |

### 12.4 关键架构决策

**1. IRuntimeActivationGate 是唯一运行时状态变异入口**

架构不变量：除 `IRuntimeActivationGate` 外，没有其他代码路径可以修改运行时描述符注册表。这确保了所有激活都经过统一的审计、验证和治理管道。

**2. 证据重校验使用完整 CanonicalHash 记录相等性**

`IActivationEvidenceRechecker` 比较 `BindingHashes` 的全部 7 个字段（SourceReviewHash、ReviewManifestHash、PackageManifestHash、PackageEvidenceHash、PackageEvidenceEnvelopeHash、ContractHash、DefinitionHash），使用 `CanonicalHash` 记录相等性而非仅比较 `.Value` 摘要。这防止了哈希摘要碰撞导致的误判。

**3. 治理决策从审查结果流向激活请求**

`SubmitActivationRequestAsync` 从审查结果中提取 `reviewRef.Review.GovernanceDecision?.MaxDecision`，并通过 `SubmitActivationRequestRequest.GovernanceDecision` 字段传递给 `RequestService`。这确保了治理决策在激活请求创建时被快照固化。

**4. 绑定完整性由编译期 + 运行时共同强制执行**

- 编译期：`ActivationBindingSnapshot.PackagePreviewId` 和 `EvidencePreviewId` 为 `required string`
- 运行时：`IsNullOrWhiteSpace` 检查 + `ACTIVATION_INCOMPLETE_BINDING` 次级 fail-closed 诊断

**5. 审批绑定到证据快照**

`ApproveActivationRequestAsync` 验证审查决策中的 `BoundEvidenceHash` 和 `BoundEnvelopeHash` 必须与激活请求快照中的 `Hashes.EvidenceHash` 和 `Hashes.EnvelopeHash` 匹配。这防止了在审查期间证据被篡改。

**6. 审批/拒绝均验证 ActivationRequestId**

两条路径均检查 `reviewDecision.ActivationRequestId == request.RequestId`，防止决策被错误路由到不相关的激活请求。

**7. 自我审批使用快照策略**

`request.Policy` 在激活请求创建时捕获自 `DescriptorActivationPolicy`，后续的自我审批检查使用此快照而非实时查询。回退使用 `snapshot.Owner.DescriptorKind`。

**8. 审计动作语义区分**

| 审计动作 | 含义 | 
|----------|------|
| `Submit` | 提交激活请求 |
| `Approve` | 人工审批通过 |
| `Reject` | 人工拒绝 |
| `Cancel` | 请求者取消 |
| `Block` | 被框架守卫阻止（非人工决策） |
| `GateDenied` | 被 RuntimeActivationGate 拒绝 |
| `Expire` | 激活请求过期 |

**9. ToolService 委托给 RequestService**

ToolService 不维护独立的激活逻辑。所有激活操作通过 `_requestService` 执行，避免双轨实现。

**10. ContractVersion 升级**

合约版本从 `"7d.v1"` 升级至 `"7e.v1"`：`AgentControlPlaneContractVersion.Current = "7e.v1"`。

### 12.5 激活请求生命周期

```
Submitted ──→ UnderReview ──→ Approved ──→ [Runtime Activation via Gate]
   │              │                │
   │              ├──→ Rejected    │
   │              │                │
   ├──→ Cancelled ├──→ Expired    │
   │                                │
   └──→ Expired                     │
```

状态转换规则：
- **Submitted** → UnderReview（HumanTask 创建后自动转换）
- **UnderReview** → Approved（审查者批准）
- **UnderReview** → Rejected（审查者拒绝）
- **Submitted / UnderReview** → Cancelled（请求者取消）
- **Submitted** → Expired（超时未进入审查）
- **UnderReview** → Expired（HumanTask 超时）

### 12.6 新增类型总览

#### Abstractions/Activation/ — 15 个契约类型

| 类型 | 说明 |
|------|------|
| `ActivationRequest` | 主激活请求记录：RequestId、DraftId、TenantId、Status、BindingSnapshot、Policy、CreatedAt、GovernanceDecision |
| `ActivationBindingSnapshot` | 绑定引用与哈希快照，`required` 字段：ReviewResultId、DraftVersion、PackagePreviewId、EvidencePreviewId、Hashes |
| `BindingHashes` | 7 个 CanonicalHash：SourceReviewHash、ReviewManifestHash、PackageManifestHash、PackageEvidenceHash、PackageEvidenceEnvelopeHash、ContractHash、DefinitionHash；PackageHashes 便捷访问器 |
| `ActivationRequestStatus` | 6 值枚举：Submitted、UnderReview、Approved、Rejected、Cancelled、Expired |
| `DescriptorActivationReviewDecision` | 审查决策：ActivationRequestId、ActorId、ActorKind、Decision、BoundEvidenceHash、BoundEnvelopeHash |
| `DescriptorActivationReviewTaskInput` | HumanTask 负载：含完整审查上下文 |
| `DescriptorActivationPolicy` | 策略快照：AllowSelfApproval、ForbidSelfApproval、[Obsolete]RequireEvidenceBinding、MaxConcurrentActivations、PolicySummary |
| `DescriptorActivationEligibility` | 派生资格：AutoActivatable、RequiresHumanReview、Blocked |
| `DescriptorActivationActorKind` | 请求者种类：Agent、Human、System |
| `DescriptorActivationAuditRecord` | 激活事件审计跟踪 |
| `DescriptorActivationAuditAction` | 7 值审计动作枚举 |
| `DescriptorActivationReviewOutcome` | 审查结果：Approved、Rejected、Deferred |
| `DescriptorActivationDecision` | 审批/拒绝决策：ActivationRequestId + ActorId + Decision + BoundEvidenceHash/EnvelopeHash |
| `SubmitActivationRequestRequest` | 提交请求 DTO，新增 GovernanceDecision 字段 |
| `DescriptorActivationReviewDecisionParser` | AoT 安全 TryParse，从 JSON 解析审查决策 |

#### ControlPlane/Activation/ — 7 个接口 + 8 个实现

| 接口 | 实现 | 说明 |
|------|------|------|
| `IDescriptorActivationRequestService` | `DefaultDescriptorActivationRequestService` | 激活请求生命周期管理 |
| `IActivationEvidenceRechecker` | `DefaultActivationEvidenceRechecker` | 证据重校验（7 字段 CanonicalHash 比较，分别校验 package hashes 和 evidence hashes） |
| `IRuntimeActivationGate` | `DefaultRuntimeActivationGate` | 唯一运行时状态变异入口（架构不变量） |
| `IActivationReviewOrchestrator` | `DefaultActivationReviewOrchestrator` | HumanTask 审查编排 |
| `IActivationBindingArtifactResolver` | `DefaultActivationBindingArtifactResolver` | 绑定引用哈希解析（StorePackageHashes + StoreEvidenceHashes 分离存储） |
| `IDescriptorActivationPolicyProvider` | `DefaultDescriptorActivationPolicyProvider` | 激活策略提供者 |
| `IDescriptorActivationAuditor` | `DefaultDescriptorActivationAuditor` | 激活审计 |
| — | `DescriptorActivationReviewHumanTaskEventHandler` | EventBus 事件处理程序 |

### 12.7 测试覆盖

- **ControlPlane 测试**：471 个（含 DraftContracts + Generator + Activation + CanonicalHash + PackagePreview），覆盖完整的激活请求生命周期、证据重校验、HumanTask 回调、策略快照、审计追踪
- **Boundary 测试**：11 个，验证激活组件依赖边界
- **总测试数**：482 个

### 12.8 Phase 7e.1 — Canonical Evidence Hashing 消费边界

Phase 7e.1 将激活工作流中的哈希生产与验证迁移到 canonical hash 基础设施，替换了 ad-hoc SHA256 管道拼接。

#### 12.8.1 BindingHashes 7-Slot 模型

`BindingHashes` 从 6-slot 升级为 7 个 flat CanonicalHash slot，加上 `PackageHashes` 便捷访问器：

| Slot | ArtifactKind | Purpose | 来源 |
|------|-------------|---------|------|
| SourceReviewHash | ReviewResult | SourceBinding | IDescriptorDraftReviewHashService |
| ReviewManifestHash | ReviewResult | Integrity | IDescriptorDraftReviewHashService |
| PackageManifestHash | PackageManifest | Integrity | IDescriptorPackageCanonicalHashComputer |
| PackageEvidenceHash | PackageEvidence | AuditEvidence | IDescriptorPackageCanonicalHashComputer |
| PackageEvidenceEnvelopeHash | PackageEvidenceEnvelope | AuditEvidence | IDescriptorPackageCanonicalHashComputer |
| ContractHash | Descriptor | Contract | IDescriptorStableHashBuilder |
| DefinitionHash | Descriptor | Definition | IDescriptorStableHashBuilder |

`PackageHashes` 便捷访问器从 3 个 package slot 构建 `DescriptorPackageHashSet`，保证原子性。

#### 12.8.2 ActivationBindingHashValidator

`ActivationBindingHashValidator` 在 3 个激活入口点执行验证（Submit、Recheck、Gate）：

- **Per-slot ArtifactKind + Purpose 验证**：每个 slot 有期望的 ArtifactKind 和 Purpose 元数据
- **Scope 验证**：所有 slot 必须为 `InternalFull`
- **Mandatory metadata 验证**：Algorithm、AlgorithmVersion、ContractVersion、CanonicalShapeVersion 非空
- **一致性验证**：AlgorithmVersion/ContractVersion 跨所有 hash 一致

#### 12.8.3 IActivationBindingArtifactResolver 拆分

`IActivationBindingArtifactResolver` 分离存储 package 和 evidence 哈希：

- `StorePackageHashes(tenantId, previewId, DescriptorPackageHashSet)` — 存储包预览哈希
- `StoreEvidenceHashes(tenantId, evidencePreviewId, DescriptorPackageHashSet)` — 存储证据预览哈希
- `ResolvedBindingArtifacts` 携带 `CurrentPackageHashes` 和 `CurrentEvidenceHashes`

重校验器独立比较 package hashes（PackageManifestHash）和 evidence hashes（EvidenceHash、EvidenceEnvelopeHash），防止 stale preview 交叉接受。

#### 12.8.4 Package Preview Reuse Identity

`BuildPackageEvidencePreviewAsync` 实现双路径重用：

- **Path A（重用）**：当 `_latestPackageByDraft` 中存在 `(TenantId, DraftId, ScopeFingerprint)` 匹配项，且 `DraftVersion` 和 `VisibleDescriptorSetHash` 匹配时，直接重用已有 package preview
- **Path B（新建）**：当无匹配预览时，单次 `_packageBuilder.Build(...)` 同时创建 package preview 和 evidence preview snapshot，存储相同的 `DescriptorPackageHashSet`

**ScopeFingerprint**：从 `AgentDescriptorVisibilityScope`（Mode + AllowedKinds + DeniedKinds）计算确定性指纹。确保不同可见性范围的预览不会交叉重用。

**VisibleDescriptorSetHash**：从 `universe.VisibleDescriptors`（catalog identity，非 proposed inventory）计算 length-prefixed encoding hash（`{len}:{FullId}:{(int)Kind}:{version}`）。确保 catalog 变化时预览不重用。Draft 变化由 `DraftVersion` 覆盖。

**_latestPackageByDraft**：键为 `(TenantId, DraftId, ScopeFingerprint)`，允许多个 scope 并行缓存各自的预览。

#### 12.8.5 Canonical Hash Writers 规范

所有 5 个 hand-written canonical hash writer 使用 **PascalCase field names via `nameof()`**：

- `DescriptorPackageManifestCanonicalHashWriter` — PackageManifest
- `DescriptorPackageEvidenceCanonicalHashWriter` — PackageEvidence
- `DescriptorPackageEvidenceEnvelopeCanonicalHashWriter` — PackageEvidenceEnvelope
- `ReviewResultSourceBindingCanonicalHashWriter` — ReviewResult/SourceBinding
- `ReviewResultIntegrityCanonicalHashWriter` — ReviewResult/Integrity

与 SG-generated writer 约定一致。元数据（ArtifactKind、Purpose、Scope 等）仅附加到 `CanonicalHash` record，不参与 digest 计算。

#### 12.8.6 已移除的遗留字段

- `DescriptorManifest.ContentHash`/`EvidenceHash`/`EnvelopeHash` string 字段已完全删除
- `DescriptorPackageHashComputer`（ad-hoc string concatenation）标记 `[Obsolete]`
- `DescriptorPackage.ContentHash` 便捷属性回退到 `Hashes?.PackageManifestHash.Value ?? string.Empty`

---

## 13. Agent Memory & Context Compression Runtime (Phase 7e+)

> **Phase 7e+ (#43)** 实现了 Agent Memory & Context Compression Runtime——从对话/任务历史到压缩上下文、记忆候选提取、晋升/拒绝/替代/归档、召回与源扩展的完整链路。所有合约类型使用 CanonicalHash 标识内容身份和完整性。

### 13.1 架构概览

主链：Sanitize → Compress → ExtractCandidates → Promote → Recall → Expand → BuildAgentAuthoringContext

- **3 个项目**：`CrestCreates.Agent.Memory.Abstractions`（27 个 sealed record/enum + 11 个接口）、`CrestCreates.Agent.Memory`（11 个默认服务）、`CrestCreates.Agent.Memory.Tests`（47 个测试）
- **AgentMemoryJsonSerializerContext** — AoT 兼容，注册 19 个 Root 类型

### 13.2 合约类型总览

#### 对话/任务历史

| Type | Kind | Description |
|------|------|-------------|
| `AgentConversationRole` | enum | User/Assistant/Tool/System |
| `AgentConversationTurn` | sealed record | TurnId, Role, Content, CreatedAt, DescriptorRefs, SourceRefs, Diagnostics |
| `AgentConversationRecord` | sealed record | ConversationId, Turns, Diagnostics |
| `AgentTaskEvent` | sealed record | EventId, TaskId, EventKind, Content, CreatedAt, SourceRefs |
| `AgentTaskRecord` | sealed record | TaskId, Title, Summary?, Events, Diagnostics |

#### 压缩上下文

| Type | Kind | Description |
|------|------|-------------|
| `AgentCompressedContextBlock` | sealed record | BlockId, Content, CanonicalContentHash, SourceRefs, ApproximateCharacterCount |
| `AgentCompressedContext` | sealed record | ContextId, Blocks, Diagnostics |

#### 记忆

| Type | Kind | Description |
|------|------|-------------|
| `AgentMemoryKind` | enum | Preference/ProjectFact/Decision/Constraint/WorkflowHint/Risk |
| `AgentMemoryConfidence` | enum | Unknown/Low/Medium/High |
| `AgentMemoryStatus` | enum | Candidate/Active/Rejected/Superseded/Archived |
| `AgentMemoryCandidate` | sealed record | CandidateId, Kind, Content, CanonicalContentHash, Confidence, Tags, DescriptorRefs, SourceRefs, Status |
| `AgentMemoryItem` | sealed record | MemoryId, Kind, Content, CanonicalContentHash, PromotedAt, Confidence, Status, IsAuthoritative, Tags, SupersedesMemoryId?, SupersededByMemoryId? |
| `AgentMemoryQuery` | sealed record | TenantId, IntentText?, MemoryIds, Kinds, Tags, DescriptorRefs, VisibleDescriptorRefs, VisibleDescriptorKinds, MaxCount?, CharacterBudget?, MinimumConfidence |
| `AgentMemoryPack` | sealed record | TenantId, Memories, Diagnostics, IsAuthoritative(false), ScopeFingerprint?, VisibleMemorySetHash?, CanonicalPackHash? |

#### 源扩展与引用

| Type | Kind | Description |
|------|------|-------------|
| `AgentSourceKind` | enum | 11 values: ConversationTurn through ActivationRequest |
| `AgentContextSourceRef` | sealed record | SourceKind, TenantId, SourceId, RangeStart?, RangeEnd?, DescriptorRefs, CorrelationId?, CausationId?, CanonicalContentHash? |
| `AgentContextEvidenceRef` | sealed record | EvidenceId, EvidenceKind, TenantId, SourceRefs, CanonicalContentHash? |
| `AgentSourceExpansionStatus` | enum | Expanded/NotExpandable/ExternalSourceNotSupported/NotFound/Redacted |
| `AgentSourceExpansionResult` | sealed record | SourceRef, Status, SanitizedContent?, Diagnostics |

#### 脱敏与诊断

| Type | Kind | Description |
|------|------|-------------|
| `SanitizedAgentContent` | sealed record | SanitizedContent, CanonicalContentHash, Rejected, RedactionKinds, Diagnostics |
| `AgentMemoryDiagnostic` | sealed record | Code(DiagnosticCode), Message, Severity(SeverityLevel), SourceRefs |
| `AgentMemoryOperationKind` | enum | Promote/Reject/Supersede/Archive |
| `AgentMemoryOperationRequest` | sealed record | TenantId, InvocationContext, Reason, Timestamp, SourceRefs, Explanation? |
| `AgentMemoryInvocationContext` | sealed record | TenantId, ActorId, ActorKind, AgentId?, SessionId?, CorrelationId?, CausationId?, InvocationSource?, DisplayName?, TraceAttributes |

#### Authoring Context

| Type | Kind | Description |
|------|------|-------------|
| `AgentAuthoringRequest` | sealed record | TenantId, IntentText, MemoryQuery? |
| `AgentAuthoringContext` | sealed record | Request, MetadataContextPack, MemoryPack, Diagnostics |

### 13.3 接口总览

| Interface | Method | Description |
|-----------|--------|-------------|
| `IAgentConversationStore` | SaveConversationAsync, GetConversationAsync | 对话持久化与检索 |
| `IAgentTaskHistoryStore` | SaveTaskAsync, GetTaskAsync, AppendEventAsync, ListTasksAsync | 任务历史持久化与检索 |
| `IAgentCompressedContextStore` | SaveCompressedContextAsync, GetCompressedContextAsync | 压缩上下文持久化与检索 |
| `IAgentMemoryStore` | SaveCandidateAsync, GetCandidateAsync, SaveMemoryAsync, GetMemoryAsync, ListMemoriesAsync | 记忆候选与正式记忆持久化 |
| `IAgentMemoryContentSanitizer` | Sanitize(tenantId, content, sourceRefs) | 内容脱敏：bearer token、credential、connection string、long base64 |
| `IAgentContextCompressor` | CompressConversationAsync, CompressTaskAsync | 对话/任务 → 压缩块 |
| `IAgentMemoryExtractor` | ExtractCandidatesAsync | 压缩上下文 → 记忆候选 |
| `IAgentMemoryPromotionService` | PromoteAsync, RejectAsync, SupersedeAsync, ArchiveAsync | 记忆生命周期管理 |
| `IAgentMemoryRetriever` | RecallAsync(query) | 基于查询的记忆召回，含置信度排序、字符预算、ScopeFingerprint |
| `IAgentContextSourceExpander` | ExpandAsync(sourceRef) | 按 SourceKind 分发到对应 Store 扩展 |
| `IAgentAuthoringContextBuilder` | BuildAsync(request, metadataContextPack, memoryPack, ct) | 组装 AuthoringContext |

### 13.4 关键架构决策

1. **AgentMemoryPack.IsAuthoritative 始终为 false** — 元数据优先于冲突记忆。Agent 不应将记忆视为权威真相。
2. **CanonicalHash 贯穿所有合约** — `AgentContextSourceRef.CanonicalContentHash`、`SanitizedAgentContent.CanonicalContentHash`、`AgentCompressedContextBlock.CanonicalContentHash`、`AgentMemoryCandidate.CanonicalContentHash`、`AgentMemoryItem.CanonicalContentHash`、`AgentMemoryPack.CanonicalPackHash/ScopeFingerprint/VisibleMemorySetHash` 全部使用 `CanonicalHash` 类型。
3. **Snapshot-on-read 防御性拷贝** — 所有 InMemory Store 在读写时执行深拷贝，防止外部代码修改内部状态。
4. **DI 使用 TryAddSingleton + TimeProvider.System** — 允许测试替换，默认使用系统时间。
5. **依赖边界** — Memory.Abstractions 不引用 ControlPlane.Abstractions 或 Core.Abstractions。Memory.Abstractions 引用 Metadata.Abstractions、Metadata.ContextPack.Abstractions。
6. **VisibleDescriptorKinds fail-closed** — 当 `AgentMemoryQuery.VisibleDescriptorKinds` 包含无法解析的值时，召回返回空结果而非暴露不可见记忆。
7. **AgentMemoryInvocationContext 替代 AgentActorContext** — 使用更完整的调用身份（TenantId, ActorId, ActorKind, AgentId, SessionId, CorrelationId, CausationId, InvocationSource, DisplayName, TraceAttributes），与 Agent.Abstractions 对齐。
8. **IAgentAuthoringContextBuilder.BuildAsync 三参数签名** — `(AgentAuthoringRequest, MetadataContextPack, AgentMemoryPack)` — 不内部调用 retriever；调用者传入预构建的 MemoryPack。

### 13.5 主链数据流

```
AgentConversationTurn / AgentTaskEvent
    → IAgentMemoryContentSanitizer.Sanitize(content, sourceRefs)
    → SanitizedAgentContent (脱敏后内容 + CanonicalContentHash)
    → IAgentContextCompressor.CompressConversationAsync / CompressTaskAsync
    → AgentCompressedContext (压缩块列表)
    → IAgentMemoryExtractor.ExtractCandidatesAsync
    → AgentMemoryCandidate[] (Kind=ProjectFact, Confidence=Low)
    → IAgentMemoryPromotionService.PromoteAsync / RejectAsync / SupersedeAsync / ArchiveAsync
    → AgentMemoryItem (正式记忆)

AgentMemoryQuery (IntentText, Kinds, Tags, DescriptorRefs, VisibleDescriptorRefs, CharacterBudget)
    → IAgentMemoryRetriever.RecallAsync(query)
    → AgentMemoryPack (IsAuthoritative=false, ScopeFingerprint, VisibleMemorySetHash, CanonicalPackHash)

AgentContextSourceRef
    → IAgentContextSourceExpander.ExpandAsync(sourceRef)
    → AgentSourceExpansionResult (Expanded/NotExpandable/NotFound/Redacted)

AgentAuthoringRequest + MetadataContextPack + AgentMemoryPack
    → IAgentAuthoringContextBuilder.BuildAsync(request, pack, memoryPack, ct)
    → AgentAuthoringContext (Request, MetadataContextPack, MemoryPack, Diagnostics)
```

### 13.6 DI 注册

```csharp
services.AddAgentMemoryRuntime();
// 等价于：
services.TryAddSingleton<IAgentConversationStore, InMemoryAgentConversationStore>();
services.TryAddSingleton<IAgentTaskHistoryStore, InMemoryAgentTaskHistoryStore>();
services.TryAddSingleton<IAgentCompressedContextStore, InMemoryAgentCompressedContextStore>();
services.TryAddSingleton<IAgentMemoryStore, InMemoryAgentMemoryStore>();
services.TryAddSingleton<IAgentMemoryContentSanitizer, DefaultAgentMemoryContentSanitizer>();
services.TryAddSingleton<IAgentContextCompressor, DefaultAgentContextCompressor>();
services.TryAddSingleton<IAgentMemoryExtractor, DefaultAgentMemoryExtractor>();
services.TryAddSingleton<IAgentMemoryPromotionService, DefaultAgentMemoryPromotionService>();
services.TryAddSingleton<IAgentMemoryRetriever, DefaultAgentMemoryRetriever>();
services.TryAddSingleton<IAgentContextSourceExpander, DefaultAgentContextSourceExpander>();
services.TryAddSingleton<IAgentAuthoringContextBuilder, DefaultAgentAuthoringContextBuilder>();
services.TryAddSingleton<AgentMemoryCanonicalHashProjector>();
```

### 13.7 测试覆盖

- **Memory 主链测试**：39 个（MainChainTests）
- **Memory 边界测试**：2 个（BoundaryTests — 依赖边界检查）
- **Memory 合约测试**：6 个（ContractTests — enum shape, JSON serialization, collection immutability）
- **总测试数**：47 个

### 13.8 Canonical Hash Shape Versions

| 常量 | 值 | 用途 |
|------|-----|------|
| `MemoryContentV1` | `"memory-content-hash-v1"` | AgentMemoryItem/AgentMemoryCandidate 内容哈希 |
| `MemoryPackV1` | `"memory-pack-hash-v1"` | AgentMemoryPack 整体哈希 |
| `MemoryScopeV1` | `"memory-scope-hash-v1"` | AgentMemoryPack 范围指纹 |
| `MemorySetV1` | `"memory-set-hash-v1"` | AgentMemoryPack 可见记忆集合哈希 |

---

## 14. AI-assisted Descriptor Authoring Golden Scenario (Phase 7f)

> **Phase 7f (#32)** 在 Phase 7e+ Agent Memory 基础之上，实现了从意图文本到描述符草稿创作、审查、治理、激活绑定、运行时证明的完整端到端链路。核心组件均位于 sample 项目中，不修改框架核心合约。

### 14.1 架构概览

核心链路：

```
Intent Text
  → IAgentAuthoringContextBuilder.BuildAsync (MetadataContextPack + AgentMemoryPack)
  → AgentAuthoringContext
  → IDescriptorAuthoringAgent.AuthorAsync(authoringContext, ct)
  → DescriptorDraftSet (all-or-block)
  → Per-draft Review / Materialization
  → Final Inventory Diff → Final Impact / Compatibility / Governance
  → Activation Binding (real hashes from IDescriptorDraftReviewHashService + IDescriptorPackageBuilder + IDescriptorStableHashBuilder)
  → IRuntimeActivationGate
  → Fresh Host from Approved Final Inventory → Runtime Proof
```

### 14.2 组件归属

| 项目 | 内容 |
|------|------|
| `samples/CrestCreates.Samples.DescriptorControlPlane/Authoring` | IDescriptorAuthoringAgent、FakeCompanyCertificationAuthoringAgent、CompanyCertificationAuthoringGoldenScenarioRunner/Report/DraftSetReviewResult、ActivationBindingReferenceRegistry |
| `tests/Framework/Testing/CrestCreates.Samples.Tests` | 21 个 Authoring Golden Scenario 测试 + 7 个 ControlPlane Golden Scenario 测试 |

### 14.3 关键合约类型

| Type | Kind | Description |
|------|------|-------------|
| `IDescriptorAuthoringAgent` | interface | `Task<DescriptorAuthoringResult> AuthorAsync(AgentAuthoringContext, CancellationToken)` |
| `DescriptorAuthoringResult` | sealed record | 创作结果，实现 ISnapshotable |
| `DescriptorDraftSet` | sealed record | 包装 `IReadOnlyList<DescriptorDraft>`，原子性 |
| `FakeCompanyCertificationAuthoringAgent` | sealed class | 确定性假 Agent，无构造函数依赖，仅消费 AgentAuthoringContext |
| `CompanyCertificationAuthoringGoldenScenarioRunner` | sealed class | 三方法编排：RunUntilDraftSetReviewAsync / RunAsync / RunActivationOnlyAsync |
| `CompanyCertificationAuthoringGoldenScenarioReport` | sealed record | 完整报告：AuthoringSucceeded, DraftSetBlocked, FinalDecisionSource, RuntimeActivationGateSucceeded, RuntimeProofUsedFreshActivatedHost, etc. |
| `CompanyCertificationDraftSetReviewResult` | sealed record | DraftSet, PerDraftReviewResults, FinalProposedInventory, IsBlocked, FinalDecisionSource, FinalTopology, FinalGovernance, FinalImpact, FinalCompat |
| `ActivationBindingReferenceRegistry` | sealed class | 在创建点注册 review/package/evidence 引用 + DraftId；激活前只读验证 |
| `BindingReferenceValidationResult` | sealed record | IsValid, Errors |

### 14.4 关键架构决策

1. **FakeAgent 无构造函数依赖** — `FakeCompanyCertificationAuthoringAgent` 仅消费 `AgentAuthoringContext`（含 Request.TenantId, Request.IntentText），不注入任何服务，不访问 raw memory stores 或 runtime handlers。

2. **Draft set 原子性** — 全部 draft 创建成功或全部 block。Materialization 失败 → IsBlocked = true。

3. **Final decision 基于 inventory diff** — Final scenario-level decision 使用 `IDescriptorChangeSetBuilder.Build(startingInventory, finalProposedInventory)` → `IDescriptorImpactAnalyzer.Analyze(topology, changeSet)` → `IDescriptorCompatibilityAnalyzer.Analyze(before, after, changeSet, impactReport)`，不取最后一个 draft review 的 impact/compat 结果。

4. **激活绑定使用真实 hash** — SourceReviewHash/ReviewManifestHash 通过 `IDescriptorDraftReviewHashService` 计算；PackageManifestHash/PackageEvidenceHash/PackageEvidenceEnvelopeHash 通过 `IDescriptorPackageBuilder.Build()` + `IDescriptorPackageCanonicalHashComputer.ComputeHashSet()` 计算；ContractHash/DefinitionHash 通过 `IDescriptorStableHashBuilder.Build()` 计算。无 placeholder fallback，缺 hash 即 block。

5. **绑定引用注册在创建点** — `ActivationBindingReferenceRegistry.RegisterReviewResult/RegisterPackagePreview/RegisterEvidencePreview` 在 artifact 创建时调用；激活前 `ValidateReferences` 只读验证，检查存在性和 DraftId 匹配。等价于 Control Plane 内部 `_reviewResults`/`_packagePreviews`/`_evidencePreviews` 字典 + DraftId mismatch 校验。

6. **运行时证明用 fresh host** — 从 approved final inventory 构建新 `CompanyCertificationGoldenScenarioHost`，不使用原始 host。证明激活后的描述符在独立 runtime 中可执行。

7. **AgentMemoryPack.IsAuthoritative 始终为 false** — 当记忆与元数据冲突时，元数据优先。Agent 不应将记忆视为权威真相。

8. **CreatedAt 使用固定时间** — `DescriptorPackageBuildRequest.CreatedAt` 使用 `GoldenScenarioCreatedAt`（2026-01-01T00:00:00Z），确保 evidence binding hash 确定性。

9. **HumanTask 完成去重** — 使用 `completedHumanTaskInstanceIds` HashSet 防止重复完成同一 HumanTask。

10. **不修改框架核心合约** — Phase 7f 所有新增类型均在 sample 项目中，`CompanyCertificationGoldenScenarioHost` 新增接受 `IReadOnlyList<IDescriptor>` 的构造函数重载，不改变已有接口。

### 14.5 Runner 方法说明

| 方法 | 输入 | 输出 | 范围 |
|------|------|------|------|
| `RunUntilDraftSetReviewAsync` | intentText, startingInventory?, ct | CompanyCertificationDraftSetReviewResult | 意图→创作→per-draft审查→final inventory diff→final governance |
| `RunAsync` | intentText, ct | CompanyCertificationAuthoringGoldenScenarioReport | 完整链路：+ 激活绑定 + runtime gate + fresh host 运行时证明 |
| `RunActivationOnlyAsync` | intentText, ct | CompanyCertificationAuthoringGoldenScenarioReport | 到激活门为止，不构建 fresh host |

### 14.6 测试覆盖

21 个 Authoring Golden Scenario 测试：

| 类别 | 测试数 | 覆盖范围 |
|------|--------|---------|
| Fake Agent 确定性 + 约束 | 5 | 输出确定性、不使用 raw memory stores、不访问 runtime handlers、IsAuthoritative=false、实现框架接口 |
| Draft Set 原子性 | 4 | HumanTask 创建、Workflow 更新、sequential materialization、all-or-block (materialization fail / incomplete inventory / valid drafts) |
| Final Decision | 2 | Final decision rechecks complete inventory、final impact/compat from inventory diff |
| 激活绑定 | 2 | Activation request binds final review + package evidence hashes、activation gate alone ≠ runtime proof |
| 运行时证明 | 2 | Fresh host from approved inventory、completes initial review then finance review |
| 内存边界 | 2 | Memory is non-authoritative、metadata wins when memory conflicts |
| 端到端 | 1 | Full authoring → activated runtime golden scenario |
| Review Pipeline | 1 | LLM agent golden scenario drafts flow through review pipeline |

加上 7 个 ControlPlane Golden Scenario 测试（baseline、happy path、approval event、breaking schema、missing target、explicit inventory），总计 28 个测试。
