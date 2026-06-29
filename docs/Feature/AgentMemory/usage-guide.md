# Agent Memory & Context Compression Runtime — Usage Guide

> **Date:** 2026-06-29 | **Status:** Implemented | **Phase 7e+ (#43)**

## 1. 快速开始 (Quick Start)

### 1.1 前置依赖

Agent Memory Runtime 分布在两个项目中：

- `CrestCreates.Agent.Memory.Abstractions` — 合约、接口、枚举、JSON 序列化上下文
- `CrestCreates.Agent.Memory` — 默认实现、InMemory Store、DI 扩展

### 1.2 命名空间导入

```csharp
// 合约与接口
using CrestCreates.Agent.Memory.Abstractions;

// JSON 序列化（AoT 兼容）
using CrestCreates.Agent.Memory.Abstractions.Json;
```

### 1.3 DI 注册

```csharp
// 在 Startup / Module 中注册所有默认服务
services.AddAgentMemoryRuntime();

// 可选：如果已注册自定义 TimeProvider，不会被覆盖
// TimeProvider.System 是 TryAddSingleton，允许消费者提前替换
```

`AddAgentMemoryRuntime()` 使用 `TryAddSingleton` 注册 12 个服务 + `TimeProvider.System`：

- 4 个 InMemory Store（Conversation、TaskHistory、CompressedContext、Memory）
- 7 个 Default 管线服务（Sanitizer、Compressor、Extractor、PromotionService、Retriever、SourceExpander、AuthoringContextBuilder）

---

## 2. 核心管线使用 (Core Pipeline Usage)

### 2.1 保存对话轮次 (Save Conversation)

```csharp
var sanitizer = services.GetRequiredService<IAgentMemoryContentSanitizer>();
var store = services.GetRequiredService<IAgentConversationStore>();

var conversation = new AgentConversationRecord
{
    ConversationId = "conv-1",
    TenantId = "tenant-1",
    Turns =
    [
        new AgentConversationTurn
        {
            TurnId = "turn-1",
            TenantId = "tenant-1",
            Role = AgentConversationRole.User,
            Content = "I prefer using UTC timestamps for all event records.",
            CreatedAt = DateTimeOffset.UtcNow
        },
        new AgentConversationTurn
        {
            TurnId = "turn-2",
            TenantId = "tenant-1",
            Role = AgentConversationRole.Assistant,
            Content = "Noted. I will use UTC timestamps.",
            CreatedAt = DateTimeOffset.UtcNow
        }
    ]
};

// Save 内部会执行脱敏
await store.SaveConversationAsync(conversation);

// 读取
var retrieved = await store.GetConversationAsync("tenant-1", "conv-1");
Console.WriteLine($"{retrieved!.Turns[0].Role}: {retrieved.Turns[0].Content}");
```

**注意事项**：
- `InMemoryAgentConversationStore.SaveConversationAsync` 内部调用 `IAgentMemoryContentSanitizer.Sanitize` 脱敏每个 turn 的内容
- 返回的快照是防御性副本（`.ToArray()`），外部列表变更不影响内部存储
- 使用 `ConcurrentDictionary<(TenantId, ConversationId), AgentConversationRecord>` 作为存储

### 2.2 保存任务历史 (Save Task History)

```csharp
var taskStore = services.GetRequiredService<IAgentTaskHistoryStore>();

// 创建任务
var task = new AgentTaskRecord
{
    TaskId = "task-1",
    TenantId = "tenant-1",
    Title = "Build descriptor schema",
    Summary = "Define the schema for the OrderProcessing capability",
    Events =
    [
        new AgentTaskEvent
        {
            EventId = "evt-1",
            TenantId = "tenant-1",
            TaskId = "task-1",
            EventKind = "Started",
            Content = "Task started at phase 1.",
            CreatedAt = DateTimeOffset.UtcNow
        }
    ]
};
await taskStore.SaveTaskAsync(task);

// 追加事件
var newEvent = new AgentTaskEvent
{
    EventId = "evt-2",
    TenantId = "tenant-1",
    TaskId = "task-1",
    EventKind = "Progress",
    Content = "Phase 1 completed.",
    CreatedAt = DateTimeOffset.UtcNow
};
await taskStore.AppendEventAsync("tenant-1", "task-1", newEvent);

// 列出所有任务
var allTasks = await taskStore.ListTasksAsync("tenant-1");
Console.WriteLine($"Total tasks: {allTasks.Count}");
```

**注意事项**：
- `AppendEventAsync` 如果 task 不存在会抛出 `InvalidOperationException`（附 "(not found)" 提示）
- Store 内部的 Summary 和 Event.Content 均经过脱敏

### 2.3 压缩上下文 (Compress Context)

```csharp
var compressor = services.GetRequiredService<IAgentContextCompressor>();
var contextStore = services.GetRequiredService<IAgentCompressedContextStore>();

// 压缩对话
var conversation = await conversationStore.GetConversationAsync("tenant-1", "conv-1");
var compressed = await compressor.CompressConversationAsync(conversation!);
await contextStore.SaveCompressedContextAsync(compressed);

Console.WriteLine($"ContextId: {compressed.ContextId}");
Console.WriteLine($"Blocks: {compressed.Blocks.Count}");
foreach (var block in compressed.Blocks)
{
    Console.WriteLine($"  [{block.BlockId}] {block.Content}");
    Console.WriteLine($"    SourceKind={block.SourceRefs[0].SourceKind}, " +
                      $"SourceId={block.SourceRefs[0].SourceId}");
}

// 压缩任务
var task = await taskStore.GetTaskAsync("tenant-1", "task-1");
var taskCompressed = await compressor.CompressTaskAsync(task!);
```

**Compressor 行为**：
- 每个 turn/event 生成一个 `AgentCompressedContextBlock`
- BlockId 格式：`{ConversationId}_{TurnId}` 或 `{TaskId}_{EventId}`
- 当 turn.SourceRefs 为空时，合成 SourceRef（`SourceKind=ConversationTurn`, `SourceId=ConversationId`, `RangeStart=RangeEnd=index`）
- 被脱敏拒绝的块被跳过（`ContentRejected` 诊断）
- 脱敏后的块产生 `BlockSanitized` 诊断
- 内容格式：`[{Role}] {SanitizedContent}` 或 `[{EventKind}] {SanitizedContent}`

### 2.4 提取候选 (Extract Candidates)

```csharp
var extractor = services.GetRequiredService<IAgentMemoryExtractor>();
var memoryStore = services.GetRequiredService<IAgentMemoryStore>();

// 从压缩上下文提取候选
var candidates = await extractor.ExtractCandidatesAsync(compressed);

Console.WriteLine($"Extracted {candidates.Count} candidates:");
foreach (var candidate in candidates)
{
    await memoryStore.SaveCandidateAsync(candidate);
    Console.WriteLine($"  {candidate.CandidateId}: [{candidate.Kind}] {candidate.Content}");
    Console.WriteLine($"    Confidence={candidate.Confidence}, Status={candidate.Status}");
}
```

**默认 Extractor 行为**：
- 每个 `CompressedContextBlock` → 一个 `AgentMemoryCandidate`
- `CandidateId = "candidate_{BlockId}"`
- `Kind = ProjectFact`（默认）
- `Confidence = Low`（默认）
- `Status = Candidate`
- **不自动升级**（需显式调用 PromotionService）

### 2.5 升级候选 (Promote Candidates)

```csharp
var promotionService = services.GetRequiredService<IAgentMemoryPromotionService>();

var operationRequest = new AgentMemoryOperationRequest
{
    TenantId = "tenant-1",
    Actor = new AgentActorContext
    {
        ActorId = "agent-1",
        ActorKind = "Agent"
    },
    Reason = "User preference detected in conversation",
    Timestamp = DateTimeOffset.UtcNow,
    Explanation = "Promoting based on conversation analysis"
};

// 升级
var promotedMemory = await promotionService.PromoteAsync(
    "tenant-1", candidates[0].CandidateId, operationRequest);

Console.WriteLine($"Memory {promotedMemory.MemoryId} promoted:");
Console.WriteLine($"  Content: {promotedMemory.Content}");
Console.WriteLine($"  Status: {promotedMemory.Status}");
Console.WriteLine($"  IsAuthoritative: {promotedMemory.IsAuthoritative}"); // Always false

// 拒绝候选
var rejectRequest = new AgentMemoryOperationRequest
{
    TenantId = "tenant-1",
    Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
    Reason = "Content is not useful",
    Timestamp = DateTimeOffset.UtcNow,
    Explanation = "Not relevant to current project"
};
await promotionService.RejectAsync("tenant-1", candidates[1].CandidateId, rejectRequest);

// 取代已有记忆
var replacement = new AgentMemoryCandidate
{
    CandidateId = "c-new",
    TenantId = "tenant-1",
    Kind = AgentMemoryKind.Preference,
    Content = "Use ISO 8601 with millisecond precision for all timestamps.",
    CanonicalContentHash = "hashed-content"
};
await memoryStore.SaveCandidateAsync(replacement);

var supersedeRequest = new AgentMemoryOperationRequest
{
    TenantId = "tenant-1",
    Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
    Reason = "Updated preference with more precision",
    Timestamp = DateTimeOffset.UtcNow,
    Explanation = "Expanding timestamp format preference"
};

var newMemory = await promotionService.SupersedeAsync(
    "tenant-1", promotedMemory.MemoryId, replacement, supersedeRequest);

Console.WriteLine($"New: {newMemory.MemoryId}, Supersedes: {newMemory.SupersedesMemoryId}");
// 旧记忆被标记为 Superseded，SupersededByMemoryId = 新记忆的 CandidateId

// 归档记忆
var archiveRequest = new AgentMemoryOperationRequest
{
    TenantId = "tenant-1",
    Actor = new AgentActorContext { ActorId = "agent-1", ActorKind = "Agent" },
    Reason = "No longer relevant",
    Timestamp = DateTimeOffset.UtcNow,
    Explanation = "Policy change makes this preference obsolete"
};
await promotionService.ArchiveAsync("tenant-1", promotedMemory.MemoryId, archiveRequest);
```

**5 项验证守卫**（所有操作共享）：
1. `request.TenantId == tenantId` → 否则 `InvalidOperationTenantMismatch`
2. `request.Actor` 非空且 `ActorId`/`ActorKind` 非空 → 否则 `InvalidOperationMissingActor`
3. `request.Reason` 非空 → 否则 `InvalidOperationMissingReason`
4. `request.Timestamp != default` → 否则 `InvalidOperationMissingTimestamp`
5. `request.SourceRefs.Count > 0` 或 `request.Explanation` 非空 → 否则 `InvalidOperationMissingSourceOrExplanation`

### 2.6 召回记忆 (Recall Memories)

```csharp
var retriever = services.GetRequiredService<IAgentMemoryRetriever>();

// 基本查询
var query = new AgentMemoryQuery
{
    TenantId = "tenant-1"
};
var pack = await retriever.RecallAsync(query);

Console.WriteLine($"Recalled {pack.Memories.Count} memories:");
Console.WriteLine($"  IsAuthoritative: {pack.IsAuthoritative}"); // Always false
foreach (var memory in pack.Memories)
{
    Console.WriteLine($"  [{memory.Kind}] {memory.Content}");
    Console.WriteLine($"    Confidence={memory.Confidence}, Status={memory.Status}");
}

// 按 MemoryKind 过滤
var kindQuery = new AgentMemoryQuery
{
    TenantId = "tenant-1",
    Kinds = [AgentMemoryKind.Preference, AgentMemoryKind.Decision]
};

// 置信度阈值
var confidenceQuery = new AgentMemoryQuery
{
    TenantId = "tenant-1",
    MinimumConfidence = AgentMemoryConfidence.Medium
};

// 最大数量 + 字符预算
var budgetQuery = new AgentMemoryQuery
{
    TenantId = "tenant-1",
    MaxCount = 10,
    CharacterBudget = 2000 // 近似字符预算
};

// 包含历史状态
var historyQuery = new AgentMemoryQuery
{
    TenantId = "tenant-1",
    IncludeSuperseded = true,
    IncludeArchived = true
};

// 按 DescriptorRef 过滤
var descriptorRefQuery = new AgentMemoryQuery
{
    TenantId = "tenant-1",
    DescriptorRefs = [new DescriptorRef("default", "cap_process_order")]
};

// 按可见性 DescriptorRefs 过滤（调用方提供已解析的可见性边界）
var visibilityQuery = new AgentMemoryQuery
{
    TenantId = "tenant-1",
    VisibleDescriptorRefs =
    [
        new DescriptorRef("default", "cap_visible_1"),
        new DescriptorRef("default", "cap_visible_2")
    ]
};
```

**Recall 行为**：
- 确定性排序: `Confidence desc → Kind → PromotedAt desc → MemoryId → CanonicalContentHash`
- Store 过滤（Status、Kinds、Tags、DescriptorRefs、MemoryIds）
- Recall 过滤（Confidence、VisibleDescriptorRefs、MaxCount、CharacterBudget）
- `VisibleDescriptorKinds` 过滤器 fail-closed 返回空结果（`VisibilityKindUnresolvable` 诊断）
- 字符预算截断产生 `BudgetTruncated` 诊断
- `IsAuthoritative` 始终为 `false`
- 默认 `IncludeStale=false`，`IncludeSuperseded=false`，`IncludeArchived=false`

### 2.7 构建创作上下文 (Build Authoring Context)

```csharp
var builder = services.GetRequiredService<IAgentAuthoringContextBuilder>();

// 准备 MetadataContextPack（由调用方提供）
var metadataPack = new MetadataContextPack
{
    Request = new MetadataContextPackRequest
    {
        Scope = MetadataContextPackScope.FocusOnly,
        FocusDescriptors = Array.Empty<DescriptorRef>(),
        TenantId = "tenant-1"
    },
    Descriptors = Array.Empty<MetadataContextPackDescriptorEntry>(),
    Relationships = Array.Empty<MetadataContextPackRelationshipEntry>(),
    Summary = new MetadataContextPackSummary
    {
        TotalDescriptorCount = 0,
        DescriptorCountsByKind = new Dictionary<DescriptorKind, int>(),
        TotalRelationshipCount = 0,
        RelationshipCountsByKind = new Dictionary<RelationshipKind, int>(),
        FocusRefs = Array.Empty<DescriptorRef>(),
        WasTruncated = false
    },
    Diagnostics = Array.Empty<MetadataContextPackDiagnostic>()
};

// 准备 AuthoringRequest
var authoringRequest = new AgentAuthoringRequest
{
    TenantId = "tenant-1",
    IntentText = "What timestamp format should I use?",
    // MemoryQuery 可选，不提供时使用默认值
    MemoryQuery = new AgentMemoryQuery
    {
        TenantId = "tenant-1",
        MaximumConfidence = AgentMemoryConfidence.Medium,
        MaxCount = 5
    }
};

// 构建创作上下文
var authoringContext = await builder.BuildAsync(
    authoringRequest, metadataPack);

Console.WriteLine($"Request Intent: {authoringContext.Request.IntentText}");
Console.WriteLine($"Metadata Descriptors: {authoringContext.MetadataContextPack.Summary.TotalDescriptorCount}");
Console.WriteLine($"Memories: {authoringContext.MemoryPack.Memories.Count}");
Console.WriteLine($"Diagnostics: {authoringContext.Diagnostics.Count}");

// 检查记忆与元数据冲突（MetadataContextPack 优先于 AgentMemoryPack）
foreach (var memory in authoringContext.MemoryPack.Memories)
{
    // Memory 始终标记为非权威性
    Console.WriteLine($"  [{memory.Kind}] {memory.Content} (authoritative: {memory.IsAuthoritative})");
}
```

**Builder 行为**：
- 接收已构建的 `MetadataContextPack` 和 `AgentAuthoringRequest`
- 通过 `IAgentMemoryRetriever.RecallAsync` 获取记忆
- 不查询 Control Plane、Descriptor Store、Activation Store 或 Registry
- 不突变输入
- 如果 `AuthoringRequest.MemoryQuery` 为 null，使用默认 query（只设 TenantId）

---

## 3. 查询与过滤 (Query and Filtering)

### 3.1 AgentMemoryQuery 完整字段

| 字段 | 类型 | 默认值 | 过滤层级 | 说明 |
|------|------|--------|---------|------|
| `TenantId` | `required string` | — | Store | 租户隔离（必填） |
| `MemoryIds` | `IReadOnlyList<string>` | `[]` | Store | 精确 ID 匹配 |
| `Kinds` | `IReadOnlyList<AgentMemoryKind>` | `[]` | Store | 记忆种类过滤 |
| `Tags` | `IReadOnlyList<string>` | `[]` | Store | 标签过滤（任一匹配） |
| `DescriptorRefs` | `IReadOnlyList<DescriptorRef>` | `[]` | Store | Descriptor 引用过滤 |
| `IncludeStale` | `bool` | `false` | Store | 是否包含 Stale 状态 |
| `IncludeSuperseded` | `bool` | `false` | Store | 是否包含 Superseded 状态 |
| `IncludeArchived` | `bool` | `false` | Store | 是否包含 Archived 状态 |
| `IntentText` | `string?` | `null` | Recall | 意图文本（预留，当前未参与评分） |
| `MinimumConfidence` | `AgentMemoryConfidence` | `Unknown` | Recall | 最小置信度阈值 |
| `VisibleDescriptorRefs` | `IReadOnlyList<DescriptorRef>` | `[]` | Recall | 可见 Descriptor 引用（调用方提供） |
| `VisibleDescriptorKinds` | `IReadOnlyList<DescriptorKind>` | `[]` | Recall | 可见 Descriptor 种类（**fail-closed**） |
| `MaxCount` | `int?` | `null` | Recall | 最大记忆数 |
| `CharacterBudget` | `int?` | `null` | Recall | 近似字符预算 |
| `IncludeSourceRefs` | `bool` | `true` | — | 是否保留 SourceRefs |

### 3.2 Store-level vs Recall-level 过滤

```csharp
// Store 查询：持久化层面过滤
// InMemoryAgentMemoryStore 只过滤 TenantId、Kinds、Tags、MemoryIds、DescriptorRefs、Status
var storeQuery = new AgentMemoryQuery
{
    TenantId = "tenant-1",
    Kinds = [AgentMemoryKind.Preference],
    Tags = ["urgent"]
};
var rawResults = await memoryStore.ListMemoriesAsync(storeQuery); // Store 不过滤 Confidence

// Recall 查询：在 Store 结果上应用 Recall 级过滤
// DefaultAgentMemoryRetriever 过滤 Confidence、VisibleDescriptorRefs、MaxCount、CharacterBudget
var recallQuery = new AgentMemoryQuery
{
    TenantId = "tenant-1",
    Kinds = [AgentMemoryKind.Preference],
    MinimumConfidence = AgentMemoryConfidence.High,
    MaxCount = 3,
    CharacterBudget = 1000
};
var pack = await retriever.RecallAsync(recallQuery);
```

---

## 4. SourceRef 展开 (SourceRef Expansion)

### 4.1 SourceRef 协议

`AgentContextSourceRef` 是溯源的唯一载体。`SourceId` 是实体 ID（conversationId 或 taskId），`RangeStart`/`RangeEnd` 是条目标索引。

### 4.2 展开来源

```csharp
var expander = services.GetRequiredService<IAgentContextSourceExpander>();

// 展开对话来源（返回所有 turn 内容）
var conversationSource = new AgentContextSourceRef
{
    SourceKind = AgentSourceKind.ConversationTurn,
    TenantId = "tenant-1",
    SourceId = "conv-1"
};
var convResult = await expander.ExpandAsync(conversationSource);
Console.WriteLine($"Status: {convResult.Status}"); // Expanded
Console.WriteLine($"Content: {convResult.SanitizedContent}");

// 展开带范围的来源（只返回 RangeStart 到 RangeEnd 的 turn）
var rangedSource = new AgentContextSourceRef
{
    SourceKind = AgentSourceKind.ConversationTurn,
    TenantId = "tenant-1",
    SourceId = "conv-1",
    RangeStart = 0,
    RangeEnd = 0  // 只有第 0 个 turn
};
var rangedResult = await expander.ExpandAsync(rangedSource);

// 展开任务来源
var taskSource = new AgentContextSourceRef
{
    SourceKind = AgentSourceKind.TaskRecord,
    TenantId = "tenant-1",
    SourceId = "task-1"
};
var taskResult = await expander.ExpandAsync(taskSource);

// 展开记忆来源
var memorySource = new AgentContextSourceRef
{
    SourceKind = AgentSourceKind.MemoryItem,
    TenantId = "tenant-1",
    SourceId = "m-1"
};
var memResult = await expander.ExpandAsync(memorySource);

// 外部来源 → NotExpandable
var extSource = new AgentContextSourceRef
{
    SourceKind = AgentSourceKind.ReviewReport,
    TenantId = "tenant-1",
    SourceId = "report-1"
};
var extResult = await expander.ExpandAsync(extSource);
Console.WriteLine($"Status: {extResult.Status}"); // NotExpandable
```

### 4.3 SourceRef 展开状态

| 状态 | 说明 |
|------|------|
| `Expanded` | 成功展开（返回 `SanitizedContent`） |
| `NotFound` | 来源在存储中未找到（`AGENT_MEMORY_SOURCE_NOT_FOUND` 诊断） |
| `NotExpandable` | 来源种类不可展开（`AGENT_MEMORY_SOURCE_NOT_EXPANDABLE` 诊断） |
| `ExternalSourceNotSupported` | 外部来源（预留，当前未使用） |
| `Redacted` | 已被脱敏（预留，当前未使用） |

---

## 5. 内存生命周期 (Memory Lifecycle)

### 5.1 状态转换

```
Candidate ──Promote──→ Active ──Supersede──→ Superseded
    │                       │                      │
    ├──Reject──→ Rejected   ├──Archive──→ Archived  ├──Archive──→ Archived
    │                       │                      │
    └───────────────────────┴──────────────────────┘
```

### 5.2 状态操作

| 操作 | 前置状态 | 后置状态 | 副作用 |
|------|---------|---------|--------|
| `PromoteAsync` | Candidate | Active | 创建 `AgentMemoryItem`（IsAuthoritative=false），Candidate.Status 更新为 Active |
| `RejectAsync` | Candidate | Rejected | Candidate.Status 更新为 Rejected |
| `SupersedeAsync` | Active | 旧→Superseded, 新→Active | 旧记忆 `SupersededByMemoryId` 指向新记忆 ID，新记忆 `SupersedesMemoryId` 指向旧记忆 ID |
| `ArchiveAsync` | Active 或 Superseded | Archived | 只有 Active 和 Superseded 可归档 |

### 5.3 双向链接

取代操作在旧记忆和新记忆之间建立双向链接：

```csharp
var oldMemory = await memoryStore.GetMemoryAsync(tenantId, originalMemoryId);
Console.WriteLine($"Old memory: {oldMemory!.Status}, SupersededBy: {oldMemory.SupersededByMemoryId}");

var newMemory = await memoryStore.GetMemoryAsync(tenantId, replacementCandidateId);
Console.WriteLine($"New memory: {newMemory!.Status}, Supersedes: {newMemory.SupersedesMemoryId}");
```

---

## 6. 诊断与脱敏 (Diagnostics and Sanitization)

### 6.1 诊断码列表

| 诊断码 | 常量 | 说明 |
|--------|------|------|
| `EmptyContent` | `AGENT_MEMORY_EMPTY_CONTENT` | 内容为空或空白 |
| `SourceNotFound` | `AGENT_MEMORY_SOURCE_NOT_FOUND` | 来源在存储中未找到 |
| `SourceNotExpandable` | `AGENT_MEMORY_SOURCE_NOT_EXPANDABLE` | 来源种类不可展开 |
| `ContentRedacted` | `AGENT_MEMORY_CONTENT_REDACTED` | 内容被脱敏（按次数） |
| `ContentRejected` | `AGENT_MEMORY_CONTENT_REJECTED` | 内容被拒绝 |
| `BlockSanitized` | `AGENT_MEMORY_BLOCK_SANITIZED` | 块被脱敏 |
| `BudgetTruncated` | `AGENT_MEMORY_BUDGET_TRUNCATED` | 字符预算截断 |
| `InvalidOperationTenantMismatch` | `AGENT_MEMORY_INVALID_OPERATION_TENANT_MISMATCH` | 租户不匹配 |
| `InvalidOperationMissingActor` | `AGENT_MEMORY_INVALID_OPERATION_MISSING_ACTOR` | 缺少操作者 |
| `InvalidOperationMissingReason` | `AGENT_MEMORY_INVALID_OPERATION_MISSING_REASON` | 缺少原因 |
| `InvalidOperationMissingTimestamp` | `AGENT_MEMORY_INVALID_OPERATION_MISSING_TIMESTAMP` | 缺少时间戳 |
| `InvalidOperationMissingSourceOrExplanation` | `AGENT_MEMORY_INVALID_OPERATION_MISSING_SOURCE_OR_EXPLANATION` | 缺少来源或解释 |
| `VisibilityKindUnresolvable` | `AGENT_MEMORY_VISIBILITY_KIND_UNRESOLVABLE` | 可见性种类无法解析 |

### 6.2 脱敏种类

```csharp
// 5 种脱敏种类定义在 AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds 中
AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.EmptyContent        // "empty-content"
AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.BearerToken         // "bearer-token"
AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.Credential          // "credential"
AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.ConnectionCredential // "connection-credential"
AgentMemoryDiagnosticCodes.AgentMemoryRedactionKinds.LongToken            // "long-token"
```

### 6.3 脱敏示例

```csharp
var sanitizer = services.GetRequiredService<IAgentMemoryContentSanitizer>();

// Bearer token 脱敏
var result1 = sanitizer.Sanitize("tenant-1",
    "Authorization: Bearer abc123token",
    Array.Empty<AgentContextSourceRef>());
Console.WriteLine(result1.SanitizedContent); // "Authorization: [REDACTED:bearer-token]"
Console.WriteLine(result1.RedactionKinds);   // ["bearer-token"]
Console.WriteLine(result1.Rejected);          // false

// 密码赋值脱敏（全量脱敏 → 拒绝）
var result2 = sanitizer.Sanitize("tenant-1",
    "api_key=sk-abc123xyz",
    Array.Empty<AgentContextSourceRef>());
Console.WriteLine(result2.Rejected);          // true (全量脱敏)
Console.WriteLine(result2.Diagnostics[1].Code); // AGENT_MEMORY_CONTENT_REJECTED

// 连接字符串脱敏（部分脱敏，不被拒绝）
var result3 = sanitizer.Sanitize("tenant-1",
    "Server=host;Password=mypass;Database=db",
    Array.Empty<AgentContextSourceRef>());
Console.WriteLine(result3.SanitizedContent); // "Server=host;[REDACTED:connection-credential];Database=db"
Console.WriteLine(result3.Rejected);          // false (非全量)

// 空内容 → 拒绝
var result4 = sanitizer.Sanitize("tenant-1", "   ", Array.Empty<AgentContextSourceRef>());
Console.WriteLine(result4.Rejected);    // true
Console.WriteLine(result4.Diagnostics[0].Code); // AGENT_MEMORY_EMPTY_CONTENT
```

### 6.4 管线中的脱敏位置

```text
SaveConversation/SaveTask           ← 存储时脱敏
  ↓
CompressConversation/CompressTask   ← 压缩时脱敏
  ↓
ExtractCandidates / Promote          ← 候选/记忆保留脱敏内容
  ↓
Recall                              ← 返回脱敏内容
  ↓
SourceExpansion                     ← 展开返回已脱敏的存储内容
```

---

## 7. DI 注册 (DI Registration)

### 7.1 完整注册

```csharp
using CrestCreates.Agent.Memory;

// 注册所有默认服务
services.AddAgentMemoryRuntime();
```

### 7.2 注册明细

| 接口 | 默认实现 | 生命周期 |
|------|---------|---------|
| `IAgentConversationStore` | `InMemoryAgentConversationStore` | Singleton |
| `IAgentTaskHistoryStore` | `InMemoryAgentTaskHistoryStore` | Singleton |
| `IAgentCompressedContextStore` | `InMemoryAgentCompressedContextStore` | Singleton |
| `IAgentMemoryStore` | `InMemoryAgentMemoryStore` | Singleton |
| `IAgentMemoryContentSanitizer` | `DefaultAgentMemoryContentSanitizer` | Singleton |
| `IAgentContextCompressor` | `DefaultAgentContextCompressor` | Singleton |
| `IAgentMemoryExtractor` | `DefaultAgentMemoryExtractor` | Singleton |
| `IAgentMemoryPromotionService` | `DefaultAgentMemoryPromotionService` | Singleton |
| `IAgentMemoryRetriever` | `DefaultAgentMemoryRetriever` | Singleton |
| `IAgentContextSourceExpander` | `DefaultAgentContextSourceExpander` | Singleton |
| `IAgentAuthoringContextBuilder` | `DefaultAgentAuthoringContextBuilder` | Singleton |
| `TimeProvider` | `TimeProvider.System` | Singleton |

### 7.3 自定义替换

```csharp
// 替换为自定义 Compressor
services.AddSingleton<IAgentContextCompressor, MyCustomCompressor>();

// 替换为自定义 Store（例如 EFCore 持久化）
services.AddScoped<IAgentMemoryStore, EFCoreAgentMemoryStore>();

// 然后调用 AddAgentMemoryRuntime（TryAddSingleton 不会覆盖已有的注册）
services.AddAgentMemoryRuntime();
```

---

## 8. JSON 序列化 (JSON Serialization)

### 8.1 Source-Generated Context

```csharp
using CrestCreates.Agent.Memory.Abstractions.Json;
using System.Text.Json;

// 使用预配置的 Context
var json = JsonSerializer.Serialize(
    myAgentMemoryPack,
    AgentMemoryJsonSerializerContext.Default.AgentMemoryPack);

// 反序列化
var pack = JsonSerializer.Deserialize<AgentMemoryPack>(
    json,
    AgentMemoryJsonSerializerContext.Default.AgentMemoryPack);
```

### 8.2 序列化特性

- CamelCase 属性命名策略
- 忽略 null 值（`DefaultIgnoreCondition = WhenWritingNull`）
- Metadata 生成模式
- `DiagnosticCode` 序列化为/反序列化自其字符串值（例如 `"AGENT_MEMORY_CONTENT_REJECTED"`）
- 所有集合属性使用 `IReadOnlyList<T>`（无暴露可变 `List<T>`）

---

## 9. 测试模式 (Testing Patterns)

### 9.1 合约测试 (ContractTests)

```csharp
// AgentMemoryConfidence 是封闭枚举，不是浮点数
[Fact]
public void AgentMemoryConfidence_IsClosedEnum_NotFloatingPoint()
{
    typeof(AgentMemoryConfidence).IsEnum.Should().BeTrue();
    typeof(AgentMemoryItem).GetProperty(nameof(AgentMemoryItem.Confidence))!
        .PropertyType.Should().Be(typeof(AgentMemoryConfidence));
}

// AgentContextEvidenceRef 未被命名/误用为 ActivationEvidence
[Fact]
public void AgentContextEvidenceRef_IsNotNamedActivationEvidence()
{
    typeof(AgentContextEvidenceRef).Name.Should().Be("AgentContextEvidenceRef");
}

// DiagnosticCode 序列化为/反序列化自字符串
[Fact]
public void AgentMemoryDiagnostic_JsonSerializesDiagnosticCodeAsString()
{
    var diagnostic = new AgentMemoryDiagnostic
    {
        Code = AgentMemoryDiagnosticCodes.ContentRejected,
        Message = "Rejected",
        Severity = SeverityLevel.Warning
    };
    var json = JsonSerializer.Serialize(diagnostic,
        AgentMemoryJsonSerializerContext.Default.AgentMemoryDiagnostic);
    json.Should().Contain("\"code\":\"AGENT_MEMORY_CONTENT_REJECTED\"");
}

// 无公共可变集合暴露
[Fact]
public void Contracts_DoNotExposeMutableCollectionTypes()
{
    var mutableProperties = typeof(AgentMemoryPack).Assembly.GetTypes()
        .Where(type => type.IsPublic)
        .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        .Where(property => property.PropertyType.IsGenericType)
        .Where(property => property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
        .Should().BeEmpty();
}
```

### 9.2 边界测试 (BoundaryTests)

```csharp
// AgentMemory.Abstractions 不引用 ControlPlane.Abstractions
[Fact]
public void AgentMemoryAbstractionsAssembly_DoesNotReference_ControlPlaneAbstractions()
{
    typeof(AgentMemoryDiagnostic).Assembly
        .GetReferencedAssemblies()
        .Select(name => name.Name)
        .Should()
        .NotContain("CrestCreates.Agent.ControlPlane.Abstractions");
}

// AgentMemory 不引用 ControlPlane（实现）
[Fact]
public void AgentMemoryRuntimeAssembly_DoesNotReference_ControlPlane()
{
    typeof(AgentMemoryServiceCollectionExtensions).Assembly
        .GetReferencedAssemblies()
        .Select(name => name.Name)
        .Should()
        .NotContain(new[] {
            "CrestCreates.Agent.ControlPlane",
            "CrestCreates.Agent.ControlPlane.Abstractions"
        });
}
```

### 9.3 主链测试 (MainChainTests)

完整管线端到端测试（无 LLM）：

```csharp
[Fact]
public async Task FullMainChain_ConversationToAuthoringContext()
{
    var sanitizer = new DefaultAgentMemoryContentSanitizer();
    var conversationStore = new InMemoryAgentConversationStore(sanitizer);
    var taskStore = new InMemoryAgentTaskHistoryStore(sanitizer);
    var contextStore = new InMemoryAgentCompressedContextStore();
    var memoryStore = new InMemoryAgentMemoryStore();
    var compressor = new DefaultAgentContextCompressor(sanitizer);
    var extractor = new DefaultAgentMemoryExtractor();
    var promotionService = new DefaultAgentMemoryPromotionService(memoryStore);
    var retriever = new DefaultAgentMemoryRetriever(memoryStore);
    var expander = new DefaultAgentContextSourceExpander(
        conversationStore, taskStore, contextStore, memoryStore);
    var builder = new DefaultAgentAuthoringContextBuilder(retriever);

    // Step 1: Save conversation
    var conversation = new AgentConversationRecord { ... };
    await conversationStore.SaveConversationAsync(conversation);

    // Step 2: Compress
    var compressed = await compressor.CompressConversationAsync(conversation);
    await contextStore.SaveCompressedContextAsync(compressed);

    // Step 3: Extract candidates
    var candidates = await extractor.ExtractCandidatesAsync(compressed);
    foreach (var c in candidates) await memoryStore.SaveCandidateAsync(c);

    // Step 4: Promote
    var promoted = await promotionService.PromoteAsync(
        tenantId, candidates[0].CandidateId, operationRequest);
    promoted.Status.Should().Be(AgentMemoryStatus.Active);

    // Step 5: Recall
    var pack = await retriever.RecallAsync(new AgentMemoryQuery { TenantId = tenantId });
    pack.Memories.Should().ContainSingle();
    pack.IsAuthoritative.Should().BeFalse();

    // Step 6: Build authoring context
    var authoringContext = await builder.BuildAsync(
        authoringRequest, metadataContextPack);
    authoringContext.MemoryPack.Memories.Should().ContainSingle();
}
```

### 9.4 关键测试覆盖

主链测试覆盖以下关键行为：

| 测试 | 验证行为 |
|------|---------|
| `Sanitizer_RedactsBearerTokens` | Bearer token 被脱敏（`[REDACTED:bearer-token]`） |
| `Sanitizer_RejectsEntirelyRedactedContent` | 全量脱敏内容被拒绝 |
| `Compressor_SanitizesBeforeCompressing` | 压缩前脱敏（内容含 REDACTED 标记） |
| `Compressor_SkipsRejectedBlocks` | 被拒绝的块被跳过 |
| `Compressor_GeneratesSyntheticSourceRef` | 空的 SourceRefs 产生合成 SourceRef |
| `Recall_IsAlwaysNonAuthoritative` | `IsAuthoritative` 始终为 false |
| `Recall_EmitsDiagnosticWhenBudgetTruncates` | 预算截断触发 `BudgetTruncated` |
| `Recall_DeterministicOrdering` | 按 Confidence → Kind → PromotedAt → MemoryId → Hash 排序 |
| `Recall_VisibleDescriptorKinds_FailClosed` | `VisibleDescriptorKinds` 过滤器 fail-closed 返回空 |
| `Store_DoesNotApplyRecallFilters` | Store 不过滤 Confidence 等 Recall 字段 |
| `Store_ReturnsSnapshotCopies` | 读取返回防御性副本 |
| `Store_SanitizesContentOnSave` | Store 保存时自动脱敏 |
| `Promotion_RequiresNonEmptyReason` | Reason 为空时抛出 `InvalidOperationMissingReason` |
| `Promotion_RequiresSourceRefsOrExplanation` | 无 SourceRefs 且无 Explanation 时抛出异常 |
| `Promotion_RequiresActorContext` | ActorId 为空时抛出 `InvalidOperationMissingActor` |
| `Promotion_RequiresMatchingTenantId` | 租户不匹配时抛出 `InvalidOperationTenantMismatch` |
| `Promotion_RejectsNonCandidateStatus` | 只能升级 Candidate 状态 |
| `Supersede_CreatesLink` | 双向链接正确（SupersedesMemoryId + SupersededByMemoryId） |
| `SourceExpander_ResolvesConversationSource` | 展开对话来源 |
| `SourceExpander_ReturnsNotFoundForMissingSource` | 来源不存在返回 NotFound |
| `Expander_ReturnsSanitizedContent` | 展开返回脱敏内容 |
