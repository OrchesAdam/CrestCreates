# Tool DTO, JSON Contract & Review Report — Usage Guide

> **Date:** 2026-07-02 | **Status:** Implemented | **Phase 7c (#41 DTO Design + #42 Source Generator) + Phase 7d (#16 Review Report & Fix Proposal) + Phase 7e (#17 Safe Activation Workflow) + Phase 7e+ (#43 Agent Memory & Context Compression Runtime) + Phase 7f (#32 AI-assisted Descriptor Authoring Golden Scenario) + Phase 7g (#48 LLM-backed Descriptor Authoring Adapter) + Phase 7h (#52 Agent Prompt Contracts and Prompt Versioning)**

## 1. 快速开始 (Quick Start)

### 1.1 前置依赖

Phase 7c 的 DTO 和 JSON 序列化上下文位于 `CrestCreates.Agent.ControlPlane.Abstractions` 项目。使用方式：

```csharp
// 引入命名空间
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Json;
```

### 1.2 获取 JSON 序列化选项

```csharp
// 预配置的 JsonSerializerOptions，使用 Source Generator 注册
var options = AgentControlPlaneToolJsonSerializerOptions.CreateDefault();

// 等价于手工构造：
var options = new JsonSerializerOptions
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        AgentControlPlaneToolJsonSerializerContext.Default)
};
```

### 1.3 序列化示例

```csharp
// 反序列化工具结果
var result = JsonSerializer.Deserialize<AgentToolResult<AgentDescriptorDraftDto>>(json, options);

// 序列化创建草稿请求
var request = new CreateDescriptorDraftRequest
{
    DescriptorKind = DescriptorKind.Schema,
    DescriptorId = "schema_blog_post",
    Operation = DescriptorDraftOperation.Create,
    Payload = new AgentDraftPayloadDto
    {
        Discriminator = DescriptorKind.Schema,
        Schema = new AgentSchemaDraftPayloadDto
        {
            DescriptorRef = new DescriptorRef("default", "schema_blog_post"),
            Name = "BlogPost",
            DisplayName = "Blog Post Schema",
            State = "Active",
            SchemaKind = "Evolutionary",
            ContractHash = "abc123",
            DefinitionHash = "def456",
            Version = 1
        }
    }
};

var json = JsonSerializer.Serialize(request, options);
```

---

## 2. 工具 DTO 参考 (Tool DTO Reference)

### 2.1 泛型结果包装

所有工具返回 `AgentToolResult<T>`，其中 `T` 是具体结果类型：

```csharp
public sealed record AgentToolResult<T> where T : class
{
    public required AgentToolResultStatus Status { get; init; }    // Success / Denied / Failed / InvalidRequest / NotFound
    public T? Value { get; init; }
    public required IReadOnlyList<AgentToolDiagnostic> Diagnostics { get; init; }
    public AgentToolInvocationAuditRecord? AuditRecord { get; init; }

    // 工厂方法
    public static AgentToolResult<T> Success(T value, ...);
    public static AgentToolResult<T> Denied(IReadOnlyList<AgentToolDiagnostic> diagnostics, ...);
    public static AgentToolResult<T> InvalidRequest(IReadOnlyList<AgentToolDiagnostic> diagnostics, ...);
    public static AgentToolResult<T> NotFound(string message, ...);
    public static AgentToolResult<T> Failed(IReadOnlyList<AgentToolDiagnostic> diagnostics, ...);
}
```

### 2.2 Wave 1 — Context / Read（6 tools）

| 工具名 | 请求类型 | 结果类型 (AgentToolResult<>) |
|--------|---------|---------------------------|
| `BuildMetadataContextPack` | `MetadataContextPackRequest` | `MetadataContextPack` |
| `BuildRuntimeScenarioContextPack` | `MetadataContextPackRequest` | `MetadataContextPack` |
| `GetDescriptorByRef` | `DescriptorRef` | `DescriptorInfo` |
| `SearchDescriptors` | `DescriptorSearchRequest` | `DescriptorSearchResult` |
| `ListDescriptorRelationships` | `DescriptorRef` | `DescriptorRelationshipsResult` |
| `GetTopologySummary` | (无请求参数) | `TopologySummaryResult` |

### 2.3 Wave 2 — Draft（6 tools）

| 工具名 | 请求类型 | 结果类型 (AgentToolResult<>) |
|--------|---------|---------------------------|
| `CreateDescriptorDraft` | `CreateDescriptorDraftRequest` | `AgentDescriptorDraftDto` |
| `UpdateDescriptorDraft` | `UpdateDescriptorDraftRequest` | `AgentDescriptorDraftDto` |
| `GetDescriptorDraft` | `DescriptorRef` | `AgentDescriptorDraftDto` |
| `ListDescriptorDrafts` | `DraftQuery` | `DescriptorDraftListResult` |
| `CancelDescriptorDraft` | `DescriptorRef` | `AgentDescriptorDraftDto` |
| `CompareDescriptorDraft` | `DescriptorRef` | `DraftComparisonResult` |

**DraftComparisonResult** 包含：
- `AgentDescriptorDraftDto Draft` — 草稿本体
- `DescriptorSummaryDto? CurrentActiveDescriptor` — 当前活跃描述符摘要（替换原来的 `IDescriptor?`）
- `IReadOnlyList<DraftDifference> Differences` — 差异列表

### 2.4 Wave 3 — Review（5 tools）

| 工具名 | 请求类型 | 结果类型 (AgentToolResult<>) |
|--------|---------|---------------------------|
| `ValidateDescriptorDraft` | `DescriptorRef` | `DescriptorDraftValidationResult` |
| `ReviewDescriptorDraft` | `DescriptorRef` | `AgentReviewResultDto` |
| `GetDraftReviewResult` | `DescriptorRef` | `AgentReviewResultDto` |
| `ListDraftReviewResults` | (无请求参数) | `ReviewResultListResult` |
| `ExplainDiagnostics` | `ExplainDiagnosticsRequest` | `DiagnosticExplanation` |

**AgentReviewResultDto** 包含 6 个摘要子 DTO：
- `AgentMaterializationSummaryDto? MaterializationSummary`
- `AgentProposedInventorySummaryDto? ProposedInventorySummary`
- `AgentTopologySummaryDto? TopologySummary`
- `AgentImpactAnalysisSummaryDto? ImpactAnalysisSummary`
- `AgentCompatibilitySummaryDto? CompatibilitySummary`
- `AgentGovernanceSummaryDto? GovernanceSummary`

### 2.5 Wave 3.5 — ReviewReport (New in Phase 7d)

| 工具名 | 请求类型 | 结果类型 (AgentToolResult<>) |
|--------|---------|---------------------------|
| `BuildDescriptorReviewReport` | `string draftId` | `DescriptorReviewReportDto` |
| `RenderDescriptorReviewReport` | `DescriptorReviewReportDto` + `DescriptorReviewReportFormat` | `string` |

**DescriptorReviewReportDto** 包含 13 个固定 Section，3 个核心子 DTO：
- 13 × `DescriptorReviewReportSectionDto` — 每个含 Kind、SectionId、Title、Order、IsEmpty、OverallSeverity、Items
- `DescriptorReviewReportItemDto` — 含 ReasonCode、MessageTemplateId、Message、Severity、Parameters、RelatedDescriptorIds
- `DescriptorReviewRecommendationDto` — 含 Kind (RequestActivationHandoff / RequestHumanReview / ApplyFixProposal / ReviseDraft / CancelDraft / NoAction)、IsActionable

### 2.6 Wave 4 — Fix Proposal（4 tools）

| 工具名 | 请求类型 | 结果类型 (AgentToolResult<>) |
|--------|---------|---------------------------|
| `SuggestDescriptorDraftFixes` | `DescriptorRef` | `FixProposalListResult` |
| `GetFixProposal` | `DescriptorRef` | `FixProposal` |
| `ListFixProposals` | `DescriptorRef` | `FixProposalListResult` |
| `ApplyFixProposalToDraft` | `ApplyFixProposalRequest` | `AgentDescriptorDraftDto` |

### 2.7 Wave 5 — Package Preview（4 tools）

| 工具名 | 请求类型 | 结果类型 (AgentToolResult<>) |
|--------|---------|---------------------------|
| `PreviewDescriptorPackage` | `DescriptorRef` | `DescriptorPackagePreview` |
| `BuildPackageEvidencePreview` | `DescriptorRef` | `PackageEvidencePreview` |
| `BuildActivationReadinessPreview` | `DescriptorRef` | `ActivationReadinessPreview` |
| `GetPackagePreview` | `DescriptorRef` | `DescriptorPackagePreview` |

### 2.8 Wave 6 — Activation Handoff（3 tools）

> **Phase 7e (#17)** 完整实现了 Wave 6 的激活工作流，包括激活请求生命周期管理、HumanTask 审查编排、证据重校验、以及 `IRuntimeActivationGate` 作为唯一运行时状态变异入口。

| 工具名 | 请求类型 | 结果类型 (AgentToolResult<>) |
|--------|---------|---------------------------|
| `SubmitActivationRequest` | `SubmitActivationRequestRequest` | `ActivationRequest` |
| `GetActivationRequestStatus` | `DescriptorRef` | `ActivationRequest` |
| `CancelActivationRequest` | `DescriptorRef` | `ActivationRequest` |

**SubmitActivationRequestRequest** 新增字段：

```csharp
public sealed record SubmitActivationRequestRequest
{
    public required string DraftId { get; init; }
    public required ActivationBindingSnapshot BindingSnapshot { get; init; }
    public DescriptorLifecycleDecisionKind? GovernanceDecision { get; init; } // NEW in Phase 7e
}
```

**ActivationBindingSnapshot** — 激活请求时的绑定引用与哈希快照：

```csharp
public sealed record ActivationBindingSnapshot
{
    public required string TenantId { get; init; }
    public required string DraftId { get; init; }
    public required int DraftVersion { get; init; }
    public required string ReviewResultId { get; init; }
    public string? ReportId { get; init; }
    public required string PackagePreviewId { get; init; }    // required (Phase 7e) - 原 string?
    public required string EvidencePreviewId { get; init; }   // required (Phase 7e) - 原 string?
    public required BindingHashes Hashes { get; init; }
    public string? CorrelationId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
```

**BindingHashes** — 7 个 CanonicalHash 字段：

```csharp
public sealed record BindingHashes
{
    public required CanonicalHash SourceReviewHash { get; init; }
    public required CanonicalHash ReviewManifestHash { get; init; }
    public required CanonicalHash PackageManifestHash { get; init; }
    public required CanonicalHash PackageEvidenceHash { get; init; }
    public required CanonicalHash PackageEvidenceEnvelopeHash { get; init; }
    public required CanonicalHash ContractHash { get; init; }
    public required CanonicalHash DefinitionHash { get; init; }
}
```

**ActivationRequest** — 激活请求主记录：

```csharp
public sealed record ActivationRequest
{
    public required string RequestId { get; init; }
    public required string DraftId { get; init; }
    public required string TenantId { get; init; }
    public required ActivationRequestStatus Status { get; init; }
    public required ActivationBindingSnapshot BindingSnapshot { get; init; }
    public required DescriptorActivationPolicy Policy { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DescriptorLifecycleDecisionKind? GovernanceDecision { get; init; }
}
```

**ActivationRequestStatus** — 6 值状态枚举：
- `Submitted` — 已提交
- `UnderReview` — 审查中（HumanTask 创建后自动转换）
- `Approved` — 已批准（触发 RuntimeActivationGate）
- `Rejected` — 已拒绝（人工决策）
- `Cancelled` — 请求者取消
- `Expired` — 超时过期

**激活工作流**：

```
1. SubmitActivationRequest → Status: Submitted
   ├─ ToolService 从 ReviewResult 提取 GovernanceDecision
   ├─ RequestService 捕获 Policy 快照 + BindingSnapshot
   └─ ReviewOrchestrator 创建 HumanTask → Status: UnderReview

2. [HumanTask 审查完成] → HumanTaskCompletedEvent
   ├─ DescriptorActivationReviewHumanTaskEventHandler 接收事件
   ├─ DescriptorActivationReviewDecisionParser.TryParse 解析决策
   └─ 路由到 RequestService:
       ├─ Approved → EvidenceRechecker.RecheckAsync → RuntimeActivationGate.ActivateAsync
       └─ Rejected → 更新状态 + 审计

3. GetActivationRequestStatus → 查询当前状态和记录

4. CancelActivationRequest → RequestService 取消 + 审计
```

### 2.9 Wave 7 — Manifest（2 tools）

| 工具名 | 请求类型 | 结果类型 |
|--------|---------|---------|
| `ListAgentTools` | (无请求参数) | `AgentToolResult<IReadOnlyList<AgentToolDescriptor>>` |
| `GetAgentToolDescriptor` | `string` (tool name) | `AgentToolResult<AgentToolDescriptor>` |

---

## 3. AgentDraftPayloadDto 使用 (Usage)

> **注意 (#42)**：`AgentDraftPayloadDto`、其 6 个子 record、`AgentDraftPayloadPatchDto` 以及 ChangedField 枚举现在由 `CrestCreates.CodeGenerator` 中的 `AgentDraftContractGenerator` 编译期生成，不再是手写类型。类型通过 `CrestCreates.Agent.ControlPlane.Abstractions` 中的 global using 别名暴露，因此客户端代码使用 `new AgentDraftPayloadDto()` 的语法完全不变。

### 3.1 构造负载

每个描述符类型有对应的子 record。以下是 6 种负载构造示例：

```csharp
// Capability
var capabilityPayload = new AgentDraftPayloadDto
{
    Discriminator = DescriptorKind.Capability,
    Capability = new AgentCapabilityDraftPayloadDto
    {
        DescriptorRef = new DescriptorRef("default", "cap_process_order"),
        Name = "ProcessOrder",
        DisplayName = "Process Order",
        State = "Active",
        CapabilityKind = "Automation",
        Categories = new[] { "order", "payment" },
        Produces = new[] { new DescriptorRef("event", "order_processed") },
        Consumes = new[] { new DescriptorRef("event", "order_placed") },
        InputSchema = new DescriptorRef("schema", "order_input"),
        OutputSchema = new DescriptorRef("schema", "order_output"),
        RiskLevel = "Low",
        ContractHash = "abc",
        DefinitionHash = "def",
        Version = 1
    }
};

// Workflow
var workflowPayload = new AgentDraftPayloadDto
{
    Discriminator = DescriptorKind.Workflow,
    Workflow = new AgentWorkflowDraftPayloadDto
    {
        DescriptorRef = new DescriptorRef("default", "wf_order_fulfillment"),
        Name = "OrderFulfillment",
        DisplayName = "Order Fulfillment Workflow",
        State = "Active",
        VariableSchema = new DescriptorRef("schema", "wf_variables"),
        ContractHash = "abc",
        DefinitionHash = "def",
        Version = 1
    }
};

// HumanTask
var humanTaskPayload = new AgentDraftPayloadDto
{
    Discriminator = DescriptorKind.HumanTask,
    HumanTask = new AgentHumanTaskDraftPayloadDto
    {
        DescriptorRef = new DescriptorRef("default", "ht_review_order"),
        Name = "ReviewOrder",
        DisplayName = "Review Order",
        State = "Active",
        AssignmentStrategy = "RoundRobin",
        InputSchema = new DescriptorRef("schema", "review_input"),
        OutputSchema = new DescriptorRef("schema", "review_output"),
        Interaction = new DescriptorRef("form", "review_form"),
        Timeout = "01:00:00",
        ContractHash = "abc",
        DefinitionHash = "def",
        Version = 1
    }
};

// Form
var formPayload = new AgentDraftPayloadDto
{
    Discriminator = DescriptorKind.Form,
    Form = new AgentFormDraftPayloadDto
    {
        DescriptorRef = new DescriptorRef("default", "form_review"),
        Name = "ReviewForm",
        DisplayName = "Review Form",
        State = "Active",
        FormSchema = new DescriptorRef("schema", "form_structure"),
        ContractHash = "abc",
        DefinitionHash = "def",
        Version = 1
    }
};

// Event
var eventPayload = new AgentDraftPayloadDto
{
    Discriminator = DescriptorKind.Event,
    Event = new AgentEventDraftPayloadDto
    {
        DescriptorRef = new DescriptorRef("default", "evt_order_placed"),
        Name = "OrderPlaced",
        DisplayName = "Order Placed",
        State = "Active",
        EventKind = "Business",
        EventType = "Notification",
        PayloadSchema = new DescriptorRef("schema", "event_payload"),
        Importance = "Normal",
        ChangeKind = "Evolutionary",
        ContractHash = "abc",
        DefinitionHash = "def",
        Version = 1
    }
};

// Schema
var schemaPayload = new AgentDraftPayloadDto
{
    Discriminator = DescriptorKind.Schema,
    Schema = new AgentSchemaDraftPayloadDto
    {
        DescriptorRef = new DescriptorRef("default", "schema_blog_post"),
        Name = "BlogPost",
        DisplayName = "Blog Post Schema",
        State = "Active",
        SchemaKind = "Evolutionary",
        ContractHash = "abc",
        DefinitionHash = "def",
        Version = 1
    }
};
```

### 3.2 不变式规则

**区分器必须匹配非空子 record**。以下构造会触发验证失败：

```csharp
// ❌ 错误：Discriminator 是 Capability，但填充的是 Schema 子 record
var invalid = new AgentDraftPayloadDto
{
    Discriminator = DescriptorKind.Capability,
    Schema = new AgentSchemaDraftPayloadDto { ... }  // 应该填充 Capability！
};

// ❌ 错误：多个子 record 同时非空
var ambiguous = new AgentDraftPayloadDto
{
    Discriminator = DescriptorKind.Capability,
    Capability = new AgentCapabilityDraftPayloadDto { ... },
    Schema = new AgentSchemaDraftPayloadDto { ... }  // 不能同时存在
};

// ✅ 正确：只有匹配的子 record 非空
var valid = new AgentDraftPayloadDto
{
    Discriminator = DescriptorKind.Capability,
    Capability = new AgentCapabilityDraftPayloadDto { ... }
    // Schema = null, Workflow = null, ...（默认）
};
```

### 3.3 AgentDraftPayloadPatchDto — 构造 Update 负载

> **New in #42**：Update 操作使用 `AgentDraftPayloadPatchDto` 而非 `AgentDraftPayloadDto`。Patch DTO 所有字段均为 nullable，配合 `ChangedFields` 枚举实现字段级合并。

```csharp
// Capability Update 示例 — 只更新 Name 和 State
var capabilityPatch = new AgentDraftPayloadPatchDto
{
    Discriminator = DescriptorKind.Capability,
    ChangedFields = new HashSet<Enum>
    {
        AgentCapabilityDraftChangedField.Name,
        AgentCapabilityDraftChangedField.State
    },
    Capability = new AgentCapabilityDraftPayloadPatchDto
    {
        Name = "UpdatedCapability",
        State = "Active"
        // DescriptorRef, ContractHash 等未设置 → 在 Merge 中保留现有值
    }
};

// Workflow Update 示例 — 清除 InputSchema
var workflowPatch = new AgentDraftPayloadPatchDto
{
    Discriminator = DescriptorKind.Workflow,
    ChangedFields = new HashSet<Enum>
    {
        AgentWorkflowDraftChangedField.InputSchema
    },
    Workflow = new AgentWorkflowDraftPayloadPatchDto
    {
        InputSchema = null  // 显式置空
    }
};
```

#### Patch 合并语义

| 场景 | 结果 |
|------|------|
| 字段在 `ChangedFields` + DTO 非 null | 更新为该值 |
| 字段在 `ChangedFields` + DTO null（可空字段） | 清除为 null |
| 字段在 `ChangedFields` + DTO null（非可空字段） | ❌ `ADPC007` (NonNullableFieldNull) |
| 字段**不在** `ChangedFields` 中 | 保留现有值 |
| `ChangedFields` 含未知 bit | ❌ `ADPC005` (UnknownChangedField) |
| `ChangedFields` 为空 | ❌ `ADPC004` (EmptyChangedFields) |
| 标记为 `[AgentDraftPreserve]` 的字段 | 始终从现有值复制 |

---

## 4. JSON 序列化 (JSON Serialization)

### 4.1 使用 Source-Generated Context

```csharp
using CrestCreates.Agent.ControlPlane.Abstractions.Json;

// 方式一：通过工厂获取预配置选项
var options = AgentControlPlaneToolJsonSerializerOptions.CreateDefault();

// 方式二：手动组合（例如与项目的其他 JsonContext 合并）
var options = new JsonSerializerOptions
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        AgentControlPlaneToolJsonSerializerContext.Default,
        MyProjectJsonSerializerContext.Default)
};
```

### 4.2 完整序列化/反序列化示例

```csharp
// ── 序列化 ──
var request = new CreateDescriptorDraftRequest
{
    DescriptorKind = DescriptorKind.Schema,
    DescriptorId = "schema_blog_post",
    Operation = DescriptorDraftOperation.Create,
    Payload = new AgentDraftPayloadDto
    {
        Discriminator = DescriptorKind.Schema,
        Schema = new AgentSchemaDraftPayloadDto { Name = "BlogPost", ... }
    }
};

string json = JsonSerializer.Serialize(request, options);

// ── 反序列化 ──
var deserialized = JsonSerializer.Deserialize<CreateDescriptorDraftRequest>(json, options);

// ── 工具结果反序列化 ──
var resultJson = """{"status":"Success","value":{"draftId":"...","descriptorKind":"Schema",...},"diagnostics":[]}""";
var result = JsonSerializer.Deserialize<AgentToolResult<AgentDescriptorDraftDto>>(resultJson, options);

if (result.Status == AgentToolResultStatus.Success && result.Value is not null)
{
    var dto = result.Value;
    Console.WriteLine($"Draft {dto.DraftId} for descriptor {dto.DescriptorId}");
}
```

### 4.3 已注册的类型

`AgentControlPlaneToolJsonSerializerContext` 注册了以下类型族：

- **所有 Root DTO** — `AgentDescriptorDraftDto`、`AgentDraftPayloadDto`、6 个子 payload DTO、`DescriptorSummaryDto`、`AgentReviewResultDto`、6 个摘要 DTO
- **所有请求/结果类型** — `CreateDescriptorDraftRequest`、`UpdateDescriptorDraftRequest`、`DescriptorSearchRequest`、`ExplainDiagnosticsRequest`、`ApplyFixProposalRequest`、`SubmitActivationRequestRequest`
- **所有稳定值对象** — `DescriptorRef`、`DescriptorKind`、`DescriptorState`、`RelationshipKind`、`DescriptorStableHashes`、`DescriptorRelationship`
- **所有基础类型** — `AgentToolResult<T>`、`AgentToolResultStatus`、`AgentToolDiagnostic`、`DraftDifference`、`FixProposalAction`、`ActivationRequestStatus`、`AgentToolDescriptor`、`AgentToolCategory`、`AgentToolActorKind` 等

---

## 5. 投影帮助器 (Projection Helpers)

投影帮助器位于 `CrestCreates.Agent.ControlPlane.Projections` 命名空间，在 `ControlPlane` 项目（非 `Abstractions`）。

### 5.1 FromDraft — 领域草稿 → DTO（只读操作的结果）

```csharp
using CrestCreates.Agent.ControlPlane.Projections;

// DescriptorDraft → AgentDescriptorDraftDto
AgentDescriptorDraftDto dto = AgentDescriptorDraftDtoProjection.FromDraft(draft);

// DescriptorDraftReviewResult → AgentReviewResultDto（自动可见性过滤）
AgentReviewResultDto reviewDto = AgentReviewResultDtoProjection.Project(reviewResult, deniedKinds);

// IDescriptor → DescriptorSummaryDto
DescriptorSummaryDto? summary = DescriptorSummaryDtoProjection.FromDescriptor(descriptor);
```

### 5.2 Create — DTO → 领域负载（Create 操作）

> **Updated in #42**：payload 投影已迁移到生成的 `AgentDraftPayloadProjection`。`AgentDescriptorDraftDtoProjection` 将 payload 操作委托给新投影。

```csharp
using CrestCreates.Agent.DraftContracts.Projection;

// 从 CreateDescriptorDraftRequest 提取 Payload，转换为领域负载
DescriptorDraftPayload domainPayload = AgentDraftPayloadProjection.Create(request.Payload);
```

此方法验证区分器不变式。如果区分器不匹配，返回 `ADPC002 (DiscriminatorMismatch)` 错误。验证通过后根据 `Discriminator` 创建对应的 `CapabilityDescriptorDraftPayload` / `WorkflowDescriptorDraftPayload` / 等。

### 5.3 Merge — DTO → 领域负载（Update 操作）

> **Updated in #42**：payload 合并已迁移到生成的 `AgentDraftPayloadProjection`。Merge 使用 `AgentDraftPayloadPatchDto` 而非 `AgentDraftPayloadDto`，返回 `AgentDraftContractResult<T>` 而非直接返回 payload。

```csharp
using CrestCreates.Agent.DraftContracts.Projection;

// 从现有草稿加载领域负载，与 UpdateDescriptorDraftRequest.Payload 合并
DescriptorDraftPayload existingPayload = draftStore.Load(draftId).Payload;
AgentDraftContractResult<DescriptorDraftPayload> result =
    AgentDraftPayloadProjection.Merge(existingPayload, updateRequest.Payload!);

if (result.Error is not null)
{
    // 合并失败 — 处理错误（ADPC004 / ADPC005 / ADPC007）
    return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest(
        new AgentToolDiagnostic
        {
            Code = result.Error.Code,       // 如 "ADPC005"
            Severity = AgentToolDiagnosticSeverity.Error,
            Message = result.Error.Message
        });
}

// 合并成功 — result.Value 包含合并后的领域 payload
DescriptorDraftPayload mergedPayload = result.Value;
```

**合并语义**：只有 `ChangedFields` 中标记的字段才被更新。领域子结构（Steps、Fields、ValidationRules、Outcomes、Permissions 等）从现有 payload 原样保留。Preserve 字段（标记为 `[AgentDraftPreserve]`）始终从现有值复制，不受 `ChangedFields` 影响。

### 5.4 何时使用哪个

| 场景 | 使用 |
|------|------|
| 从领域加载数据并返回给适配器 | `FromDraft` / `Project` |
| 从适配器接收 Create 请求并转换为领域对象 | `AgentDraftPayloadProjection.Create` |
| 从适配器接收 Update 请求并合并到现有草稿 | `AgentDraftPayloadProjection.Merge` |
| 将领域 payload 转换为 DTO | `AgentDraftPayloadProjection.FromDomain` |
| 验证 payload 区分器一致性 | `AgentDraftPayloadProjection.TryValidatePayload` |
| 将 `IDescriptor` 转换为 DTO 摘要 | `FromDescriptor` |
| 将 ReviewResult 返回给适配器，需过滤不可见种类 | `Project(source, deniedKinds)` |

---

## 6. 区分器一致性 (Kind/Discriminator Consistency)

### 6.1 验证规则

Create 和 Update 工具都验证 `DescriptorKind == Payload.Discriminator`。如果 `CreateDescriptorDraftRequest.DescriptorKind` 与 `Payload.Discriminator` 不匹配，工具返回 `InvalidRequest`：

```csharp
// 在 DefaultAgentControlPlaneToolService 中
if (request.DescriptorKind != request.Payload.Discriminator)
{
    return AgentToolResult<AgentDescriptorDraftDto>.InvalidRequest(
    [
        new AgentToolDiagnostic
        {
            Code = "KIND_DISCRIMINATOR_MISMATCH",
            Severity = AgentToolDiagnosticSeverity.Error,
            Message = $"DescriptorKind ({request.DescriptorKind}) does not match Payload.Discriminator ({request.Payload.Discriminator})."
        }
    ]);
}
```

### 6.2 投影层的区分器验证

投影层内的 `ValidateDiscriminator` 方法执行更严格的检查：不仅比较 `Discriminator` 的值，还确保只有匹配的子 record 是非空的。这防止了**字段歧义**：

```csharp
// ValidateDiscriminator 检查：
// - Discriminator == Capability → Capability 非 null，其余全为 null
// - Discriminator == Workflow  → Workflow 非 null，其余全为 null
// - ... 以此类推
//
// 不匹配时抛出 InvalidOperationException
```

---

## 7. 契约版本化 (Contract Versioning)

### 7.1 获取当前版本

```csharp
string version = AgentControlPlaneContractVersion.Current;
// => "7e.v1"
```

### 7.2 从 Manifest 查询版本

```csharp
// 获取所有工具的描述符，每个都包含 ContractVersion
var manifestProvider = services.GetRequiredService<IAgentToolManifestProvider>();
var allTools = manifestProvider.GetAllTools();

foreach (var tool in allTools)
{
    Console.WriteLine($"{tool.Name}: contract={tool.ContractVersion}");
}
// 输出示例:
//   BuildMetadataContextPack: contract=7e.v1
//   CreateDescriptorDraft: contract=7e.v1
//   ListAgentTools: contract=7e.v1
//   ...
```

### 7.3 适配器兼容性检查

```csharp
// 适配器在连接时检查契约版本兼容性
var toolDescriptor = manifestProvider.GetToolByName("CreateDescriptorDraft");
if (toolDescriptor?.ContractVersion != "7e.v1")
{
    // 版本不匹配 — 适配器可能需要升级或降级策略
    throw new AdapterException($"Expected contract 7e.v1, got {toolDescriptor?.ContractVersion}");
}
```

---

## 8. DTO 边界规则 (DTO Boundary Rules)

DTO 层是有意限制的契约边界。以下规则确保适配器无需引用领域程序集：

| 规则 | 说明 | 违反后果 |
|------|------|---------|
| 不暴露 `IDescriptor` | 使用 `DescriptorSummaryDto` 或 `DescriptorRef` | 适配器必须引用 Metadata.Abstractions |
| 不暴露 `IServiceProvider` | DTO 是纯数据 | 反序列化失败 |
| 不暴露 `object`/`dynamic`/`JsonElement` | 所有字段类型确定 | Source Generator 无法生成代码 |
| 不暴露运行时处理程序 | 如 `IDescriptorHandler` | 破坏 AoT 兼容性 |
| 不暴露注册表实例 | 如 `IDescriptorRegistry` | 破坏序列化边界 |
| 使用密封 record | 值语义、JSON 友好 | 反射回退，AoT 不友好 |
| 枚举使用 `string?` | 避免依赖领域枚举 | 适配器需要额外枚举程序集 |

### 8.1 边界验证测试

`ToolDtoBoundaryConstraintTests` 通过递归类型图检查确保没有 DTO 违反上述规则：

```csharp
// 伪代码：边界约束测试逻辑
foreach (var dtoType in AllToolDtoTypes)
{
    Assert.That(dtoType, Does.Not.Implement<IDescriptor>());
    Assert.That(dtoType, Does.Not.Have.PropertyOfType<IServiceProvider>());
    Assert.That(dtoType, Does.Not.Have.PropertyOfType<object>());
    Assert.That(dtoType, Does.Not.Have.PropertyOfType<dynamic>());
    Assert.That(dtoType, Does.Not.Have.PropertyOfType<JsonElement>());
    Assert.That(dtoType, Does.Not.ReferenceAnyRegistryType());
}
```

---

## 9. 测试模式 (Testing Patterns)

### 9.1 DTO 往返测试

验证序列化/反序列化不丢失信息：

```csharp
[Fact]
public void AgentDraftPayloadDto_serializes_and_deserializes_capability()
{
    var payload = new AgentDraftPayloadDto
    {
        Discriminator = DescriptorKind.Capability,
        Capability = new AgentCapabilityDraftPayloadDto
        {
            DescriptorRef = new DescriptorRef("default", "cap_test"),
            Name = "TestCapability",
            // ... 其他字段
        }
    };

    var options = AgentControlPlaneToolJsonSerializerOptions.CreateDefault();
    var json = JsonSerializer.Serialize(payload, options);
    var deserialized = JsonSerializer.Deserialize<AgentDraftPayloadDto>(json, options);

    deserialized.Should().BeEquivalentTo(payload);
}
```

### 9.2 区分器不变式测试

```csharp
[Theory]
[InlineData(DescriptorKind.Capability, nameof(AgentDraftPayloadDto.Capability))]
[InlineData(DescriptorKind.Workflow,  nameof(AgentDraftPayloadDto.Workflow))]
// ...
public void ValidateDiscriminator_returns_error_when_kind_mismatches(
    DescriptorKind discriminator, string populatedProperty)
{
    var dto = new AgentDraftPayloadDto
    {
        Discriminator = discriminator,
        Capability = populatedProperty == nameof(AgentDraftPayloadDto.Capability)
            ? new AgentCapabilityDraftPayloadDto { ... } : null,
        // 故意只填充不匹配的 Capability
    };

    var (isValid, error) = AgentDraftPayloadProjection.TryValidatePayload(dto);
    isValid.Should().BeFalse();
    error.Should().NotBeNull();
    error.Code.Should().Be("ADPC002");
}
```

### 9.3 可见性闭包回归测试

```csharp
[Fact]
public void denied_kinds_are_filtered_from_review_result()
{
    var deniedKinds = new HashSet<DescriptorKind> { DescriptorKind.Schema };
    var reviewResult = CreateSampleReviewResult(); // 包含 Schema 种类的描述符

    var dto = AgentReviewResultDtoProjection.Project(reviewResult, deniedKinds);

    // 验证所有摘要中不包含 Schema kind 的引用
    dto.ProposedInventorySummary.Should().NotBeNull();
    dto.ProposedInventorySummary.DescriptorRefs
        .Should().NotContain(r => r.Namespace == "schema");
}
```

### 9.4 契约覆盖测试

确保所有 32 个工具在 Manifest 和 JSON Context 中都已注册：

```csharp
[Fact]
public void all_32_tools_are_registered_in_manifest()
{
    var manifest = new StaticAgentToolManifestProvider();
    manifest.GetAllTools().Should().HaveCount(32);
}

[Fact]
public void all_tool_request_types_are_in_json_context()
{
    // 通过反射检查 JsonSerializable 特性覆盖了所有请求/结果类型
    var contextType = typeof(AgentControlPlaneToolJsonSerializerContext);
    var serializables = contextType.GetCustomAttributes<JsonSerializableAttribute>();
    serializables.Select(a => a.Type)
        .Should().Contain(typeof(CreateDescriptorDraftRequest))
        .And.Contain(typeof(UpdateDescriptorDraftRequest))
        // ... 验证所有 32 个工具的类型
        ;
}
```

### 9.5 Patch 合并测试模式

验证 Patch Merge 的字段级合并语义：

```csharp
[Fact]
public void Merge_only_updates_changed_fields()
{
    var existing = CreateSampleCapabilityPayload();
    var patch = new AgentDraftPayloadPatchDto
    {
        Discriminator = DescriptorKind.Capability,
        ChangedFields = new HashSet<Enum> { AgentCapabilityDraftChangedField.Name },
        Capability = new AgentCapabilityDraftPayloadPatchDto
        {
            Name = "NewName"
            // State, ContractHash 等未设置
        }
    };

    var result = AgentDraftPayloadProjection.Merge(existing, patch);

    result.Error.Should().BeNull();
    result.Value.Should().NotBeNull();
    result.Value.Name.Should().Be("NewName");           // 更新
    result.Value.State.Should().Be(existing.State);      // 保留
    result.Value.ContractHash.Should().Be(existing.ContractHash); // 保留
}

[Fact]
public void Merge_empty_changed_fields_returns_ADPC004()
{
    var result = AgentDraftPayloadProjection.Merge(existing, new AgentDraftPayloadPatchDto
    {
        Discriminator = DescriptorKind.Capability,
        ChangedFields = new HashSet<Enum>(), // 空！
        Capability = new AgentCapabilityDraftPayloadPatchDto()
    });

    result.Error.Should().NotBeNull();
    result.Error.Code.Should().Be("ADPC004"); // EmptyChangedFields
}
```

### 9.6 生成器诊断回归测试

确保 Spec 文件完整性，防止意外遗漏字段分类：

```csharp
[Fact]
public void all_properties_are_classified_in_capability_spec()
{
    // 通过分析 Spec 特性与 descriptor 类型的属性对比
    // 确保每个属性都有 [AgentDraftField] / [AgentDraftReference] /
    // [AgentDraftPreserve] / [AgentDraftUnsupported] 之一
}

[Fact]
public void contract_manifest_includes_all_six_kinds()
{
    Assert.Equal(6, AgentDraftContractManifest.SupportedKinds.Count);
    Assert.Contains(DescriptorKind.Capability, AgentDraftContractManifest.SupportedKinds);
    Assert.Contains(DescriptorKind.Workflow, AgentDraftContractManifest.SupportedKinds);
    Assert.Contains(DescriptorKind.HumanTask, AgentDraftContractManifest.SupportedKinds);
    Assert.Contains(DescriptorKind.Form, AgentDraftContractManifest.SupportedKinds);
    Assert.Contains(DescriptorKind.Event, AgentDraftContractManifest.SupportedKinds);
    Assert.Contains(DescriptorKind.Schema, AgentDraftContractManifest.SupportedKinds);
}
```

### 9.7 Review Report Builder/Renderer 测试模式 (New in Phase 7d)

验证审查报告 Builder 和 Renderer 的正确性：

```csharp
[Fact]
public void Build_AllSections_AlwaysPresent()
{
    var request = CreateSampleBuildRequest(VisibilityApplied: true);
    var report = _builder.Build(request);

    report.SummarySection.Should().NotBeNull();
    report.DraftIdentitySection.Should().NotBeNull();
    report.ProposedChangesSection.Should().NotBeNull();
    report.ImpactAnalysisSection.Should().NotBeNull();
    report.DependencySummarySection.Should().NotBeNull();
    report.CompatibilitySection.Should().NotBeNull();
    report.GovernanceSection.Should().NotBeNull();
    report.RequiredHumanReviewSection.Should().NotBeNull();
    report.ActivationEligibilitySection.Should().NotBeNull();
    report.DiagnosticsSection.Should().NotBeNull();
    report.RecommendationsSection.Should().NotBeNull();
    report.PackagePreviewSection.Should().NotBeNull();
    report.StableHashesSection.Should().NotBeNull();
    // 13 sections always present, empty sections have IsEmpty=true
}

[Fact]
public void RenderMarkdown_Deterministic()
{
    var report = CreateSampleReport();
    var options = AgentControlPlaneToolJsonSerializerOptions.CreateDefault();

    var output1 = _renderer.RenderMarkdown(report);
    var output2 = _renderer.RenderMarkdown(report);

    output1.Should().Be(output2); // Same DTO → same output
}

[Fact]
public void RenderDescriptorReviewReport_RejectsUnsupportedContractVersion()
{
    var report = CreateSampleReport() with { ContractVersion = "6a.v1" };

    // 在工具服务层面验证
    var format = DescriptorReviewReportFormat.Markdown;
    // Should return UnsupportedReportContractVersion diagnostic
}

[Fact]
public void Build_VisibilityAppliedFalse_ThrowsInvalidOperationException()
{
    var request = CreateSampleBuildRequest(VisibilityApplied: false);
    Action act = () => _builder.Build(request);
    act.Should().Throw<InvalidOperationException>();
}

[Fact]
public void FixProposal_IsExecutable_AggregationRule()
{
    var proposal = new FixProposal
    {
        Id = "fix_1",
        DraftId = "draft_1",
        TenantId = "tenant_1",
        Kind = FixProposalKind.SetRequiredField,
        Title = "Set Rationale",
        Explanation = "Rationale field is empty",
        ReasonCode = "RATIONALE_EMPTY",
        Applicability = FixProposalApplicability.CurrentMutableDraft,
        IsExecutable = true, // Aggregation: Applicability==CurrentMutableDraft && All(IsExecutable)
        RequiresManualAction = false,
        RequiresHumanReview = false,
        BlocksActivationUntilResolved = false,
        RiskLevel = FixProposalRiskLevel.Low,
        Actions = new[] { new FixProposalAction
        {
            Kind = FixProposalActionKind.SetValue,
            TargetPath = "$.Rationale",
            IsExecutable = true,
            SafetyLevel = FixProposalActionSafetyLevel.Safe
        }},
        Diagnostics = [],
        CreatedAt = DateTimeOffset.UtcNow,
        ContractVersion = AgentControlPlaneContractVersion.Current
    };

    proposal.IsExecutable.Should().BeTrue();
}
```

---

## 10. Generator 编译期诊断 (Compile-Time Diagnostics)

> **New in #42**：`AgentDraftContractGenerator` 在编译期执行 spec 文件验证。以下诊断在构建时报告为警告或错误：

| 诊断码 | 严重度 | 说明 |
|--------|--------|------|
| ADP001 | Error | Missing spec class for a known descriptor kind |
| ADP002 | Error | Descriptor property not classified in spec |
| ADP003 | Error | Multiple primary classifications for a property |
| ADP004 | Warning | Preserve field without reason |
| ADP005 | Warning | Missing preserve create strategy |
| ADP006 | Warning | Invalid RequiredOnCreate |
| ADP007 | Error | Nullable/collection conflict |
| ADP008 | Error | Duplicate ContractName |
| ADP009 | Warning | Unstable contract |
| ADP010 | Info | Spec property not found on descriptor type |

当构建出现 ADP001/ADP002/ADP003/ADP007/ADP008 错误时，生成器不会输出任何 payload DTO，确保运行时代码不会引用不完整的生成类型。

---

## 11. Review Report 使用 (Review Report Usage) (Phase 7d)

### 11.1 构建审查报告

使用 `BuildDescriptorReviewReportAsync` 从草稿审查结果构建结构化报告：

```csharp
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.ToolDtos;

// 构建审查报告（需要先有审查结果）
var reportResult = await toolService.BuildDescriptorReviewReportAsync(
    context,
    draftId: "draft_cap_123",
    ct: cancellationToken);

if (reportResult.Status == AgentToolResultStatus.Success && reportResult.Value is not null)
{
    var report = reportResult.Value;
    Console.WriteLine($"Report {report.ReportId} generated at {report.GeneratedAt}");
    Console.WriteLine($"Contract: {report.ContractVersion}, Template: {report.TemplateVersion}");

    // 检查推荐
    foreach (var rec in report.Recommendations)
    {
        Console.WriteLine($"[{rec.Kind}] {rec.Message} (actionable: {rec.IsActionable})");
    }
}
```

### 11.2 渲染审查报告

使用 `RenderDescriptorReviewReportAsync` 将 DTO 渲染为 Markdown 或 PlainText：

```csharp
// 渲染为 Markdown
var markdownResult = await toolService.RenderDescriptorReviewReportAsync(
    context,
    report,
    DescriptorReviewReportFormat.Markdown,
    ct: cancellationToken);

if (markdownResult.Status == AgentToolResultStatus.Success)
{
    Console.WriteLine(markdownResult.Value);
    // 输出示例：
    // # Review Report: draft_cap_123
    // ## Summary
    // Draft validation passed with 3 diagnostics.
    // ## Draft Identity
    // - DraftId: draft_cap_123
    // - DescriptorKind: Capability
    // ...
}
```

**注意**：`RenderDescriptorReviewReportAsync` 直接接受 `DescriptorReviewReportDto`，而非 `reportId`。报告 DTO 是权威制品；内部 `_reports` 字典是可选临时缓存。`RenderStoredDescriptorReviewReportAsync` 内部存在但**不作为工具暴露**。

### 11.3 报告结构概览

`DescriptorReviewReportDto` 包含 13 个固定 Section：

| Section | Kind | 描述 |
|---------|------|------|
| SummarySection | Summary=1 | 聚合严重级别计数、激活资格概述 |
| DraftIdentitySection | DraftIdentity=2 | 草稿身份信息（DraftId、DescriptorKind、Operation 等） |
| ProposedChangesSection | ProposedChanges=3 | 提议库存变更 |
| ImpactAnalysisSection | ImpactAnalysis=4 | 影响分析摘要 |
| DependencySummarySection | DependencySummary=5 | 依赖图节点/边概览 |
| CompatibilitySection | Compatibility=6 | 兼容性评估 |
| GovernanceSection | Governance=7 | 治理决策与理由 |
| RequiredHumanReviewSection | RequiredHumanReview=8 | 需要人工关注的诊断 |
| ActivationEligibilitySection | ActivationEligibility=9 | 激活资格状态（仅解释，非门控） |
| DiagnosticsSection | Diagnostics=10 | 按严重级别分组的所有诊断 |
| RecommendationsSection | Recommendations=11 | 人读的推荐动作 |
| PackagePreviewSection | PackagePreview=12 | 包预览哈希 |
| StableHashesSection | StableHashes=13 | 稳定哈希值 |

每个 Section 的可选性由 `IsEmpty` 标记。Renderer 可选择隐藏空 Section。

### 11.4 ContractVersion 验证

渲染时验证输入 DTO 的 `ContractVersion` 是否匹配当前版本：

```csharp
// 在 DefaultAgentControlPlaneToolService 内部
if (report.ContractVersion != AgentControlPlaneContractVersion.Current)
{
    return AgentToolResult<string>.InvalidRequest(
    [
        new AgentToolDiagnostic
        {
            Code = "UNSUPPORTED_REPORT_CONTRACT_VERSION",
            Severity = AgentToolDiagnosticSeverity.Error,
            Message = $"Report contract version '{report.ContractVersion}' does not match current '{AgentControlPlaneContractVersion.Current}'."
        }
    ]);
}
```

---

## 12. Fix Proposal 使用 — 升级版 (Fix Proposal Usage — Updated) (Phase 7d)

### 12.1 FixProposal 升级概览

Phase 7d 对 `FixProposal` 和 `FixProposalAction` 进行了破坏性契约升级：

| 变化 | 旧 (Phase 7c) | 新 (Phase 7d) |
|------|---------------|---------------|
| 提案 ID | `ProposalId` | `Id` |
| 提案种类 | 无 | `FixProposalKind`（9 值） |
| 适用性 | 无 | `FixProposalApplicability`（4 值） |
| 可执行性 | 无 | `IsExecutable`（聚合规则） |
| 动作路径 | `Path` | `TargetPath` |
| 值类型 | `string` | `JsonElement?` |
| 动作种类 | 3 值（Set/Remove/Add） | 10 值（SetValue 至 ManualActionRequired） |
| 安全级别 | 无 | `FixProposalActionSafetyLevel`（4 值） |
| 契约版本 | 无 | `ContractVersion` |

### 12.2 使用 JsonElement? 值

`FixProposalAction.CurrentValue` 和 `ProposedValue` 现在是 `JsonElement?`：

```csharp
// 构建 FixProposal 时使用 JsonSerializer.SerializeToElement
using System.Text.Json;

var action = new FixProposalAction
{
    Kind = FixProposalActionKind.SetValue,
    TargetPath = "$.Rationale",
    CurrentValue = JsonSerializer.SerializeToElement(""),        // 当前值为空字符串
    ProposedValue = JsonSerializer.SerializeToElement("Added rationale for activation readiness"),
    IsExecutable = true,
    SafetyLevel = FixProposalActionSafetyLevel.Safe,
    Description = "Set Rationale to non-empty value"
};

// 读取时使用 JsonElement 的标准 API
if (action.ProposedValue.HasValue)
{
    string proposedString = action.ProposedValue.Value.GetString() ?? "";
    Console.WriteLine($"Proposed value: {proposedString}");
}
```

**注意**：始终通过 `JsonSerializer.SerializeToElement(...)` 创建。必要时使用 `.Clone()` 避免 `JsonDocument` 生命周期问题。

### 12.3 FixProposalKind 9 值

| 值 | 枚举 | 说明 |
|----|------|------|
| 1 | `CreateMissingDescriptor` | 创建引用的缺失描述符 |
| 2 | `ReplaceMissingReference` | 替换损坏的引用 |
| 3 | `RemoveInvalidRelationship` | 移除无效关系 |
| 4 | `AddRequiredBindingMetadata` | 添加必需的绑定元数据 |
| 5 | `SplitBreakingChangeIntoCompatibleChange` | 将破坏性变更拆分为兼容变更 |
| 6 | `MarkRequiresReview` | 标记需要审查（默认映射） |
| 7 | `FlagUnsafeExpansion` | 标记不安全扩展（不拒绝） |
| 8 | `SuggestVersionBump` | 建议版本号提升 |
| 9 | `SetRequiredField` | 设置必需的字段（由 MapDiagnosticToFixProposalKind 映射） |

### 12.4 MapDiagnosticToFixProposalKind

| 诊断码 | 映射 Kind | 说明 |
|---|---|---|
| `RATIONALE_EMPTY` | `SetRequiredField` | 草稿缺少 Rationale 字段 |
| `INTENT_EMPTY` | `SetRequiredField` | 草稿缺少 Intent 字段 |
| 其他诊断 | `MarkRequiresReview` | 默认映射 |

### 12.5 ApplyFixProposalToDraftAsync — 单动作限制

Phase 7d 仅支持单动作可执行提案。多动作提案被拒绝以避免部分应用：

```csharp
// ✅ 支持：单动作提案
var singleActionResult = await toolService.ApplyFixProposalToDraftAsync(
    context,
    new ApplyFixProposalRequest
    {
        DraftId = "draft_cap_123",
        FixProposalId = "fix_1"
    },
    ct);

// ❌ 拒绝：多动作提案 → UnsupportedMultiActionFixProposal diagnostic
// ❌ 拒绝：Applicability != CurrentMutableDraft → diagnostic
// ❌ 拒绝：action.IsExecutable == false → NonExecutableFixAction diagnostic
// ❌ 拒绝：不支持的 action.Kind → UnsupportedFixActionKind diagnostic
// ❌ 拒绝：SafetyLevel == Unsafe → UnsafeFixActionRejected diagnostic
// ❌ 拒绝：目标为活跃注册表 → FixActionTargetBoundaryViolation diagnostic
```

#### 6 个运行时诊断码

| 诊断码 | 说明 |
|--------|------|
| `NonExecutableFixAction` | 动作不可执行（IsExecutable=false） |
| `UnsupportedMultiActionFixProposal` | 多动作提案不受支持 |
| `UnsupportedFixActionKind` | 不支持的 FixProposalActionKind |
| `UnsafeFixActionRejected` | SafetyLevel=Unsafe 的动作被拒绝 |
| `FixActionTargetBoundaryViolation` | 目标为活跃描述符/注册表 |
| `FixActionTargetNotAllowed` | 目标路径不在允许集合中 |

### 12.6 IsExecutable 聚合规则

```
FixProposal.IsExecutable =
    Applicability == FixProposalApplicability.CurrentMutableDraft
    && Actions.All(a => a.IsExecutable)
```

Builder 强制此规则。混合可执行/不可执行动作的提案为不可执行。

### 12.7 BlocksActivationUntilResolved

这是**解释字段**，不是门控决策。Phase 7d 不拥有激活阻塞权限。激活门控属于 Phase 7e 或后续阶段。

---

## 13. Builder/Renderer 扩展 (Builder/Renderer Extension) (Phase 7d)

### 13.1 自定义 Builder

实现 `IDescriptorReviewReportBuilder` 以提供自定义报告构建逻辑：

```csharp
using CrestCreates.Agent.ControlPlane.Abstractions.ToolDtos;
using CrestCreates.Agent.ControlPlane;

public sealed class CustomReviewReportBuilder : IDescriptorReviewReportBuilder
{
    private readonly TimeProvider _clock;
    private readonly IDescriptorReviewMessageTemplateCatalog _templateCatalog;

    public CustomReviewReportBuilder(
        TimeProvider clock,
        IDescriptorReviewMessageTemplateCatalog templateCatalog)
    {
        _clock = clock;
        _templateCatalog = templateCatalog;
    }

    public DescriptorReviewReportDto Build(DescriptorReviewReportBuildRequest request)
    {
        // Fail-fast: 必须预先应用可见性过滤
        if (!request.VisibilityApplied)
        {
            throw new InvalidOperationException(
                "DescriptorReviewReportBuilder requires a visibility-projected review result. " +
                "Call with VisibilityApplied=true after applying denied descriptor kind filtering.");
        }

        // 自定义构建 13 个 Section 的逻辑
        // 通过 _templateCatalog.Format(templateId, parameters) 填充 Message
        // ReportId = 稳定 hash(TenantId + DraftId + DraftVersion + ReviewResultId + ContractVersion + TemplateVersion)
        // ...
    }
}

// DI 注册
services.AddSingleton<IDescriptorReviewReportBuilder, CustomReviewReportBuilder>();
```

**关键约束**：
- Builder 是投影层，不是可见性/编辑层 — 使用预先过滤的输入
- Builder 使用 `TimeProvider` 实现确定性时间戳
- Builder 使用 `IDescriptorReviewMessageTemplateCatalog`，不直接硬编码措辞字符串
- ReportId 通过 `IDescriptorStableHashBuilder` 生成稳定 SHA256

### 13.2 自定义 Renderer

实现 `IDescriptorReviewReportRenderer` 提供自定义投影格式：

```csharp
public sealed class HtmlReviewReportRenderer : IDescriptorReviewReportRenderer
{
    public string RenderMarkdown(DescriptorReviewReportDto report)
    {
        // 从 DTO 读取 Message 字段，不重新生成文本
        // 不访问注册表、目录或外部服务
        // ...
    }

    public string RenderPlainText(DescriptorReviewReportDto report)
    {
        // 同上，纯文本输出
        // ...
    }
}

// DI 注册
services.AddSingleton<IDescriptorReviewReportRenderer, HtmlReviewReportRenderer>();
```

**Renderer 硬性约束**：
- 仅读取 `DescriptorReviewReportDto` — 不访问注册表、目录或外部服务
- 使用 DTO 的 `Message` 字段 — 不通过 TemplateCatalog 重新生成文本
- **不执行**可见性过滤、治理决策、激活决策
- **不执行**运行时注册表变异、处理程序执行或 LLM 调用
- **确定性输出**：相同 DTO → 相同输出
- 输入 DTO 的 `ContractVersion` 由工具服务在调用前验证

### 13.3 自定义 Message Template Catalog

实现 `IDescriptorReviewMessageTemplateCatalog` 提供自定义措辞：

```csharp
public sealed class LocalizedTemplateCatalog : IDescriptorReviewMessageTemplateCatalog
{
    private readonly IReadOnlyDictionary<string, string> _templates;

    public LocalizedTemplateCatalog()
    {
        _templates = new Dictionary<string, string>
        {
            ["report.activation.eligible"] = "草稿可进行激活交接。",
            ["report.governance.approved"] = "治理决策：已批准。{Rationale}",
            ["report.diagnostics.missing_ref"] = "描述符 '{DescriptorId}' 引用了缺失的 '{ReferenceId}'。",
            // ... 31 个模板，使用 {Param} 格式占位符
        };
    }

    public string Format(string messageTemplateId, IReadOnlyDictionary<string, string> parameters)
    {
        if (!_templates.TryGetValue(messageTemplateId, out var template))
        {
            return $"[unknown template: {messageTemplateId}]";
        }

        // 正则替换 {ParamName} → 参数值
        return Regex.Replace(template, @"\{(\w+)\}", match =>
            parameters.GetValueOrDefault(match.Groups[1].Value, match.Value));
    }
}

// DI 注册
services.AddSingleton<IDescriptorReviewMessageTemplateCatalog, LocalizedTemplateCatalog>();
```

**约束**：
- 模板版本必须在 `TemplateVersion` 中追踪（当前 `"7e.v1"`）
- 未知模板 ID 返回 fallback 消息，不抛异常
- 相同 templateId + 参数 → 相同输出

### 13.4 DI 注册汇总

```csharp
// Phase 7d 默认注册
services.AddSingleton<IDescriptorReviewReportBuilder, DefaultDescriptorReviewReportBuilder>();
services.AddSingleton<IDescriptorReviewReportRenderer, DefaultDescriptorReviewReportRenderer>();
services.AddSingleton<IDescriptorReviewMessageTemplateCatalog, DefaultDescriptorReviewMessageTemplateCatalog>();
```

---

## 14. Safe Activation Workflow 使用 (Safe Activation Workflow Usage) (Phase 7e)

> **Phase 7e (#17)** 实现了从已审查的描述符草稿到运行时激活的完整安全路径。激活请求生命周期通过 `IDescriptorActivationRequestService` 管理，HumanTask 审查通过 `IActivationReviewOrchestrator` 编排，证据完整性通过 `IActivationEvidenceRechecker` 验证，运行时状态变异通过唯一的 `IRuntimeActivationGate` 入口执行。

### 14.1 提交激活请求

```csharp
using CrestCreates.Agent.ControlPlane.Abstractions;
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;

// 从审查结果构建绑定快照
var bindingSnapshot = new ActivationBindingSnapshot
{
    TenantId = tenantId,
    DraftId = draft.DraftId,
    ReviewResultId = reviewResult.ReviewResultId,
    DraftVersion = draft.CurrentVersion,
    ReportId = reviewReport.ReportId,
    PackagePreviewId = packagePreview.PreviewId,     // required — 编译期强制
    EvidencePreviewId = evidencePreview.PreviewId,   // required — 编译期强制
    Hashes = new BindingHashes
    {
        SourceReviewHash = reviewResult.StableHash,
        ReviewManifestHash = reviewHashService.ComputeReviewManifestHash(reviewResult),
        PackageManifestHash = packagePreview.PackageManifestHash,
        PackageEvidenceHash = evidencePreview.PackageEvidenceHash,
        PackageEvidenceEnvelopeHash = evidencePreview.PackageEvidenceEnvelopeHash,
        ContractHash = draft.ContractHash,
        DefinitionHash = draft.DefinitionHash
    },
    CorrelationId = correlationId,
    CreatedAt = timeProvider.GetUtcNow()
};

// 治理决策从审查结果流向激活请求
var request = new SubmitActivationRequestRequest
{
    DraftId = draft.DraftId,
    BindingSnapshot = bindingSnapshot,
    GovernanceDecision = reviewResult.GovernanceDecision?.MaxDecision
};

// 通过工具服务提交
var result = await toolService.SubmitActivationRequestAsync(context, request, ct);
if (result.Status == AgentToolResultStatus.Success && result.Value is not null)
{
    var activationRequest = result.Value;
    Console.WriteLine($"Activation request {activationRequest.RequestId} submitted");
    Console.WriteLine($"Status: {activationRequest.Status}"); // Submitted → UnderReview
    Console.WriteLine($"Policy: {activationRequest.Policy.PolicySummary}");
}
```

### 14.2 治理决策传递

治理决策从审查结果流向激活请求的路径：

```
ReviewDescriptorDraft
  → DescriptorDraftReviewResult
    → GovernanceDecision?.MaxDecision
      → SubmitActivationRequestRequest.GovernanceDecision
        → ActivationRequest.GovernanceDecision (快照固化)
```

ToolService 在 `SubmitActivationRequestAsync` 内部提取决策：

```csharp
// ToolService 内部逻辑（伪代码）
var reviewResult = await ResolveReviewResultAsync(draftId);
var request = new SubmitActivationRequestRequest
{
    DraftId = draftId,
    BindingSnapshot = BuildBindingSnapshot(reviewResult, draft),
    GovernanceDecision = reviewResult.GovernanceDecision?.MaxDecision
};
return await _requestService.SubmitAsync(request);
```

### 14.3 HumanTask 审查流程

激活请求提交后自动创建 HumanTask：

```csharp
// SubmitAsync 内部的审查编排
// → _reviewOrchestrator.CreateReviewTaskAsync(request)

// HumanTask 负载：
var taskInput = new DescriptorActivationReviewTaskInput
{
    ActivationRequestId = request.RequestId,
    DraftId = request.DraftId,
    DescriptorKind = draft.DescriptorKind,
    ReviewSummary = "Draft passed validation. 3 warnings, 0 errors.",
    EvidenceSummary = "Package: 5 descriptors. Evidence: integrity verified.",
    BoundHashes = request.BindingSnapshot.Hashes,
    PackageManifestSummary = "Capability: ProcessOrder, Workflow: OrderFulfillment, Schema: OrderSchema",
    ImpactContext = "Affects 2 dependent workflows. No breaking changes.",
    PackageManifestJson = packageManifestJson
};

// HumanTask 完成后触发 EventBus 回调：
// HumanTaskCompletedEvent → DescriptorActivationReviewHumanTaskEventHandler
// → 解析 DescriptorActivationReviewDecision → 路由到 RequestService
```

### 14.4 审批/拒绝操作

审批时进行证据绑定验证和 ActivationRequestId 匹配：

```csharp
// 审批决策结构
var reviewDecision = new DescriptorActivationReviewDecision
{
    ActivationRequestId = activationRequestId,
    ActorId = reviewerUserId,
    ActorKind = DescriptorActivationActorKind.Human,
    Decision = DescriptorActivationReviewOutcome.Approved,
    BoundEvidenceHash = request.BindingSnapshot.Hashes.PackageEvidenceHash,  // 绑定到证据快照
    BoundEnvelopeHash = request.BindingSnapshot.Hashes.PackageEvidenceEnvelopeHash    // 绑定到信封快照
};

// ApproveAsync 内部验证序列：
// 1. reviewDecision.ActivationRequestId == request.RequestId
//    → 不匹配: ACTIVATION_REVIEW_REQUEST_MISMATCH
// 2. reviewDecision.Decision == DescriptorActivationReviewOutcome.Approved
//    → 不匹配: ACTIVATION_REVIEW_DECISION_MISMATCH
// 3. reviewDecision.BoundEvidenceHash == request.BindingSnapshot.Hashes.PackageEvidenceHash
//    → 不匹配: ACTIVATION_REVIEW_EVIDENCE_MISMATCH（证据漂移）
// 4. reviewDecision.BoundEnvelopeHash == request.BindingSnapshot.Hashes.PackageEvidenceEnvelopeHash
//    → 不匹配: ACTIVATION_REVIEW_ENVELOPE_MISMATCH（信封漂移）
// 5. → _evidenceRechecker.RecheckAsync(request)
//    → 所有 7 字段 CanonicalHash 比较通过
// 6. → _runtimeActivationGate.ActivateAsync(request)
//    → 唯一运行时状态变异入口

// 拒绝决策
var rejectDecision = new DescriptorActivationReviewDecision
{
    ActivationRequestId = activationRequestId,
    ActorId = reviewerUserId,
    ActorKind = DescriptorActivationActorKind.Human,
    Decision = DescriptorActivationReviewOutcome.Rejected,
    BoundEvidenceHash = request.BindingSnapshot.Hashes.PackageEvidenceHash,
    BoundEnvelopeHash = request.BindingSnapshot.Hashes.PackageEvidenceEnvelopeHash
};

// RejectAsync 内部验证：
// 1. reviewDecision.ActivationRequestId == request.RequestId
//    → 不匹配: ACTIVATION_REVIEW_REQUEST_MISMATCH
// 2. request.Status 允许拒绝（非 Approved/Rejected/Cancelled/Expired）
//    → 不允许: ACTIVATION_INVALID_STATUS_FOR_REJECTION
```

### 14.5 证据重校验

证据重校验在审批前执行，比较全部 7 个 BindingHashes 字段：

```csharp
// IActivationEvidenceRechecker.RecheckAsync(request)

// 重校验逻辑（伪代码）：
// 1. 通过 IActivationBindingArtifactResolver 解析当前制品哈希
// 2. 比较 BindingHashes 的全部 7 个字段，使用 CanonicalHash 记录相等性：
//    - SourceReviewHash: 审查结果哈希是否一致
//    - ReviewManifestHash: 审查结果 manifest 哈希是否一致
//    - PackageManifestHash: 包清单哈希是否一致
//    - PackageEvidenceHash: 证据哈希是否一致
//    - PackageEvidenceEnvelopeHash: 证据信封哈希是否一致
//    - ContractHash: 契约哈希是否一致
//    - DefinitionHash: 定义哈希是否一致
// 3. 返回重校验结果

// 使用示例：
var evidenceResult = await _evidenceRechecker.RecheckAsync(activationRequest);
if (!evidenceResult.AllMatch)
{
    // 处理漂移字段
    foreach (var driftedField in evidenceResult.DriftedFields)
    {
        Console.WriteLine($"Hash drift detected in {driftedField}: " +
            $"request has {evidenceResult.RequestHashes[driftedField]}, " +
            $"current is {evidenceResult.CurrentHashes[driftedField]}");
    }
}
```

### 14.6 AoT 安全决策解析

审查决策通过 `DescriptorActivationReviewDecisionParser` 以 AoT 安全方式解析：

```csharp
using CrestCreates.Agent.ControlPlane.Abstractions.Activation;

// 从 HumanTask 完成事件的 JSON 负载解析
string decisionJson = eventData.DecisionPayload;
if (DescriptorActivationReviewDecisionParser.TryParse(decisionJson, out var decision))
{
    switch (decision.Decision)
    {
        case DescriptorActivationReviewOutcome.Approved:
            await _requestService.ApproveActivationRequestAsync(decision);
            break;
        case DescriptorActivationReviewOutcome.Rejected:
            await _requestService.RejectActivationRequestAsync(decision);
            break;
    }
}
else
{
    // 解析失败 — 记录审计
    await _auditor.RecordAsync(new DescriptorActivationAuditRecord
    {
        ActivationRequestId = eventData.ActivationRequestId,
        Action = DescriptorActivationAuditAction.Block,
        Reason = "Failed to parse review decision from HumanTask"
    });
}
```

### 14.7 自我审批检查

自我审批使用激活请求创建时捕获的策略快照：

```csharp
// request.Policy 在 SubmitAsync 时捕获 — 不是实时查询
if (request.Policy.AllowSelfApproval)
{
    // 允许相同 ActorId 的审批
}
else if (request.Policy.ForbidSelfApproval)
{
    // 拒绝自我审批 → Block audit action
    var reason = $"Self-approval forbidden by policy: {request.Policy.PolicySummary}";
}
else
{
    // 回退策略：使用 snapshot.Owner.DescriptorKind 查询
    var fallbackPolicy = await _policyProvider.GetPolicyAsync(
        request.TenantId,
        request.BindingSnapshot.Owner?.DescriptorKind ?? DescriptorKind.Unknown);
}
```

### 14.8 激活诊断码参考

所有 Phase 7e 激活诊断码使用 SCREAMING_SNAKE_CASE：

| 诊断码 | 触发条件 | Audit Action |
|--------|----------|--------------|
| `ACTIVATION_BINDING_SNAPSHOT_REQUIRED` | SubmitAsync — BindingSnapshot 为 null | Block |
| `ACTIVATION_BINDING_HASHES_REQUIRED` | SubmitAsync — Hashes 为 null | Block |
| `ACTIVATION_INCOMPLETE_BINDING` | SubmitAsync — PackagePreviewId 为 IsNullOrWhiteSpace | Block |
| `ACTIVATION_INCOMPLETE_EVIDENCE_BINDING` | SubmitAsync — EvidencePreviewId 为 IsNullOrWhiteSpace | Block |
| `ACTIVATION_REVIEW_REQUEST_MISMATCH` | ApproveAsync/RejectAsync — ActivationRequestId 不匹配 | Block |
| `ACTIVATION_REVIEW_DECISION_MISMATCH` | ApproveAsync — 决策种类不是 Approved | Block |
| `ACTIVATION_REVIEW_EVIDENCE_MISMATCH` | ApproveAsync — BoundEvidenceHash 漂移 | GateDenied |
| `ACTIVATION_REVIEW_ENVELOPE_MISMATCH` | ApproveAsync — BoundEnvelopeHash 漂移 | GateDenied |
| `ACTIVATION_INVALID_STATUS_FOR_REJECTION` | RejectAsync — 请求状态不允许拒绝 | Block |

### 14.9 DI 注册

```csharp
// Phase 7e 激活组件注册
services.AddSingleton<IDescriptorActivationRequestService, DefaultDescriptorActivationRequestService>();
services.AddSingleton<IDescriptorActivationPolicyProvider, DefaultDescriptorActivationPolicyProvider>();
services.AddSingleton<IDescriptorActivationAuditor, DefaultDescriptorActivationAuditor>();
services.AddSingleton<IActivationEvidenceRechecker, DefaultActivationEvidenceRechecker>();
services.AddSingleton<IRuntimeActivationGate, DefaultRuntimeActivationGate>();
services.AddSingleton<IActivationReviewOrchestrator, DefaultActivationReviewOrchestrator>();
services.AddSingleton<IActivationBindingArtifactResolver, DefaultActivationBindingArtifactResolver>();

// EventBus 处理程序
services.AddTransient<DescriptorActivationReviewHumanTaskEventHandler>();
```

### 14.10 关键使用约束

| 约束 | 说明 |
|------|------|
| **BindingSnapshot 为 required** | `PackagePreviewId` 和 `EvidencePreviewId` 为 `required string`，编译期强制非空 |
| **自我审批用快照策略** | `request.Policy` 在创建时捕获，不使用实时查询 |
| **证据重校验比较 7 字段 CanonicalHash** | 使用完整的 `CanonicalHash` 记录相等性，不只用 `.Value` 摘要 |
| **IRuntimeActivationGate 是唯一变异入口** | 任何代码路径都不应在 Gate 之外修改运行时注册表 |
| **审批绑定到证据快照** | `BoundEvidenceHash` / `BoundEnvelopeHash` 必须与激活请求快照的 `Hashes.PackageEvidenceHash` / `Hashes.PackageEvidenceEnvelopeHash` 匹配 |
| **ActivationRequestId 绑定** | 审批和拒绝路径均检查 `reviewDecision.ActivationRequestId == request.RequestId` |
| **审计动作语义** | 验证失败用 `Block` 或 `GateDenied`，不用 `Reject`（后者表示人工拒绝） |
| **ToolService 不维护双轨逻辑** | ToolService 委托给 `_requestService`，无独立的激活代码路径 |

---

## 15. LLM 集成 (Implemented as Phase 7g)

Phase 7b 的 LLM 集成能力已由 Phase 7g 实现。核心映射：

| Phase 7b 设计 | Phase 7g 实现 |
|---------------|---------------|
| `ILLMProvider` | `IDescriptorAuthoringModelClient` |
| `DescriptorDraftBuilder` | `JsonDescriptorAuthoringOutputParser` |
| `PromptTemplate` | `DefaultDescriptorAuthoringPromptBuilder` |
| `PromptTemplateRegistry` | `DefaultDescriptorAuthoringPromptInputFactory` |

LLM 产出的 draft 经过与人工草稿相同的审查/治理/激活管线。参见 §18 获取完整使用说明。

---

## 16. Agent Memory & Context Compression Runtime 使用 (Phase 7e+)

> **Phase 7e+ (#43)** 实现了从对话/任务历史到压缩上下文、记忆候选提取、晋升/召回、源扩展和 AuthoringContext 组装的完整链路。所有合约类型使用 `CanonicalHash` 标识内容身份和完整性。

### 16.1 DI 注册

```csharp
// 一次性注册所有 Agent Memory 服务
services.AddAgentMemoryRuntime();

// 等价于注册 11 个默认服务 + AgentMemoryCanonicalHashProjector + TimeProvider.System
```

### 16.2 内容脱敏

所有内容在进入 Memory 系统前必须经过脱敏：

```csharp
var sanitizer = services.GetRequiredService<IAgentMemoryContentSanitizer>();

var result = sanitizer.Sanitize(
    tenantId: "tenant_1",
    content: "User mentioned bearer token: Bearer eyJhbGciOi...",
    sourceRefs: new[] { new AgentContextSourceRef { ... } });

if (result.Rejected)
{
    // 内容被完全拒绝（所有内容都被脱敏）
    Console.WriteLine($"Content rejected: {string.Join(", ", result.RedactionKinds)}");
}
else
{
    // result.SanitizedContent — 脱敏后内容
    // result.CanonicalContentHash — 内容身份哈希
}
```

脱敏规则：
- Bearer token（`Bearer ...`）
- Credential 模式（`password=...`, `secret=...`）
- Connection string（`Server=...;Password=...`）
- Long base64 token（>100 字符的 base64 字符串）

### 16.3 上下文压缩

对话和任务历史可以压缩为上下文块：

```csharp
var compressor = services.GetRequiredService<IAgentContextCompressor>();

// 压缩对话
var conversation = new AgentConversationRecord
{
    ConversationId = "conv_1",
    TenantId = "tenant_1",
    Turns = turns // AgentConversationTurn[]
};
var compressed = await compressor.CompressConversationAsync(conversation, ct);
// compressed.Blocks — AgentCompressedContextBlock[]
// 每个 block 有 CanonicalContentHash 和 SourceRefs

// 压缩任务历史
var task = new AgentTaskRecord { ... };
var compressedTask = await compressor.CompressTaskAsync(task, ct);
```

### 16.4 记忆候选提取与晋升

```csharp
var extractor = services.GetRequiredService<IAgentMemoryExtractor>();
var promotionService = services.GetRequiredService<IAgentMemoryPromotionService>();

// 从压缩上下文提取候选
var candidates = await extractor.ExtractCandidatesAsync(compressedContext, ct);
// 每个 candidate: Kind=ProjectFact, Confidence=Low, Status=Candidate

// 晋升为正式记忆
var operationRequest = new AgentMemoryOperationRequest
{
    TenantId = "tenant_1",
    InvocationContext = new AgentMemoryInvocationContext
    {
        TenantId = "tenant_1",
        ActorId = "agent_1",
        ActorKind = DescriptorActivationActorKind.Agent,
        Reason = "High confidence project fact"
    },
    Reason = "Verified project constraint",
    Timestamp = DateTimeOffset.UtcNow,
    SourceRefs = candidate.SourceRefs
};

var promoted = await promotionService.PromoteAsync("tenant_1", candidate.CandidateId, operationRequest, ct);
// promoted — AgentMemoryItem (Status=Active, IsAuthoritative=false)

// 其他生命周期操作
await promotionService.RejectAsync(tenantId, candidateId, operationRequest, ct);
await promotionService.SupersedeAsync(tenantId, memoryId, newMemoryId, operationRequest, ct);
await promotionService.ArchiveAsync(tenantId, memoryId, operationRequest, ct);
```

### 16.5 记忆召回

```csharp
var retriever = services.GetRequiredService<IAgentMemoryRetriever>();

var query = new AgentMemoryQuery
{
    TenantId = "tenant_1",
    IntentText = "What are the project constraints for company certification?",
    Kinds = new HashSet<AgentMemoryKind> { AgentMemoryKind.Constraint, AgentMemoryKind.ProjectFact },
    VisibleDescriptorRefs = new HashSet<string> { "wf_company_certification", "cap_approve_certification" },
    VisibleDescriptorKinds = new HashSet<DescriptorKind> { DescriptorKind.Workflow, DescriptorKind.Capability },
    CharacterBudget = 2000,
    MinimumConfidence = AgentMemoryConfidence.Medium
};

var memoryPack = await retriever.RecallAsync(query, ct);
// memoryPack.Memories — 排序后的 AgentMemoryItem 列表（置信度→种类→晋升时间→ID）
// memoryPack.IsAuthoritative — 始终为 false
// memoryPack.ScopeFingerprint — 查询范围指纹（CanonicalHash）
// memoryPack.VisibleMemorySetHash — 可见记忆集合哈希（CanonicalHash）
// memoryPack.CanonicalPackHash — 整体包哈希（CanonicalHash）
```

**关键约束**：
- `IsAuthoritative` 始终为 `false` — 元数据优先于冲突记忆
- `VisibleDescriptorKinds` fail-closed — 无法解析的值导致返回空结果
- 排序确定性：Confidence(desc) → Kind(ordinal) → PromotedAt(desc) → MemoryId(ordinal)

### 16.6 源扩展

```csharp
var expander = services.GetRequiredService<IAgentContextSourceExpander>();

var sourceRef = new AgentContextSourceRef
{
    SourceKind = AgentSourceKind.ConversationTurn,
    TenantId = "tenant_1",
    SourceId = "conv_1"
};

var expansion = await expander.ExpandAsync(sourceRef, ct);
// expansion.Status — Expanded / NotExpandable / NotFound / Redacted
// expansion.SanitizedContent — 扩展后的脱敏内容（如果 Status == Expanded）
```

SourceKind 分发规则：

| SourceKind | 目标 Store |
|------------|-----------|
| ConversationTurn | IAgentConversationStore |
| TaskRecord / TaskEvent | IAgentTaskHistoryStore |
| CompressedContextBlock | IAgentCompressedContextStore |
| MemoryCandidate / MemoryItem | IAgentMemoryStore |
| 其他 | NotExpandable |

### 16.7 AuthoringContext 组装

```csharp
var authoringBuilder = services.GetRequiredService<IAgentAuthoringContextBuilder>();

// 调用者负责构建 MetadataContextPack 和 AgentMemoryPack
var metadataPack = await metadataPackBuilder.BuildAsync(request, topology, descriptors, ct);
var memoryPack = await retriever.RecallAsync(query, ct);

var authoringContext = await authoringBuilder.BuildAsync(
    new AgentAuthoringRequest
    {
        TenantId = "tenant_1",
        IntentText = "Add second-level finance review before approving company certification"
    },
    metadataPack,
    memoryPack,
    ct);

// authoringContext.Request — AgentAuthoringRequest
// authoringContext.MetadataContextPack — MetadataContextPack
// authoringContext.MemoryPack — AgentMemoryPack (IsAuthoritative=false)
// authoringContext.Diagnostics — 诊断列表
```

**关键设计**：`IAgentAuthoringContextBuilder.BuildAsync` 不内部调用 retriever。调用者传入预构建的 `AgentMemoryPack`，确保调用者控制记忆召回策略和预算。

### 16.8 JSON 序列化

```csharp
using CrestCreates.Agent.Memory.Abstractions.Json;

// AgentMemoryJsonSerializerContext 注册了 19 个 Root 类型
var options = new JsonSerializerOptions
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        AgentControlPlaneToolJsonSerializerContext.Default,
        AgentMemoryJsonSerializerContext.Default)
};

var json = JsonSerializer.Serialize(memoryPack, options);
var deserialized = JsonSerializer.Deserialize<AgentMemoryPack>(json, options);
```

### 16.9 关键使用约束

| 约束 | 说明 |
|------|------|
| **IsAuthoritative 始终为 false** | Agent 不应将记忆视为权威真相；元数据优先于冲突记忆 |
| **CanonicalHash 贯穿所有合约** | 内容身份和完整性通过 CanonicalHash 标识，不使用 string |
| **Snapshot-on-read** | 所有 InMemory Store 返回防御性拷贝，防止外部修改内部状态 |
| **VisibleDescriptorKinds fail-closed** | 无法解析的 DescriptorKind 值导致返回空结果 |
| **MemoryPack 由调用者构建** | IAgentAuthoringContextBuilder 不内部调用 retriever |
| **Memory.Abstractions 不引用 ControlPlane.Abstractions** | 依赖边界由 Boundary 测试强制执行 |

---

## 17. AI-assisted Descriptor Authoring Golden Scenario 使用 (Phase 7f)

> **Phase 7f (#32)** 在 Phase 7e+ Agent Memory 基础之上，实现了从意图文本到描述符草稿创作、审查、治理、激活绑定、运行时证明的完整端到端链路。所有新增类型均在 sample 项目中，不修改框架核心合约。

### 17.1 IDescriptorAuthoringAgent 接口

```csharp
public interface IDescriptorAuthoringAgent
{
    Task<DescriptorAuthoringResult> AuthorAsync(AgentAuthoringContext context, CancellationToken ct);
}
```

Agent 仅消费 `AgentAuthoringContext`（含 Request、MetadataContextPack、MemoryPack），产出 `DescriptorAuthoringResult`（含 `DescriptorDraftSet`）。Agent 不访问 raw memory stores、runtime handlers、activation gate 或任何 Control Plane 内部服务。

### 17.2 FakeCompanyCertificationAuthoringAgent

确定性假 Agent，用于 golden scenario 测试：

```csharp
// DI 注册
services.AddSingleton<IDescriptorAuthoringAgent, FakeCompanyCertificationAuthoringAgent>();

// 使用
var agent = services.GetRequiredService<IDescriptorAuthoringAgent>();
var result = await agent.AuthorAsync(authoringContext, ct);

// result.DraftSet.Drafts 包含 2 个 draft：
// 1. HumanTask: ht_finance_review_company_certification
// 2. Workflow update: wf_company_certification + step_finance_review
```

**约束**：
- 无构造函数依赖 — 不注入任何服务
- 仅消费 `AgentAuthoringContext.Request.TenantId` 和 `Request.IntentText`
- 输出确定性 — 相同输入 → 相同 draft set
- 不使用 raw memory stores（IAgentMemoryStore 等）
- 不访问 runtime handlers 或 activation gate

### 17.3 CompanyCertificationAuthoringGoldenScenarioRunner

三方法编排器：

```csharp
var runner = services.GetRequiredService<CompanyCertificationAuthoringGoldenScenarioRunner>();

// 方法 1：意图 → 创作 → 审查 → final governance
var reviewResult = await runner.RunUntilDraftSetReviewAsync(
    intentText: "Add second-level finance review before approving company certification",
    startingInventory: baselineDescriptors,
    ct: cancellationToken);

// reviewResult.IsBlocked — 是否被 block
// reviewResult.FinalProposedInventory — 最终提议清单
// reviewResult.FinalDecisionSource — 决策来源
// reviewResult.FinalImpact / FinalCompat — 基于 inventory diff 的最终影响/兼容性
// reviewResult.FinalGovernance — 最终治理决策

// 方法 2：完整链路（+ 激活 + 运行时证明）
var fullReport = await runner.RunAsync(
    intentText: "Add second-level finance review before approving company certification",
    ct: cancellationToken);

// fullReport.AuthoringSucceeded — 创作是否成功
// fullReport.DraftSetBlocked — Draft set 是否被 block
// fullReport.RuntimeActivationGateSucceeded — 激活门是否通过
// fullReport.RuntimeProofUsedFreshActivatedHost — 是否使用 fresh host 证明
// fullReport.ActivatedWorkflowDescriptorId — 激活的 Workflow ID
// fullReport.WorkflowStepSequence — Workflow 步骤序列

// 方法 3：到激活门为止
var activationReport = await runner.RunActivationOnlyAsync(
    intentText: "Add second-level finance review before approving company certification",
    ct: cancellationToken);
```

### 17.4 Draft Set 原子性

Draft set 实现原子性——全部 draft 创建成功或全部 block：

```csharp
// 如果任何一个 draft 的 materialization 失败：
// → IsBlocked = true
// → BlockReason = "Draft set materialization failed for draft: ..."

// 如果 final inventory 的 topology 构建失败：
// → IsBlocked = true
// → BlockReason = "Final topology build failed: ..."

// 如果 final governance 评估失败：
// → IsBlocked = true
// → BlockReason = "Final governance evaluation failed: ..."
```

### 17.5 Final Decision 基于 Inventory Diff

Final scenario-level decision 使用完整 inventory diff，不取最后一个 draft review 的结果：

```csharp
// Runner 内部逻辑（伪代码）：
var changeSet = changeSetBuilder.Build(startingInventory, finalProposedInventory);
var finalImpact = impactAnalyzer.Analyze(topology, changeSet);
var finalCompat = compatibilityAnalyzer.Analyze(startingInventory, finalProposedInventory, changeSet, finalImpact);
var finalGovernance = governanceService.EvaluateGovernance(governanceRequest with {
    ImpactReport = finalImpact,
    CompatibilityReport = finalCompat
});
```

这确保了 3+ draft 场景下，final decision 基于完整的 baseline→final 变更，而非最后一个 draft 的局部视图。

### 17.6 激活绑定使用真实 Hash

所有 7 个 BindingHashes slot 使用真实 hash 计算，无 placeholder fallback：

| Slot | 计算方式 |
|------|---------|
| SourceReviewHash | `IDescriptorDraftReviewHashService.ComputeSourceReviewHash(draft, reviewResult)` |
| ReviewManifestHash | `IDescriptorDraftReviewHashService.ComputeReviewManifestHash(draft, reviewResult)` |
| PackageManifestHash | `IDescriptorPackageBuilder.Build(request).Hashes.PackageManifestHash` |
| PackageEvidenceHash | `IDescriptorPackageBuilder.Build(request).Hashes.PackageEvidenceHash` |
| PackageEvidenceEnvelopeHash | `IDescriptorPackageBuilder.Build(request).Hashes.PackageEvidenceEnvelopeHash` |
| ContractHash | `IDescriptorStableHashBuilder.Build(descriptor).ContractHash` |
| DefinitionHash | `IDescriptorStableHashBuilder.Build(descriptor).DefinitionHash` |

如果任何 hash 计算失败或返回 null → 激活被 block。

### 17.7 绑定引用注册与验证

`ActivationBindingReferenceRegistry` 在 artifact 创建点注册引用，激活前只读验证：

```csharp
var registry = services.GetRequiredService<ActivationBindingReferenceRegistry>();

// 在 artifact 创建时注册（review result 创建后）
registry.RegisterReviewResult(tenantId, reviewResultId, draftId);

// 在 package preview 创建后
registry.RegisterPackagePreview(tenantId, packagePreviewId, draftId);

// 在 evidence preview 创建后
registry.RegisterEvidencePreview(tenantId, evidencePreviewId, draftId);

// 在激活前验证（只读）
var validation = registry.ValidateReferences(
    tenantId, draftId, reviewResultId, packagePreviewId, evidencePreviewId);

if (!validation.IsValid)
{
    // validation.Errors — 引用不存在或 DraftId 不匹配
    // → 激活被 block
}
```

等价于 Control Plane 内部 `_reviewResults`/`_packagePreviews`/`_evidencePreviews` 字典 + DraftId mismatch 校验。

### 17.8 运行时证明

运行时证明从 approved final inventory 构建新 host：

```csharp
// Runner 内部逻辑（伪代码）：
var freshHost = new CompanyCertificationGoldenScenarioHost(
    runtimeInventory: reviewResult.FinalProposedInventory);
var freshRunner = freshHost.GetRequiredService<CompanyCertificationGoldenScenarioRunner>();

// 在 fresh host 上执行 workflow
// → 验证 activated descriptors 可执行
// → 完成 HumanTask instances
// → 验证 workflow step sequence
```

**关键约束**：不使用原始 host。Fresh host 确保激活后的描述符在独立 runtime 中可执行。

### 17.9 关键使用约束

| 约束 | 说明 |
|------|------|
| **FakeAgent 无构造函数依赖** | 仅消费 AgentAuthoringContext，不注入任何服务 |
| **Draft set 原子性** | 全部成功或全部 block，无部分成功 |
| **Final decision 基于 inventory diff** | 不取最后一个 draft review 的 impact/compat |
| **激活绑定无 placeholder** | 所有 7 slot 使用真实 hash，缺 hash 即 block |
| **引用注册在创建点** | 激活前只读验证，不在验证前补登记 |
| **运行时证明用 fresh host** | 不使用原始 host，确保独立 runtime |
| **IsAuthoritative 始终 false** | 元数据优先于冲突记忆 |
| **CreatedAt 固定时间** | GoldenScenarioCreatedAt = 2026-01-01T00:00:00Z |
| **不修改框架核心合约** | 所有新增类型在 sample 项目中 |

---

## 18. LLM-backed Descriptor Authoring Adapter 使用 (Phase 7g)

> **Phase 7g (#48)** 实现了 LLM-backed Descriptor Authoring Adapter——将 Phase 7f 的 sample-level authoring 合约产品化为框架级项目，并引入 LLM 提供者适配层。LLM agent 只产出 draft，不激活、不审批、不变异注册表。

### 18.1 DI 注册

```csharp
// 基础 Authoring runtime（使用 FakeClient）
services.AddDescriptorAuthoring();

// 替换为 OpenAI-compatible provider
services.AddOpenAICompatibleAuthoringProvider(
    providerName: "openai",
    credentialReference: "Authoring:OpenAI:ApiKey",
    endpoint: new Uri("https://api.openai.com/v1"));

// 配置 Agent options
services.Configure<LlmDescriptorAuthoringAgentOptions>(options =>
{
    options.AuthorId = "my-llm-authoring-agent";
    options.ModelProfile = new DescriptorAuthoringModelProfile
    {
        ProfileName = "production",
        ProviderName = "openai",
        ModelName = "gpt-4o",
        MaxOutputTokens = 4096,
        SupportsJsonMode = true,
        SupportsStructuredOutput = true
    };
});
```

### 18.2 使用 LLM Authoring Agent

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Authoring;

// Agent 通过 DI 注入
var agent = services.GetRequiredService<IDescriptorAuthoringAgent>();

// AuthoringContext 由 Agent Memory 系统构建
var authoringContext = await authoringContextBuilder.BuildAsync(
    request, metadataPack, memoryPack, ct);

// 调用 LLM agent
var result = await agent.AuthorAsync(authoringContext, ct);

// 检查结果
switch (result.Status)
{
    case DescriptorAuthoringStatus.Succeeded:
    case DescriptorAuthoringStatus.SucceededWithDiagnostics:
        // result.Plan — DescriptorAuthoringPlan (PlanId, IntentText, PlannedDescriptorRefs, Assumptions)
        // result.DraftSet — DescriptorDraftSet (Drafts 列表)
        // result.Diagnostics — 可能有非阻塞诊断
        break;

    case DescriptorAuthoringStatus.Blocked:
        // 治理边界拒绝（prompt hash mismatch、authority claim、unsupported operation 等）
        break;

    case DescriptorAuthoringStatus.InvalidProviderOutput:
        // LLM 输出无法解析（JSON 格式错误、contract version 不匹配）
        break;

    case DescriptorAuthoringStatus.ProviderUnavailable:
        // 提供者不可用（credential、network、timeout 等）
        break;

    case DescriptorAuthoringStatus.Failed:
        // 未知失败
        break;
}
```

### 18.3 Provider Failure 诊断

当 provider 返回失败时，`DescriptorAuthoringModelResponse.FailureKind` 携带结构化失败信息：

| FailureKind | 诊断码 | 触发条件 |
|-------------|--------|---------|
| `CredentialUnavailable` | `AUTHORING_CREDENTIAL_UNAVAILABLE` | API key 配置缺失 |
| `CredentialRejected` | `AUTHORING_CREDENTIAL_REJECTED` | HTTP 403 |
| `Unauthorized` | `AUTHORING_PROVIDER_UNAUTHORIZED` | HTTP 401 |
| `RateLimited` | `AUTHORING_PROVIDER_RATE_LIMITED` | HTTP 429 |
| `Timeout` | `AUTHORING_PROVIDER_TIMEOUT` | 请求超时（ProviderProfile.Timeout） |
| `NetworkError` | `AUTHORING_PROVIDER_UNAVAILABLE` | HttpRequestException |
| `Unknown` | `AUTHORING_PROVIDER_UNAVAILABLE` | fixture lookup 失败、非特定错误 |

### 18.4 Recorded Client（确定性测试）

`RecordedDescriptorAuthoringModelClient` 按 prompt input hash 查找预录制的 fixture 响应：

```csharp
// 构造 recorded client
var fixtures = new Dictionary<string, string>
{
    [expectedPromptInputHash] = File.ReadAllText("fixtures/company_certification_authoring.json")
};
var recordedClient = new RecordedDescriptorAuthoringModelClient(fixtures);

// 当 hash 匹配时返回 fixture；不匹配时返回 FailureKind=Unknown
```

### 18.5 Parser 严格验证

Parser 拒绝以下情况并返回 `Blocked`：

| 情况 | 诊断码 |
|------|--------|
| Prompt input hash 不匹配 | `AUTHORING_PROMPT_HASH_MISMATCH` |
| 不支持的 DescriptorKind | `AUTHORING_UNKNOWN_DESCRIPTOR_KIND` |
| 不支持的 DraftOperation | `AUTHORING_UNSUPPORTED_DRAFT_OPERATION` |
| LLM 尝试激活/审批/变异 | `AUTHORING_GOVERNANCE_BOUNDARY_VIOLATION` |
| LLM 声称记忆权威 | `AUTHORING_MEMORY_AUTHORITY_CLAIM_REJECTED` |
| WorkflowStep 缺失 target | `AUTHORING_INVALID_PROVIDER_OUTPUT` |
| WorkflowStep 未知 target kind | `AUTHORING_INVALID_PROVIDER_OUTPUT` |
| WorkflowStep target 空 id | `AUTHORING_INVALID_PROVIDER_OUTPUT` |

### 18.6 Prompt Input Hash

Prompt input hash 使用 canonical hash 基础设施计算，排序后写入确保顺序无关：

```csharp
var hashService = services.GetRequiredService<IDescriptorAuthoringPromptInputHashService>();
var promptInput = promptInputFactory.Create(authoringContext);
var hash = hashService.ComputeHash(promptInput);
// hash — CanonicalHash，基于 SHA256，排序后写入
```

排序规则：
- Descriptors: by (Namespace, Id, Version)
- Memories: by MemoryId
- Visible Descriptor Refs: by (Namespace, Id, Version)
- Supported Descriptor Kinds: by Ordinal

### 18.7 OpenAI-Compatible Provider

```csharp
// 配置
services.AddOpenAICompatibleAuthoringProvider(
    providerName: "azure-openai",
    credentialReference: "Authoring:AzureOpenAI:ApiKey",
    endpoint: new Uri("https://my-resource.openai.azure.com/openai/deployments/my-deployment"));

// appsettings.json
{
    "Authoring": {
        "AzureOpenAI": {
            "ApiKey": "your-api-key"
        }
    }
}
```

Provider 行为：
- Per-request Authorization header（不修改 HttpClient.DefaultRequestHeaders）
- Linked CancellationTokenSource + CancelAfter(ProviderProfile.Timeout)
- 区分 caller cancellation（rethrow）和 provider timeout（返回 Timeout failure）
- Source-generated JSON（OpenAICompatibleAuthoringJsonSerializerContext）

### 18.8 JSON 序列化

```csharp
using CrestCreates.Agent.Authoring.Abstractions.Json;

// Abstractions JSON context（注册 20 个类型 + CanonicalHash + DescriptorDraft）
var options = new JsonSerializerOptions
{
    TypeInfoResolver = JsonTypeInfoResolver.Combine(
        AgentControlPlaneToolJsonSerializerContext.Default,
        AgentMemoryJsonSerializerContext.Default,
        DescriptorAuthoringJsonSerializerContext.Default)
};

// Parser JSON context（在 Authoring runtime 项目内）
// DescriptorAuthoringParserJsonSerializerContext — 注册 DescriptorAuthoringProviderOutputDto 及子 DTO

// OpenAI JSON context（在 Http 项目内）
// OpenAICompatibleAuthoringJsonSerializerContext — 注册 chat request/response DTOs
```

### 18.9 关键使用约束

| 约束 | 说明 |
|------|------|
| **LLM 只产出 draft** | 不激活、不审批、不变异注册表、不绕过 Control Plane 审查 |
| **Parser 拒绝缺失 target** | 不静默 fallback 到 "unknown" 引用 |
| **PromptHashMismatch → Blocked** | Governance boundary rejection，非普通 JSON 错误 |
| **Source-generated JSON** | Parser 和 OpenAI client 均使用 JsonSerializerContext |
| **Canonical hash prompt hash** | 排序后写入，顺序无关 |
| **ProviderProfile.Timeout 生效** | Linked CTS，区分 caller cancellation 和 provider timeout |
| **ParseContext 无硬编码** | TenantId、AuthorId、AuthorKind、CreatedAt 全部来自注入 |
| **ModelProfile 可配置** | 不硬编码 "unknown" |
| **依赖边界** | Authoring 不引用 ControlPlane/DraftContracts/Http |
| **ParserSupportedKinds** | 只暴露 Parser 支持的 DescriptorKind（HumanTask、Workflow） |
| **PlanId 稳定** | 使用 prompt input hash 前 16 字符作为 fallback，不用 GetHashCode() |

---

## 19. Agent Prompt Contracts & Prompt Versioning 使用 (Phase 7h)

> **Phase 7h (#52)** 为 LLM-backed Descriptor Authoring 引入结构化 prompt 证据链路。每次 prompt 调用产生可审计的 input/output evidence 和 hash 摘要，output evidence 排除 LLM 原始输出文本。

### 19.1 DI 注册

```csharp
// 注册 Prompting 服务
services.AddAgentPrompting();

// 等价于注册：
// - IAgentPromptEvidenceFactory → DefaultAgentPromptEvidenceFactory
// - IAgentPromptHashService → DefaultAgentPromptHashService
// - InMemoryAgentPromptTemplateRegistry（空 registry）

// 添加 prompt templates
services.Configure<AgentPromptingOptions>(options =>
{
    options.Templates = new List<AgentPromptTemplateDescriptor>
    {
        new()
        {
            TemplateId = new AgentPromptTemplateId("descriptor-authoring-default"),
            Version = new AgentPromptVersion("v1"),
            Purpose = AgentPromptPurpose.DescriptorAuthoring,
            ContractVersion = new AgentPromptContractVersion("7g.v1"),
            Metadata = new Dictionary<string, string>
            {
                ["Description"] = "Default descriptor authoring prompt template"
            }
        }
    };
});
```

### 19.2 创建 Prompt Evidence

```csharp
using CrestCreates.Agent.Prompting.Abstractions;

var evidenceFactory = services.GetRequiredService<IAgentPromptEvidenceFactory>();

// 创建 input evidence（包含 prompt input hash）
var inputEvidence = evidenceFactory.CreateInputEvidence(
    templateId: new AgentPromptTemplateId("descriptor-authoring-default"),
    templateVersion: new AgentPromptVersion("v1"),
    contractVersion: new AgentPromptContractVersion("7g.v1"),
    payload: promptInput,  // DescriptorAuthoringPromptInput
    purpose: AgentPromptPurpose.DescriptorAuthoring);

// inputEvidence.Hash — CanonicalHash（input evidence hash）
// inputEvidence.TemplateId, TemplateVersion, ContractVersion, Purpose

// 创建 output evidence（排除 ResponseText，仅投影 ProviderName、ModelName、PromptInputHash、FailureKind、FailureDetail）
var outputEvidence = evidenceFactory.CreateOutputEvidence(
    payload: modelResponse,  // DescriptorAuthoringModelResponse
    purpose: AgentPromptPurpose.DescriptorAuthoring);

// outputEvidence.Hash — CanonicalHash（output evidence hash，不含 ResponseText）
```

### 19.3 Prompt Hash 服务

```csharp
var hashService = services.GetRequiredService<IAgentPromptHashService>();

// 计算 prompt input hash
var inputHash = hashService.ComputeInputHash(
    templateId: templateId,
    templateVersion: templateVersion,
    contractVersion: contractVersion,
    payload: promptInput);

// 计算 prompt output evidence hash
var outputHash = hashService.ComputeOutputHash(
    payload: modelResponse);
```

**AoT-safe 实现**：`DefaultAgentPromptHashService` 使用 `IAgentPromptCanonicalPayloadProjector<T>` 注册 projector，不使用反射。每个 payload 类型有对应的 projector 实现。

### 19.4 Prompt Template Registry

```csharp
var registry = services.GetRequiredService<InMemoryAgentPromptTemplateRegistry>();

// 查找 template
var template = registry.Find(
    new AgentPromptTemplateId("descriptor-authoring-default"),
    new AgentPromptVersion("v1"));

// 列出所有 templates
var allTemplates = registry.List();
```

**关键约束**：`Find()` 和 `List()` 返回防御性拷贝（`with { Metadata = new Dictionary<>() }`），防止外部 mutation 泄漏到 registry 内部状态。

### 19.5 Authoring 集成

Phase 7h 在 `LlmDescriptorAuthoringAgent` 中集成 prompt evidence：

```csharp
// LlmDescriptorAuthoringAgent 构造函数新增 IAgentPromptEvidenceFactory 依赖
public LlmDescriptorAuthoringAgent(
    IDescriptorAuthoringModelClient modelClient,
    IDefaultDescriptorAuthoringPromptBuilder promptBuilder,
    IDefaultDescriptorAuthoringPromptInputFactory promptInputFactory,
    IAgentPromptHashService promptHashService,
    JsonDescriptorAuthoringOutputParser parser,
    IAgentPromptEvidenceFactory evidenceFactory,  // NEW
    IOptions<LlmDescriptorAuthoringAgentOptions> options)

// DescriptorAuthoringResult 新增 PromptEvidence 字段
public sealed record DescriptorAuthoringResult
{
    // ... existing fields ...
    public AgentPromptEvidenceSummary? PromptEvidence { get; init; }  // NEW
}
```

**AgentPromptEvidenceSummary** 包含：
- `InputEvidence` — `AgentPromptInputEvidence`（hash + template 信息）
- `OutputEvidence` — `AgentPromptOutputEvidence`（hash，不含 ResponseText）

### 19.6 Projector 规范

每个 projector 必须写一个**完整的 JSON 值**（如 `WriteStartObject()` / `WriteEndObject()` 包裹）：

```csharp
// ✅ 正确：完整 JSON 对象
public void Write(Utf8JsonWriter writer, T payload)
{
    writer.WriteStartObject();
    writer.WriteString("PropertyName", payload.PropertyName);
    // ... other properties
    writer.WriteEndObject();
}

// ❌ 错误：裸属性序列（无对象边界）
public void Write(Utf8JsonWriter writer, T payload)
{
    writer.WriteString("PropertyName", payload.PropertyName);
    // Missing WriteStartObject/WriteEndObject!
}
```

这是因为 `DefaultAgentPromptHashService` 在写完 `"payload"` 属性名后调用 projector，projector 必须产出一个完整的 JSON 值。由于 `SkipValidation = true`，畸形 JSON 不会抛异常但会产出错误的 canonical hash。

### 19.7 Canonical Hash 常量扩展

Phase 7h 新增 3 个 `CanonicalHashArtifactNames` 常量和 2 个 `AgentPromptCanonicalShapeVersions` 常量。Prompt hash 复用 `CanonicalHashContractVersions.DescriptorHash`。

```csharp
// CanonicalHashArtifactNames 新增
public const string AgentPromptInputEvidence = "AgentPromptInputEvidence";
public const string AgentPromptOutputEvidence = "AgentPromptOutputEvidence";
public const string AgentPromptTemplate = "AgentPromptTemplate";

// AgentPromptCanonicalShapeVersions (Prompting.Abstractions)
public const string InputEvidence = "agent-prompt-input-evidence-shape-v1";
public const string OutputEvidence = "agent-prompt-output-evidence-shape-v1";

// Prompt hash uses existing ContractVersion
// CanonicalHashContractVersions.DescriptorHash = "canonical-hash-v1"
```

### 19.8 Prompt 诊断码

| 诊断码 | 说明 |
|--------|------|
| `PROMPT_INPUT_HASH_COMPUTE_FAILED` | Input evidence hash 计算失败 |
| `PROMPT_OUTPUT_HASH_COMPUTE_FAILED` | Output evidence hash 计算失败 |
| `PROMPT_INPUT_PROJECTOR_NOT_REGISTERED` | Input projector 未注册 |
| `PROMPT_OUTPUT_PROJECTOR_NOT_REGISTERED` | Output projector 未注册 |
| `PROMPT_TEMPLATE_NOT_FOUND` | Prompt template 未找到 |
| `PROMPT_EVIDENCE_FACTORY_FAILED` | Evidence factory 创建失败 |
| `PROMPT_CONTRACT_VERSION_MISMATCH` | Prompt contract version 不匹配 |
| `PROMPT_INVALID_PAYLOAD` | Prompt payload 验证失败 |

### 19.9 关键使用约束

| 约束 | 说明 |
|------|------|
| **Output evidence 排除 ResponseText** | LLM 原始输出不参与 hash 计算，防止输出变化导致 evidence hash 不稳定 |
| **Projector 写完整 JSON 值** | 必须用 WriteStartObject/WriteEndObject 包裹，SkipValidation=true 不阻止畸形 JSON |
| **Template registry 返回防御性拷贝** | Find/List 返回 descriptor + copied Metadata，防止外部 mutation 泄漏 |
| **依赖边界** | Prompting.Abstractions 不引用 Core.Abstractions/Framework/Web/Platform/Persistence |
| **Authoring 引用 Prompting.Abstractions** | Authoring runtime 不引用 Prompting runtime（仅引用 Abstractions） |
| **AgentPromptDiagnostic.Code 是 string** | 与 AgentToolDiagnostic.Code 保持一致，Prompting.Abstractions 不引用 Core.Abstractions |
| **AgentPromptDiagnostic.Severity 是 string** | 与 SeverityLevel 枚举解耦，Prompting.Abstractions 不引用 Core.Abstractions |
