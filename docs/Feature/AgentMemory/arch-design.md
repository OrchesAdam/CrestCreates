# Agent Memory & Context Compression Runtime — Architecture Design

> **Date:** 2026-06-29 | **Status:** Implemented | **Phase 7e+ (#43)**

## 1. 概述 (Overview)

Phase 7e+ 实现了 Agent Memory & Context Compression Runtime，为 LLM Bootstrap Plane 提供确定的、可溯源的非权威性上下文基础设施。

核心管线：

```text
SaveConversation / SaveTask
  → Sanitize (脱敏 / 拒绝)
  → Compress (压缩为上下文块)
  → ExtractCandidates (从压缩上下文提取记忆候选)
  → Promote (显式升级为持久记忆)
  → Recall (预算内召回)
  → BuildAuthoringContext (组装最终创作上下文)
```

Agent Memory 是**上下文基础设施**，不是 Agent 执行引擎、治理权威、激活权威、工具平台或聊天框架。记忆始终是非权威性的（`IsAuthoritative = false`），与 `MetadataContextPack` 冲突时记忆让步。

### 1.1 目标

| 目标 | 说明 |
|------|------|
| 上下文管道 | 提供从原始对话/任务输入到 Agent 创作上下文的完整管道 |
| 确定性 | 压缩、提取、召回均为确定性操作，无 LLM 依赖 |
| 脱敏优先 | 所有敏感内容在存储和压缩前被脱敏或拒绝 |
| 溯源能力 | 每条记忆通过 `AgentContextSourceRef` 回溯到原始来源 |
| 非权威标记 | 所有召回的记忆明确标记为非权威性上下文 |
| AoT 安全 | 所有 DTO 是密封 record，JSON 序列化使用 Source Generator |

### 1.2 交付物

- **20 个密封 record DTO** — AgentConversationTurn、AgentTaskRecord、AgentCompressedContextBlock、AgentMemoryCandidate、AgentMemoryItem、AgentMemoryQuery、AgentMemoryPack、AgentAuthoringContext 等
- **7 个枚举类型** — AgentMemoryConfidence、AgentMemoryStatus、AgentMemoryKind、AgentConversationRole、AgentSourceKind、AgentMemorySourceExpansionStatus、AgentMemoryOperationKind
- **11 个服务接口** — Store（4 个）、Sanitizer、Compressor、Extractor、PromotionService、Retriever、SourceExpander、AuthoringContextBuilder
- **11 个默认实现** — 4 个 InMemory Store + 7 个 Default 服务
- **Source-Generated JSON Context** — `AgentMemoryJsonSerializerContext`，注册 18 个类型，AoT 兼容
- **13 个诊断码** — 含 5 个脱敏种类常量
- **DI 注册** — `AddAgentMemoryRuntime()`，TryAddSingleton 模式

---

## 2. 在框架中的位置 (Position in the Framework)

```
Agent Control Plane (Phase 7a-e)
        ↑ (未来阶段: Memory 通过 Control Plane Tool 暴露给 Agent)
        │
┌─────────────────────────────────────────────┐
│   Agent Memory Runtime (Phase 7e+)          │  ← THIS DOCUMENT
│   ┌───────────────────────────────────────┐  │
│   │  Agent.Memory.Abstractions            │  │
│   │  (Contracts, Interfaces, JSON Ctx)     │  │
│   └───────────────────────────────────────┘  │
│   ┌───────────────────────────────────────┐  │
│   │  Agent.Memory (Default Implementations)│  │
│   │  (Compression, Extraction, Promotion,  │  │
│   │   Recall, Sanitization, Expansion,     │  │
│   │   Authoring, InMemory Stores)          │  │
│   └───────────────────────────────────────┘  │
└─────────────────────────────────────────────┘
        ↓                               ↓
┌──────────────┐              ┌─────────────────────┐
│   Metadata   │              │   Core.Abstractions  │
│   (Context   │              │   (DiagnosticCode,   │
│   Pack)      │              │    SeverityLevel)    │
└──────────────┘              └─────────────────────┘
```

### 2.1 依赖链

```
CrestCreates.Agent.Memory.Abstractions
  → CrestCreates.Core.Abstractions        (DiagnosticCode, SeverityLevel)
  → CrestCreates.Metadata.Abstractions    (DescriptorRef, DescriptorKind)
  → CrestCreates.Metadata.ContextPack.Abstractions  (MetadataContextPack)
  → CrestCreates.Agent.Abstractions

CrestCreates.Agent.Memory
  → CrestCreates.Agent.Memory.Abstractions
  → Microsoft.Extensions.DependencyInjection.Abstractions
```

### 2.2 项目归属

| 项目 | 内容 |
|------|------|
| `CrestCreates.Agent.Memory.Abstractions` | 所有 contracts（records、enums、interfaces）、JSON 序列化上下文、诊断码 |
| `CrestCreates.Agent.Memory` | 所有默认实现（Sanitizer、Compressor、Extractor、PromotionService、Retriever、SourceExpander、AuthoringContextBuilder）、4 个 InMemory Store、DI 扩展方法 |
| `CrestCreates.Agent.Memory.Tests` | MainChainTests、ContractTests、BoundaryTests |

---

## 3. 架构 (Architecture)

### 3.1 组件图

```
┌──────────────────────────────────────────────────────────────────┐
│                    Agent Memory Runtime                            │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  存储层 (Stores)                                          │    │
│  │  ┌─────────────────────┐ ┌─────────────────────┐         │    │
│  │  │ IAgentConversation  │ │ IAgentTaskHistory   │         │    │
│  │  │ Store               │ │ Store               │         │    │
│  │  │ InMemoryImpl        │ │ InMemoryImpl        │         │    │
│  │  └─────────┬───────────┘ └─────────┬───────────┘         │    │
│  │  ┌─────────┴───────────────────────┴───────────┐         │    │
│  │  │ IAgentCompressedContextStore                │         │    │
│  │  │ InMemoryAgentCompressedContextStore         │         │    │
│  │  └─────────┬───────────────────────────────────┘         │    │
│  │  ┌─────────┴───────────┐                                 │    │
│  │  │ IAgentMemoryStore   │                                 │    │
│  │  │ InMemoryImpl        │                                 │    │
│  │  └─────────────────────┘                                 │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  管线服务 (Pipeline Services)                              │    │
│  │                                                            │    │
│  │  IAgentMemoryContentSanitizer → SanitizedAgentContent     │    │
│  │          ↓                                                 │    │
│  │  IAgentContextCompressor → AgentCompressedContext          │    │
│  │          ↓                                                 │    │
│  │  IAgentMemoryExtractor → AgentMemoryCandidate[]            │    │
│  │          ↓                                                 │    │
│  │  IAgentMemoryPromotionService → AgentMemoryItem            │    │
│  │          ↓                                                 │    │
│  │  IAgentMemoryRetriever → AgentMemoryPack                   │    │
│  │          ↓                                                 │    │
│  │  IAgentAuthoringContextBuilder → AgentAuthoringContext     │    │
│  └──────────────────────────────────────────────────────────┘    │
│                                                                   │
│  ┌──────────────────────────────────────────────────────────┐    │
│  │  辅助服务                                                  │    │
│  │  IAgentContextSourceExpander → AgentSourceExpansionResult │    │
│  └──────────────────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────────────────┘
```

### 3.2 数据流

```text
Raw conversation/task input
  → [Sanitize] 脱敏/拒绝敏感内容
    → SanitizedAgentContent (with CanonicalContentHash)
  → [Store] ConversationStore / TaskHistoryStore 存储已脱敏内容
  → [Compress] 对话/任务 → AgentCompressedContext
    → 每个 turn/event → AgentCompressedContextBlock
    → Block 携带 SourceRefs（含 RangeStart/RangeEnd）
    → 被拒绝的块被跳过（ContentRejected 诊断）
  → [ExtractCandidates] 压缩块 → AgentMemoryCandidate[]
    → 每个 Block → 一个 Candidate（Kind=ProjectFact, Confidence=Low）
  → [Promote] Candidate → AgentMemoryItem（需 ActorContext、Reason、Timestamp）
    → IsAuthoritative = false
    → 同步更新 Candidate.Status → Active
  → [Recall] AgentMemoryQuery → AgentMemoryPack
    → Store-level 过滤: Tenant、Status、Kinds、Tags、DescriptorRefs
    → Recall-level 过滤: Confidence、MaxCount、CharacterBudget
    → 确定性排序 → BudgetTruncated 诊断
    → IsAuthoritative = false
  → [BuildAuthoringContext] AuthoringRequest + MetadataContextPack + MemoryPack
    → AgentAuthoringContext
```

---

## 4. 关键组件 (Key Components)

### 4.1 枚举类型

| 枚举 | 所在项目 | 值 | 状态 |
|------|---------|-----|------|
| `AgentSourceKind` | Abstractions | ConversationTurn=0, TaskRecord=1, TaskEvent=2, CompressedContextBlock=3, MemoryCandidate=4, MemoryItem=5, MetadataContextPack=6, ReviewReport=7, FixProposal=8, PackagePreview=9, ActivationRequest=10 | **Implemented** |
| `AgentMemoryConfidence` | Abstractions | Unknown=0, Low=1, Medium=2, High=3 | **Implemented** |
| `AgentMemoryStatus` | Abstractions | Candidate=0, Active=1, Rejected=2, Superseded=3, Archived=4 | **Implemented** |
| `AgentMemoryKind` | Abstractions | Preference=0, ProjectFact=1, Decision=2, Constraint=3, WorkflowHint=4, Risk=5 | **Implemented** |
| `AgentConversationRole` | Abstractions | User=0, Assistant=1, Tool=2, System=3 | **Implemented** |
| `AgentMemorySourceExpansionStatus` | Abstractions | Expanded=0, NotExpandable=1, ExternalSourceNotSupported=2, NotFound=3, Redacted=4 | **Implemented** |
| `AgentMemoryOperationKind` | Abstractions | Promote=0, Reject=1, Supersede=2, Archive=3 | **Implemented** |

### 4.2 契约类型（Records）

| 类型 | 所在项目 | 职责 | 状态 |
|------|---------|------|------|
| `AgentContextSourceRef` | Abstractions | 溯源引用：SourceKind、TenantId、SourceId、RangeStart/RangeEnd、DescriptorRefs、CorrelationId、CausationId、CanonicalContentHash | **Implemented** |
| `AgentContextEvidenceRef` | Abstractions | 上下文证据引用：EvidenceId、EvidenceKind、TenantId、SourceRefs、CanonicalContentHash | **Implemented** |
| `AgentMemoryDiagnostic` | Abstractions | 诊断：Code、Message、Severity、SourceRefs | **Implemented** |
| `AgentActorContext` | Abstractions | 操作者上下文：ActorId、ActorKind、DisplayName | **Implemented** |
| `AgentConversationTurn` | Abstractions | 对话轮次：TurnId、TenantId、Role、Content、CreatedAt、DescriptorRefs、SourceRefs | **Implemented** |
| `AgentConversationRecord` | Abstractions | 对话记录：ConversationId、TenantId、Turns | **Implemented** |
| `AgentTaskEvent` | Abstractions | 任务事件：EventId、TenantId、TaskId、EventKind、Content、CreatedAt、SourceRefs | **Implemented** |
| `AgentTaskRecord` | Abstractions | 任务记录：TaskId、TenantId、Title、Summary、Events | **Implemented** |
| `SanitizedAgentContent` | Abstractions | 脱敏结果：SanitizedContent、CanonicalContentHash、Rejected、RedactionKinds、Diagnostics | **Implemented** |
| `AgentCompressedContextBlock` | Abstractions | 压缩上下文块：BlockId、TenantId、Content、CanonicalContentHash、SourceRefs、Diagnostics、ApproximateCharacterCount | **Implemented** |
| `AgentCompressedContext` | Abstractions | 压缩上下文：ContextId、TenantId、Blocks、Diagnostics | **Implemented** |
| `AgentMemoryCandidate` | Abstractions | 记忆候选：CandidateId、TenantId、Kind、Content、CanonicalContentHash、Confidence、Tags、DescriptorRefs、SourceRefs、Status | **Implemented** |
| `AgentMemoryItem` | Abstractions | 持久记忆：MemoryId、TenantId、Kind、Content、CanonicalContentHash、PromotedAt、Confidence、Status、IsAuthoritative、Tags、DescriptorRefs、SourceRefs、SupersedesMemoryId、SupersededByMemoryId | **Implemented** |
| `AgentMemoryQuery` | Abstractions | 记忆查询：TenantId、IntentText、MemoryIds、Kinds、Tags、DescriptorRefs、VisibleDescriptorRefs、VisibleDescriptorKinds、MaxCount、CharacterBudget、MinimumConfidence、IncludeStale、IncludeSuperseded、IncludeArchived、IncludeSourceRefs | **Implemented** |
| `AgentMemoryPack` | Abstractions | 记忆包：TenantId、Memories、Diagnostics、IsAuthoritative | **Implemented** |
| `AgentMemoryOperationRequest` | Abstractions | 操作请求：TenantId、Actor、Reason、Timestamp、SourceRefs、Explanation | **Implemented** |
| `AgentSourceExpansionResult` | Abstractions | 来源展开结果：SourceRef、Status、SanitizedContent、Diagnostics | **Implemented** |
| `AgentAuthoringRequest` | Abstractions | 创作请求：TenantId、IntentText、MemoryQuery | **Implemented** |
| `AgentAuthoringContext` | Abstractions | 创作上下文：Request、MetadataContextPack、MemoryPack、Diagnostics | **Implemented** |

### 4.3 服务接口

| 接口 | 所在项目 | 职责 | 状态 |
|------|---------|------|------|
| `IAgentConversationStore` | Abstractions | 对话存储：SaveConversation、GetConversation | **Implemented** |
| `IAgentTaskHistoryStore` | Abstractions | 任务历史存储：SaveTask、GetTask、AppendEvent、ListTasks | **Implemented** |
| `IAgentCompressedContextStore` | Abstractions | 压缩上下文存储：SaveCompressedContext、GetCompressedContext | **Implemented** |
| `IAgentMemoryStore` | Abstractions | 记忆存储：SaveCandidate、GetCandidate、SaveMemory、GetMemory、ListMemories | **Implemented** |
| `IAgentMemoryContentSanitizer` | Abstractions | 内容脱敏：Sanitize(tenantId, content, sourceRefs) | **Implemented** |
| `IAgentContextCompressor` | Abstractions | 上下文压缩：CompressConversation、CompressTask | **Implemented** |
| `IAgentMemoryExtractor` | Abstractions | 候选提取：ExtractCandidates | **Implemented** |
| `IAgentMemoryPromotionService` | Abstractions | 升级服务：Promote、Reject、Supersede、Archive | **Implemented** |
| `IAgentMemoryRetriever` | Abstractions | 记忆检索：Recall | **Implemented** |
| `IAgentContextSourceExpander` | Abstractions | 来源展开：Expand | **Implemented** |
| `IAgentAuthoringContextBuilder` | Abstractions | 创作上下文构建：Build | **Implemented** |

### 4.4 默认实现

| 实现 | 所在项目 | 职责 | 状态 |
|------|---------|------|------|
| `DefaultAgentMemoryContentSanitizer` | Memory/Sanitization | 基于正则的脱敏：4 个 RedactionPattern，拒绝全量脱敏内容 | **Implemented** |
| `DefaultAgentContextCompressor` | Memory/Compression | 确定性压缩：合成 SourceRef、去除被拒绝块 | **Implemented** |
| `DefaultAgentMemoryExtractor` | Memory/Extraction | 从 CompressedContext 提取 Candidate（Kind=ProjectFact, Confidence=Low） | **Implemented** |
| `DefaultAgentMemoryPromotionService` | Memory/Promotion | 升级/拒绝/取代/归档：ValidateOperationRequest 执行 5 项守卫 | **Implemented** |
| `DefaultAgentMemoryRetriever` | Memory/Recall | 两层过滤：Store-level → Recall-level → CharacterBudget | **Implemented** |
| `DefaultAgentContextSourceExpander` | Memory/Recall | 按 SourceKind 展开：Conversation、Task、CompressedContext、Memory、Candidate；其余返回 NotExpandable | **Implemented** |
| `DefaultAgentAuthoringContextBuilder` | Memory/Authoring | 组装 MetadataContextPack + MemoryPack + AuthoringRequest | **Implemented** |
| `InMemoryAgentConversationStore` | Memory/Stores | ConcurrentDictionary 对话存储，脱敏后存储 | **Implemented** |
| `InMemoryAgentTaskHistoryStore` | Memory/Stores | ConcurrentDictionary 任务存储，脱敏后存储 | **Implemented** |
| `InMemoryAgentCompressedContextStore` | Memory/Stores | ConcurrentDictionary 压缩上下文存储 | **Implemented** |
| `InMemoryAgentMemoryStore` | Memory/Stores | ConcurrentDictionary 候选+记忆存储，Store-level 过滤 | **Implemented** |

---

## 5. 核心管线 (Core Pipeline)

### 5.1 Sanitize（脱敏）— 存储前 & 压缩前

`DefaultAgentMemoryContentSanitizer` 在存储和压缩前对所有内容执行脱敏：

**4 个 RedactionPattern**：

| 序号 | 正则 | 脱敏种类 | 说明 |
|------|------|---------|------|
| 0 | `(password|pwd)=...;` | `connection-credential` | 连接字符串密码段 |
| 1 | `bearer \S+` | `bearer-token` | Bearer Token |
| 2 | `(password|api_key|apikey|secret|token)=\S+` | `credential` | 密码/API 密钥赋值 |
| 3 | `[A-Za-z0-9+/]{40,}={0,2}` | `long-token` | 长 base64 令牌 |

**拒绝规则**：
- 空内容或空白字符串 → `Rejected=true`，`EmptyContent` 诊断码
- 全量脱敏（去除所有 `[REDACTED:...]` 后为空）→ `Rejected=true`，`ContentRejected` 诊断码

**输出**：`SanitizedAgentContent`（含 `CanonicalContentHash = SHA256(sanitized)`）

脱敏也在 `InMemoryAgentConversationStore` 和 `InMemoryAgentTaskHistoryStore` 的 Save 方法中执行，确保存储的内容已脱敏。

### 5.2 Compress（压缩）

`DefaultAgentContextCompressor` 将已脱敏的对话/任务转换为压缩上下文：

**CompressConversationAsync**：
- 遍历每个 ConversationTurn
- 每轮脱敏后格式化为 `[{Role}] {SanitizedContent}`
- 被拒绝的轮次 → 跳过（ContentRejected 诊断）
- 有脱敏的轮次 → BlockSanitized 诊断
- 生成 BlockId: `{ConversationId}_{TurnId}`
- **合成 SourceRef**（当 turn.SourceRefs 为空时）：
  - SourceKind = ConversationTurn, SourceId = ConversationId, RangeStart/RangeEnd = 当前索引

**CompressTaskAsync**：
- 先处理 Summary（格式化为 `[Task] {Title}: {Summary}`）
- 再遍历每个 TaskEvent（格式化为 `[{EventKind}] {SanitizedContent}`）
- 同理跳过被拒绝的事件

### 5.3 ExtractCandidates（候选提取）

`DefaultAgentMemoryExtractor` 从压缩上下文提取候选：

```csharp
CandidateId = $"candidate_{block.BlockId}"
Kind = AgentMemoryKind.ProjectFact
Confidence = AgentMemoryConfidence.Low
Status = AgentMemoryStatus.Candidate
```

默认提取器不做自动升级，不分析内容语义。

### 5.4 Promote（升级）

`DefaultAgentMemoryPromotionService` 提供 4 个操作：

| 方法 | 输入 | 输出 | 状态转换 |
|------|------|------|---------|
| `PromoteAsync` | candidateId + request | AgentMemoryItem (IsAuthoritative=false) | Candidate → Active |
| `RejectAsync` | candidateId + request | void | Candidate → Rejected |
| `SupersedeAsync` | memoryId + replacement + request | 新 AgentMemoryItem (SupersedesMemoryId) | Active → Superseded，创建新 Active |
| `ArchiveAsync` | memoryId + request | void | Active/Superseded → Archived |

**ValidateOperationRequest 5 项守卫**：

| 序号 | 检查 | 诊断码 |
|------|------|--------|
| 1 | `request.TenantId == tenantId` | `InvalidOperationTenantMismatch` |
| 2 | `request.Actor` 非空，ActorId/ActorKind 非空 | `InvalidOperationMissingActor` |
| 3 | `request.Reason` 非空 | `InvalidOperationMissingReason` |
| 4 | `request.Timestamp != default` | `InvalidOperationMissingTimestamp` |
| 5 | `request.SourceRefs.Count > 0` 或 `request.Explanation` 非空 | `InvalidOperationMissingSourceOrExplanation` |

### 5.5 Recall（召回）

`DefaultAgentMemoryRetriever` 实现两层过滤：

**Store-level 过滤**（在 `InMemoryAgentMemoryStore.ListMemoriesAsync`）：
- `TenantId` 匹配
- `Kinds` 过滤（为空则不过滤）
- `Tags` 过滤（任一匹配）
- `MemoryIds` 过滤
- `DescriptorRefs` 过滤（任一匹配）
- `Status` 过滤：Active 始终返回，Superseded/Archived 需 IncludeSuperseded/IncludeArchived，Candidate 不返回

**Recall-level 过滤**（在 `DefaultAgentMemoryRetriever.RecallAsync`）：
- `VisibleDescriptorKinds` — **Fail-closed**：DescriptorRef 不带 DescriptorKind，此过滤器始终返回空结果（`VisibilityKindUnresolvable` 诊断）
- `MinimumConfidence` 过滤（`memory.Confidence >= query.MinimumConfidence`）
- `VisibleDescriptorRefs` 过滤（`memory.DescriptorRefs` 与 `query.VisibleDescriptorRefs` 有交集）
- `MaxCount` 截断
- `CharacterBudget` 截断（超出预算时发出 `BudgetTruncated` 诊断）

**确定性排序**：
```text
Confidence desc → Kind → PromotedAt desc → MemoryId → CanonicalContentHash
```

**`IsAuthoritative` 始终为 `false`**，无论是否被截断。

### 5.6 BuildAuthoringContext（构建创作上下文）

`DefaultAgentAuthoringContextBuilder` 组装最终上下文：

```csharp
AgentAuthoringContext = AuthoringRequest + MetadataContextPack + AgentMemoryPack
```

Builder 不调用 Control Plane 工具服务、不查询描述符存储、不突变输入。

---

## 6. Store vs Recall 分离 (Store vs Recall Separation)

`AgentMemoryQuery` 被 Store 和 Retriever 共享，但两者的过滤语义严格分离：

| 层级 | 服务 | 过滤字段 | 语义 |
|------|------|---------|------|
| **Store** | `IAgentMemoryStore.ListMemoriesAsync` | TenantId, Status, Kinds, Tags, MemoryIds, DescriptorRefs | 持久化层过滤 |
| **Recall** | `IAgentMemoryRetriever.RecallAsync` | Confidence, MaxCount, CharacterBudget, VisibleDescriptorRefs, VisibleDescriptorKinds | 召回层过滤 |

Store 不解释 recall 级字段（如 MinimumConfidence、CharacterBudget），Retriever 在 store 结果之上应用 recall 级过滤。

---

## 7. SourceRef 协议 (SourceRef Protocol)

`AgentContextSourceRef` 是溯源的唯一载体。SourceRef 协议定义了如何从压缩块回溯到原始来源：

| 字段 | 说明 |
|------|------|
| `SourceKind` | 来源类型（ConversationTurn、TaskRecord、TaskEvent 等） |
| `TenantId` | 租户隔离 |
| `SourceId` | 实体 ID（如 conversationId 或 taskId） |
| `RangeStart`/`RangeEnd` | 条目标识（如 turn 索引、event 索引） |
| `CanonicalContentHash` | 脱敏后内容哈希，用于完整性验证 |

**合成 SourceRef 规则**（Compressor 中）：
- 当 turn/event 无 SourceRefs 时，Compressor 合成 SourceRef：
  - ConversationTurn: `SourceId=ConversationId`, `RangeStart=RangeEnd=index`
  - TaskRecord: `SourceId=TaskId`, RangeStart/RangeEnd 不设置
  - TaskEvent: `SourceId=TaskId`, `RangeStart=RangeEnd=index`

**展开规则**（Expander 中）：
- `SourceKind` 决定展开目标：
  - `ConversationTurn` → `IAgentConversationStore.GetConversationAsync`
  - `TaskRecord` / `TaskEvent` → `IAgentTaskHistoryStore.GetTaskAsync`
  - `CompressedContextBlock` → `IAgentCompressedContextStore.GetCompressedContextAsync`
  - `MemoryItem` → `IAgentMemoryStore.GetMemoryAsync`
  - `MemoryCandidate` → `IAgentMemoryStore.GetCandidateAsync`
  - 其余 (MetadataContextPack、ReviewReport、FixProposal、PackagePreview、ActivationRequest) → `NotExpandable`

---

## 8. IsAuthoritative 语义 (IsAuthoritative Semantics)

Memory 始终是**非权威性上下文**：

- `AgentMemoryItem.IsAuthoritative` 在 Promote 时硬编码为 `false`
- `AgentMemoryPack.IsAuthoritative` 在 Recall 时硬编码为 `false`（无论是否被截断）
- 截断触发 `BudgetTruncated` 诊断，但不切换 IsAuthoritative
- 当 Memory 与 MetadataContextPack 冲突时，Memory 让步：

```text
MetadataContextPack > AgentMemoryPack
ReviewResult > AgentMemoryPack
ActivationEvidence > AgentMemoryPack
LifecycleGovernance > AgentMemoryPack
AuthorizationPolicy > AgentMemoryPack
RuntimeActivationGate > AgentMemoryPack
```

---

## 9. 诊断码 (Diagnostic Codes)

所有诊断码定义在 `AgentMemoryDiagnosticCodes` 中：

| 诊断码 | 说明 | 严重度 |
|--------|------|--------|
| `AGENT_MEMORY_EMPTY_CONTENT` | 内容为空或空白字符串 | Warning |
| `AGENT_MEMORY_SOURCE_NOT_FOUND` | 来源在存储中未找到 | Warning |
| `AGENT_MEMORY_SOURCE_NOT_EXPANDABLE` | 来源种类不可展开 | Info |
| `AGENT_MEMORY_CONTENT_REDACTED` | 内容被脱敏（按次数统计） | Info |
| `AGENT_MEMORY_CONTENT_REJECTED` | 内容被拒绝（跳过块或全量脱敏） | Warning |
| `AGENT_MEMORY_BLOCK_SANITIZED` | 块被脱敏（记录脱敏种类） | Info |
| `AGENT_MEMORY_BUDGET_TRUNCATED` | 字符预算截断（记录省略数量） | Warning |
| `AGENT_MEMORY_INVALID_OPERATION_TENANT_MISMATCH` | 操作请求租户不匹配 | Exception |
| `AGENT_MEMORY_INVALID_OPERATION_MISSING_ACTOR` | 操作请求缺少操作者 | Exception |
| `AGENT_MEMORY_INVALID_OPERATION_MISSING_REASON` | 操作请求缺少原因 | Exception |
| `AGENT_MEMORY_INVALID_OPERATION_MISSING_TIMESTAMP` | 操作请求缺少时间戳 | Exception |
| `AGENT_MEMORY_INVALID_OPERATION_MISSING_SOURCE_OR_EXPLANATION` | 操作请求缺少溯源引用或解释 | Exception |
| `AGENT_MEMORY_VISIBILITY_KIND_UNRESOLVABLE` | VisibleDescriptorKinds 过滤器无法解析（fail-closed） | Warning |

### 9.1 脱敏种类（Redaction Kinds）

| 常量 | 值 | 说明 |
|------|-----|------|
| `AgentMemoryRedactionKinds.EmptyContent` | `empty-content` | 空内容 |
| `AgentMemoryRedactionKinds.BearerToken` | `bearer-token` | Bearer Token |
| `AgentMemoryRedactionKinds.Credential` | `credential` | 密码/API 密钥 |
| `AgentMemoryRedactionKinds.ConnectionCredential` | `connection-credential` | 连接字符串凭据 |
| `AgentMemoryRedactionKinds.LongToken` | `long-token` | 长令牌 |

---

## 10. DI 注册 (DI Registration)

```csharp
services.AddAgentMemoryRuntime();
```

注册细节（`AgentMemoryServiceCollectionExtensions`）：

```text
TryAddSingleton<IAgentConversationStore, InMemoryAgentConversationStore>
TryAddSingleton<IAgentTaskHistoryStore, InMemoryAgentTaskHistoryStore>
TryAddSingleton<IAgentCompressedContextStore, InMemoryAgentCompressedContextStore>
TryAddSingleton<IAgentMemoryStore, InMemoryAgentMemoryStore>
TryAddSingleton<IAgentMemoryContentSanitizer, DefaultAgentMemoryContentSanitizer>
TryAddSingleton<IAgentContextCompressor, DefaultAgentContextCompressor>
TryAddSingleton<IAgentMemoryExtractor, DefaultAgentMemoryExtractor>
TryAddSingleton<IAgentMemoryPromotionService, DefaultAgentMemoryPromotionService>
TryAddSingleton<IAgentMemoryRetriever, DefaultAgentMemoryRetriever>
TryAddSingleton<IAgentContextSourceExpander, DefaultAgentContextSourceExpander>
TryAddSingleton<IAgentAuthoringContextBuilder, DefaultAgentAuthoringContextBuilder>
TryAddSingleton(TimeProvider.System)
```

所有注册使用 `TryAddSingleton`，允许消费者提前替换实现。

---

## 11. 边界规则 (Boundary Rules)

| 规则 | 理由 |
|------|------|
| `Agent.Memory.Abstractions` 不引用 `Agent.ControlPlane.Abstractions` | Memory 是轻量上下文设施，不应依赖 Draft、HumanTask、Activation 等重量级合约 |
| `Agent.Memory` 不引用 `Agent.ControlPlane` | Memory 实现不应访问 Control Plane 内置字典、激活存储、注册表 |
| Memory 不解析 Descriptor 可见性 | `AgentMemoryQuery` 携带已解析的可见性边界（`VisibleDescriptorRefs`、`VisibleDescriptorKinds`），Retriever 只过滤 |
| Memory 不查询 Control Plane 存储 | SourceExpander 只展开 Memory 拥有的来源，外部来源返回 `NotExpandable` |
| Memory 不调用 `IRuntimeActivationGate` | Memory 是上下文基础设施，不是运行时状态变异入口 |
| 公共 DTO 是密封 record | 支持值语义、AoT 安全、JSON 友好 |
| JSON Context 注册所有 Root DTO | 确保 Source Generator 覆盖完整的合同表面 |
| Store 返回快照副本 | 防止外部列表突变泄漏到内部存储 |
| InMemory Store 使用 ConcurrentDictionary + (TenantId, Id) 复合键 | 租户隔离 |
| `IAgentMemoryPromotionService` 是生产升级路径 | `SaveMemoryAsync` 是持久化原语，不绕过升级语义 |

### 11.1 边界验证测试

`BoundaryTests` 通过程序集引用检查强制执行以下规则：

- `AgentMemoryAbstractionsAssembly_DoesNotReference_ControlPlaneAbstractions`
- `AgentMemoryRuntimeAssembly_DoesNotReference_ControlPlane`

---

## 12. JSON 序列化 (JSON Serialization)

`AgentMemoryJsonSerializerContext` 使用 Source Generator，注册 18 个类型：

- `AgentMemoryPack`
- `AgentAuthoringContext`
- `AgentAuthoringRequest`
- `AgentCompressedContext`
- `AgentCompressedContextBlock`
- `AgentMemoryCandidate`
- `AgentMemoryItem`
- `AgentMemoryQuery`
- `AgentContextSourceRef`
- `AgentContextEvidenceRef`
- `AgentConversationRecord`
- `AgentConversationTurn`
- `AgentTaskRecord`
- `AgentTaskEvent`
- `AgentSourceExpansionResult`
- `SanitizedAgentContent`
- `AgentMemoryOperationRequest`
- `AgentActorContext`
- `AgentMemoryDiagnostic`

配置：
- `PropertyNamingPolicy = CamelCase`
- `DefaultIgnoreCondition = WhenWritingNull`
- `GenerationMode = Metadata`

---

## 13. 哈希与标识 (Hashing and Identity)

`AgentMemoryHash.ComputeCanonicalHash` 使用 SHA256 进行内容哈希：

```csharp
SHA256.HashData(Encoding.UTF8.GetBytes(content)) → lowercased hex string
```

仅对脱敏后的内容计算哈希。原始敏感内容不被哈希为持久化可比较指纹。

---

## 14. 未来阶段 (Future Phases)

| Phase | 能力 | 状态 |
|-------|------|------|
| **7e+** | Agent Memory & Context Compression Runtime（合约、存储、默认实现、JSON Context、DI） | **Implemented (#43)** |
| 7e+-2 | Memory 工具暴露至 Agent Control Plane（`SaveConversationToMemory`、`RecallMemories` 等 Tool） | Future |
| 7e+-3 | LLM 驱动的候选项提取（替换 `DefaultAgentMemoryExtractor` 为 LLM 适配器） | Future |
| 7e+-4 | Vector 搜索与嵌入排序 | Future |
| 7e+-5 | 生产级持久化提供者（EFCore / FreeSql） | Future |
| 7e+-6 | 后台记忆维护（压缩、去重、过期清理） | Future |
| 7e+-7 | AgentRuntime Domain/Application/DynamicApi 模块栈 | Future |
