# Tool DTO, JSON Contract & Review Report — Usage Guide

> **Date:** 2026-06-22 | **Status:** Implemented | **Phase 7c (#41 DTO Design + #42 Source Generator) + Phase 7d (#16 Review Report & Fix Proposal)**

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

| 工具名 | 请求类型 | 结果类型 (AgentToolResult<>) |
|--------|---------|---------------------------|
| `SubmitActivationRequest` | `SubmitActivationRequestRequest` | `ActivationRequest` |
| `GetActivationRequestStatus` | `DescriptorRef` | `ActivationRequest` |
| `CancelActivationRequest` | `DescriptorRef` | `ActivationRequest` |

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
// => "7d.v1"
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
//   BuildMetadataContextPack: contract=7d.v1
//   CreateDescriptorDraft: contract=7d.v1
//   ListAgentTools: contract=7d.v1
//   ...
```

### 7.3 适配器兼容性检查

```csharp
// 适配器在连接时检查契约版本兼容性
var toolDescriptor = manifestProvider.GetToolByName("CreateDescriptorDraft");
if (toolDescriptor?.ContractVersion != "7d.v1")
{
    // 版本不匹配 — 适配器可能需要升级或降级策略
    throw new AdapterException($"Expected contract 7d.v1, got {toolDescriptor?.ContractVersion}");
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

确保所有 34 个工具在 Manifest 和 JSON Context 中都已注册：

```csharp
[Fact]
public void all_34_tools_are_registered_in_manifest()
{
    var manifest = new StaticAgentToolManifestProvider();
    manifest.GetAllTools().Should().HaveCount(34);
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
        // ... 验证所有 34 个工具的类型
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
- 模板版本必须在 `TemplateVersion` 中追踪（当前 `"7d.v1"`）
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

## 14. 未来：LLM 集成 (Future: LLM Integration)

Phase 7b（LLM Bootstrap Plane）将在此 DTO 边界之上构建。LLM 集成将使用相同的 Tool DTO 与 Control Plane 交互：

```
LLM Provider → Prompt → DescriptorDraftBuilder → DescriptorDraft
    → Projection (FromDraft) → AgentDescriptorDraftDto → JSON → Adapter → HTTP/MCP
```

设计考虑：
- 所有 DTO 已经可以被 LLM 原生理解和生成（纯 JSON，无抽象类型）
- `CreateDescriptorDraftRequest` 是 LLM 直接调用的理想接口
- 区分器不变式确保 LLM 输出无歧义
- 契约版本允许 LLM 工具描述在运行时自我描述

Phase 7b 核心组件（尚未实现）：

| 组件 | 说明 |
|------|------|
| `PromptTemplate` | 结构化提示模板，带描述符上下文注入 |
| `ILLMProvider` | 可插拔 LLM 后端抽象 |
| `PromptTemplateRegistry` | 按描述符种类存储和解析提示模板 |
| `DescriptorDraftBuilder` | LLM 结构化输出 → DescriptorDraft |
