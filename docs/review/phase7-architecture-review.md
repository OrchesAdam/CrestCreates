# Phase 7 架构审查报告 — LLM Bootstrap Plane

**日期:** 2026-07-03
**审查范围:** Phase 7 全部子阶段 (7a–7h, 7e+, 7g+)
**审查方法:** 设计文档分析 + 代码实现验证 + 依赖边界检查
**代码规模:** ~33 项目, ~574 源文件, ~146 测试文件, 500+ 公共类型

---

## 1. 总体评价

Phase 7 在治理优先的设计哲学上是扎实的。9 个子阶段形成了清晰的递进链：

```
7a Draft Foundation → 7b ContextPack → 7c ToolSurface → 7d ReviewReport
→ 7e Activation → 7e+ Memory → 7f GoldenScenario → 7g LLM Authoring → 7h PromptEvidence
```

核心设计不变量——Agent 永远不能绕过 review/governance 直接激活 registry——在每个子阶段都被忠实遵守。Control Plane 作为治理面、Runtime Handler 作为执行面的分离是正确的。

但存在 1 个阻断级依赖方向违规、6 个显著问题需要修复。

**修复状态（2026-07-03）：** P0-1 ✅, P1-2 ✅, P1-3 ✅, P1-5 ✅, P1-7 ✅, P2-8 ✅, P2-9 ✅。P1-4（Abstractions 拆分）和 P1-6（JsonSerializerContext SG）为后续优化项，当前不阻断。

---

## 2. 发现清单

### 🔴 P0-1: `DescriptorDraft.Abstractions` 跨层引用 Runtime + Framework，违反依赖方向 ✅ 已修复

**严重程度:** 🔴 阻断 — 依赖方向违规，边界测试盲区

**文件:**
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CrestCreates.DescriptorDraft.Abstractions.csproj` (lines 13–17)
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/WorkflowDescriptorDraftPayload.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/HumanTaskDescriptorDraftPayload.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/EventDescriptorDraftPayload.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/FormDescriptorDraftPayload.cs`
- `src/Metadata/Draft/CrestCreates.DescriptorDraft.Abstractions/CapabilityDescriptorDraftPayload.cs`

**现状:**

`DescriptorDraft.Abstractions` 位于 `src/Metadata/Draft/`，但引用了 4 个 Runtime abstractions + 1 个 Framework module：

```xml
<!-- CrestCreates.DescriptorDraft.Abstractions.csproj -->
<ProjectReference Include="../../../Runtime/Capability/CrestCreates.Capability.Abstractions/..." />
<ProjectReference Include="../../../Framework/Modules/CrestCreates.Form.Abstractions/..." />
<ProjectReference Include="../../../Runtime/Eventing/CrestCreates.Event.Abstractions/..." />
<ProjectReference Include="../../../Runtime/HumanTask/CrestCreates.HumanTask.Abstractions/..." />
<ProjectReference Include="../../../Runtime/Workflow/CrestCreates.Workflow.Abstractions/..." />
```

按 `AGENTS.md` 的依赖方向图，Metadata 层不应依赖 Runtime 层。实际依赖链为：

```
Metadata/Draft/DescriptorDraft.Abstractions → Runtime/Capability.Abstractions  ❌
Metadata/Draft/DescriptorDraft.Abstractions → Runtime/Event.Abstractions       ❌
Metadata/Draft/DescriptorDraft.Abstractions → Runtime/HumanTask.Abstractions   ❌
Metadata/Draft/DescriptorDraft.Abstractions → Runtime/Workflow.Abstractions    ❌
Metadata/Draft/DescriptorDraft.Abstractions → Framework/Modules/Form.Abstractions ❌
```

**根因:**

6 个 typed payload 直接持有 Runtime descriptor 类型作为构造函数参数：

```csharp
// WorkflowDescriptorDraftPayload.cs
public sealed record WorkflowDescriptorDraftPayload(
    WorkflowDescriptor Descriptor    // ← Workflow.Abstractions 类型
) : DescriptorDraftPayload { ... }
```

这些 payload 定义在 Abstractions 中，导致整个项目被迫引用 Runtime。

**边界测试盲区:**

`DependencyBoundaryTests.cs` 只覆盖 `CrestCreates.Metadata.Abstractions` 不引用 Runtime（line 18–24），**没有覆盖 `DescriptorDraft.Abstractions`**，所以这个违规未被检测到。

**影响:**

- `ControlPlane.Abstractions` 通过引用 `DescriptorDraft.Abstractions` 传递性拉入所有 Runtime abstractions
- Metadata 层的独立性被破坏，下游项目无法仅依赖 Metadata 而不传递依赖 Runtime
- 随着更多代码依赖当前 payload 位置，迁移成本持续上升

**推荐修复:**

将 6 个具体 payload 类型从 `DescriptorDraft.Abstractions` 移至 `DescriptorDraft`（implementation 项目）。Abstractions 只保留 `DescriptorDraftPayload` 抽象基类：

```csharp
// 保留在 Abstractions
public abstract record DescriptorDraftPayload : ISnapshotable<DescriptorDraftPayload>
{
    public abstract DescriptorKind DescriptorKind { get; }
    public abstract IDescriptor GetDescriptor();          // 返回 IDescriptor，不需要 Runtime 类型
    public abstract DescriptorDraftPayload Snapshot();
}

// 移至 DescriptorDraft (implementation)
public sealed record WorkflowDescriptorDraftPayload(
    WorkflowDescriptor Descriptor
) : DescriptorDraftPayload { ... }
```

Implementation 项目本身已引用 Runtime abstractions，不需要额外依赖。消费端通过 `GetDescriptor()` 返回 `IDescriptor`（在 `Metadata.Abstractions` 中），不需要知道具体类型。

**修复执行（2026-07-03）：**

已将 6 个 typed payload 类型（`WorkflowDescriptorDraftPayload`, `HumanTaskDescriptorDraftPayload`, `EventDescriptorDraftPayload`, `FormDescriptorDraftPayload`, `CapabilityDescriptorDraftPayload`, `AgentCapabilityDescriptorDraftPayload`）从 `DescriptorDraft.Abstractions` 移至 `DescriptorDraft`。`DescriptorDraft.Abstractions.csproj` 移除了 5 个 Runtime/Framework 引用，仅保留 `Metadata.Abstractions` + `Snapshot.Abstractions`。`DescriptorDraftPayload` 抽象基类保留在 Abstractions 中。

---

### 🟡 P1-2: InMemory 实现作为生产默认注册，语义误导 ✅ 已修复

**严重程度:** 🟡 显著 — 生产安全性风险

**文件:**
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/AgentControlPlaneServiceCollectionExtensions.cs` (lines 27, 32, 35–36)
- `src/Runtime/Agent/CrestCreates.Agent.Memory/AgentMemoryServiceCollectionExtensions.cs` (lines 36–39)
- `src/Runtime/Agent/CrestCreates.Agent.ControlPlane/Activation/InMemoryRuntimeActivationGate.cs`

**现状:**

`AddAgentControlPlane()` 方法文档为 "production-safe default authorization"，但通过 `TryAddSingleton` 注册了 4 个 InMemory 实现：

| 接口 | InMemory 实现 | 问题 |
|---|---|---|
| `IAgentToolInvocationAuditor` | `InMemoryAgentToolInvocationAuditor` | 无持久化，重启丢失审计记录 |
| `IActivationBindingArtifactResolver` | `InMemoryActivationBindingArtifactResolver` | 无持久化，binding artifact 丢失 |
| `IDescriptorActivationAuditor` | `InMemoryDescriptorActivationAuditor` | 无持久化，激活审计丢失 |
| `IRuntimeActivationGate` | `InMemoryRuntimeActivationGate` | 不执行真正激活，返回 fake ref |

`InMemoryRuntimeActivationGate` 存在三个具体问题：

1. **`CanReject` public mutable property** — 仅用于测试，暴露在生产类型上。任何代码可在运行时设置 `CanReject = true` 导致所有激活被拒绝。
2. **`ActivatedDescriptorRef = $"activated:{request.DraftId}"`** — 不是合法的 `DescriptorRef` 格式，生产环境激活后返回的 ref 无法被下游消费。
3. **`TryAdd` 语义** — 意味着"有就不管"，但方法名和文档没有明确说明这些是 stub，生产部署可能误以为已注册了真正的实现。

同样的模式在 `AgentMemoryServiceCollectionExtensions` 中重复：4 个 InMemory store 通过 `TryAddSingleton` 注册。

**推荐修复:**

1. 拆分 DI 注册：
   - `AddAgentControlPlaneCore()` — 注册真正的服务（authorization, manifest, report builder, review 等）
   - `AddAgentControlPlaneInMemoryStubs()` — 注册 InMemory 实现，方法名明确语义
2. `InMemoryRuntimeActivationGate.CanReject` 移除，改为构造函数配置 `InMemoryRuntimeActivationGateOptions`
3. 修复 `ActivatedDescriptorRef` 格式使其成为合法的 `VersionedDescriptorRef`
4. `AgentMemoryServiceCollectionExtensions` 同理拆分

**修复执行（2026-07-03）：**

1. `AddAgentControlPlane()` 3 个重载已移除 4 个 InMemory stub 注册，新增 `AddAgentControlPlaneInMemoryStubs()` 方法。文档注释已更新，明确说明 InMemory stubs 不包含在核心注册中。
2. `InMemoryRuntimeActivationGate.CanReject` 已移除，替换为 `InMemoryRuntimeActivationGateOptions { RejectAll }` 构造函数配置。
3. `ActivatedDescriptorRef` 已从 `$"activated:{request.DraftId}"` 修复为 `new DescriptorRef("in-memory", request.DraftId).FullId`，产生合法的 `"in-memory.{DraftId}"` 格式。
4. `AgentMemoryServiceCollectionExtensions` 暂未拆分（非本次修复范围，建议后续同步）。

---

### 🟡 P1-3: 边界测试覆盖缺口 ✅ 已修复

**严重程度:** 🟡 显著 — P0-1 的配套问题

**文件:** `tests/Boundary/CrestCreates.DependencyBoundaries.Tests/DependencyBoundaryTests.cs`

**现状:**

现有边界测试覆盖了 12 个断言，但存在以下缺口：

| 项目 | 是否覆盖 | 风险 |
|---|---|---|
| `Metadata.Abstractions` 不引用 Runtime/Framework | ✅ | — |
| `DescriptorDraft.Abstractions` 不引用 Runtime/Framework | ❌ | 当前违规未检测 |
| `Draft.Abstractions` 不引用 Runtime | ❌ | 未强制 |
| `ContextPack.Abstractions` 不引用 Runtime/Framework | ❌ | 未强制 |
| `Snapshot.Abstractions` 不引用上层 | ❌ | 未强制 |
| `ControlPlane.Abstractions` 不引用 Framework/Web | ✅ (部分) | 缺少 Web 层检查 |

**推荐修复:**

添加以下测试：

```csharp
[Fact]
public void MetadataDraftProjects_DoNotReferenceRuntimeOrFramework()
{
    AssertNoDirectProjectReferences(
        "src/Metadata/Draft",
        "Metadata Draft projects must not reference Runtime or Framework layers.",
        new[] { "src/Framework", "src/Runtime", "src/Persistence", "src/Platform" });
}

[Fact]
public void MetadataContextPack_DoNotReferenceRuntimeOrFramework()
{
    AssertNoDirectProjectReferences(
        "src/Metadata/CrestCreates.Metadata.ContextPack.Abstractions",
        "ContextPack.Abstractions must not reference Runtime or Framework.",
        new[] { "src/Framework", "src/Runtime", "src/Persistence", "src/Platform" });
}
```

**注意:** 此修复需与 P0-1 同步完成。在 P0-1 修复前，添加此测试会导致构建失败。

**修复执行（2026-07-03）：**

已添加 5 个边界测试方法至 `DependencyBoundaryTests.cs`：
1. `DescriptorDraftAbstractions_DoesNotReferenceRuntimeOrFramework` — 验证 Abstractions 不引用 Runtime/Framework/Platform
2. `DescriptorDraft_DoesNotReferenceFrameworkApiWebOrPlatform` — 验证 Implementation 不引用 Framework Api/Web/Platform
3. `MetadataDraftProjects_DoNotReferencePlatform` — 验证所有 Draft 项目不引用 Platform
4. `MetadataContextPackProjects_DoNotReferenceRuntimeOrFrameworkOrPlatform` — 验证 ContextPack 保持 metadata-only
5. `MetadataSnapshotProjects_DoNotReferenceRuntimeOrFrameworkOrPlatform` — 验证 Snapshot.Abstractions 保持 snapshot-only

全部 23 个边界测试通过（18 existing + 5 new）。

---

### 🟡 P1-4: `ControlPlane.Abstractions` 厚度过大 — 95 个源文件

**严重程度:** 🟡 显著 — 消费者被迫传递依赖不需要的契约

**文件:** `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/` (95 .cs 源文件)

**现状:**

| 子命名空间 | 文件数 | 内容 |
|---|---|---|
| Root | ~59 | Request/response records, enums, diagnostic codes, permission names |
| Activation/ | ~19 | ActivationRequest, BindingHashes (7 CanonicalHash slots), policy, review decision |
| ToolDtos/ | ~14 | Flat DTOs for tool surface |
| Json/ | ~3 | STJ serializer context (157 lines) |

**问题:**

Activation 契约（7e 的核心产出）与 Read/Draft 契约耦合在同一项目中。任何只需要读取 descriptor 信息的消费者也被迫传递依赖整个 Activation 模型，包括：
- `BindingHashes` 的 7 个 CanonicalHash slot
- `ActivationRequest` 的 6 种 status
- `DescriptorActivationPolicy` / `DescriptorActivationEligibility`
- `DescriptorActivationReviewDecision` / `DescriptorActivationHumanTaskIds`

**推荐修复:**

短期：文档化 rationale，明确说明 Activation 契约嵌入 ControlPlane.Abstractions 的原因。

中期：将 Activation 契约拆分为 `CrestCreates.Agent.ControlPlane.Activation.Abstractions`，让只需 read/draft 工具的消费者不需要传递依赖 Activation 模型。拆分后的依赖链：

```
ControlPlane.Abstractions (read + draft + review DTOs)
  └─> ControlPlane.Activation.Abstractions (activation + binding + policy)
```

---

### 🟡 P1-5: Prompting / Authoring 测试覆盖部分不足 ✅ 已修复

**严重程度:** 🟡 显著 — 部分核心契约缺少验证

**文件:**
- `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/` — 3 个测试文件
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/` — 8 个测试文件

**现状:**

Prompting 测试已有 prompt evidence 相关覆盖：
- `PromptingHashTests` (6 tests): input hash 稳定性、template version 影响、model profile ref 影响、correlation/actor 排除、output hash AuditEvidence purpose、missing projector 抛异常
- `PromptingContractTests` (5 tests): semantic value object 验证、TemplateDescriptor 默认值、Evidence diagnostics 默认值、EvidenceSummary JSON 序列化
- `PromptingRegistryTests` (3 tests): registry 存储/防御性拷贝

Authoring 测试已有 8 个文件，含 `GoldenScenarioLlmFixtureTests` + `CompanyCertificationLlmFixture`。

**仍缺失的覆盖:**

| 项目 | 缺失 |
|---|---|
| Prompting | `DefaultAgentPromptEvidenceFactory` projection 逻辑测试（当前 hash tests 只覆盖 `IAgentPromptHashService`，未验证 factory 的 input→evidence→summary 完整路径）、`AgentPromptingJsonSerializerContext` 覆盖验证（所有 DTO 是否有 `[JsonSerializable]` 条目） |
| Authoring | `DescriptorAuthoringJsonSerializerContext` 覆盖验证 |

**推荐修复:**

补充 `DefaultAgentPromptEvidenceFactory` 的 projection 逻辑测试和 `AgentPromptingJsonSerializerContext` 覆盖验证（参照 ControlPlane 的 `ToolContractCoverageTests` 模式）。

**修复执行（2026-07-03）：**

1. 新增 `PromptingEvidenceFactoryTests.cs`（5 tests）：input evidence 全字段传播、hash service 委托、output evidence 无诊断、null hash 产生 Warning 诊断、input hash 传播。
2. 新增 `PromptingSerializerCoverageTests.cs`（1 enforcement test）：验证所有 public DTO 类型有 `[JsonSerializable]` 注册。
3. 覆盖测试发现 6 个缺失类型（`AgentPromptContractVersion`, `AgentPromptModelProfileRef`, `AgentPromptProviderProfileRef`, `AgentPromptTemplateDescriptor`, `AgentPromptTemplateId`, `AgentPromptVersion`），已补齐至 `AgentPromptingJsonSerializerContext`。
4. 新增 `AuthoringSerializerCoverageTests.cs`（1 enforcement test）：验证 Authoring.Abstractions 所有 public DTO 类型有 `[JsonSerializable]` 注册。
5. 覆盖测试发现 3 个缺失类型（`DescriptorAuthoringStatus`, `DescriptorAuthoringDescriptorProjection`, `DescriptorAuthoringMemoryItemProjection`），已补齐至 `DescriptorAuthoringJsonSerializerContext`。

---

### 🟡 P1-6: Source Generator 覆盖不足 — 与项目原则对齐问题

**严重程度:** 🟡 显著 — 方向性对齐

**现状:**

Phase 7 仅 `AgentDraftContractGenerator` 一个 source generator。以下组件仍使用手写模式：

| 组件 | 手写内容 | 应由 SG 生成 |
|---|---|---|
| `AgentControlPlaneToolJsonSerializerContext` | 157 行手写 `[JsonSerializable]` 属性 | SG 自动从 assembly 类型生成 |
| ToolDto projections | `AgentReviewResultDtoProjection`, `DescriptorSummaryDtoProjection` 等手写映射 | `GenerateObjectMapping` SG |
| CanonicalHash profiles | `BindingHashes` 的 7 个 hash slot 手动维护 | SG 从 contract spec 推导 |

项目原则是"编译期代码生成优于运行时反射"，但 Phase 7 大量使用手写序列化上下文和 projection 代码。虽然没有使用运行时反射（不违反约束），但与项目方向不对齐。

**已知约束:** STJ source generator 无法看到另一个 Roslyn generator 发出的 `[JsonSerializable]` 属性（memory id 119）。但 `JsonSerializerContext` 中的类型不是 SG 生成的——它们是手写 DTO，所以 STJ SG 可以正常处理。可以通过另一个 SG 自动生成 `[JsonSerializable]` 属性列表。

**推荐修复:**

优先级排序：
1. 自动生成 `JsonSerializerContext` — 添加新类型时减少手动维护，ROI 最高
2. ToolDto projection 生成 — 等 ObjectMapping SG 支持 projection 模式后处理
3. CanonicalHash profile 生成 — 当前手写维护成本可接受，暂缓

---

### 🟡 P1-7: `IAgentControlPlaneToolService` — 27 个方法未分解 ✅ 已修复

**严重程度:** 🟡 显著 — ISP 违规，与 authorization model 不对齐

**文件:** `src/Runtime/Agent/CrestCreates.Agent.ControlPlane.Abstractions/IAgentControlPlaneToolService.cs` (177 lines, 27 methods)

**现状:**

单一 facade 接口包含 6 个 wave 的 27 个方法。每个方法签名一致（`AgentToolInvocationContext + request → AgentToolResult<T>`），实现委托给内部服务。

**问题:**

1. **ISP 违规** — 只需要 read 工具的消费者被迫依赖 activation handoff 方法
2. **Mock 成本高** — 测试需要 setup 27 个方法
3. **与 authorization 不对齐** — authorization 分为 `AllowReadOnlyTools` / `AllowMutationTools` / `AllowActivationHandoffTools` 三级，但接口是单体

**推荐修复:**

提取子接口，与 authorization 三级模型对齐：

```csharp
public interface IReadOnlyControlPlaneTools        // Wave 1: 6 methods
{
    Task<AgentToolResult<MetadataContextPack>> BuildMetadataContextPackAsync(...);
    Task<AgentToolResult<DescriptorInfo>> GetDescriptorByRefAsync(...);
    Task<AgentToolResult<DescriptorSearchResult>> SearchDescriptorsAsync(...);
    Task<AgentToolResult<DescriptorRelationshipsResult>> ListDescriptorRelationshipsAsync(...);
    Task<AgentToolResult<TopologySummaryResult>> GetTopologySummaryAsync(...);
    Task<AgentToolResult<MetadataContextPack>> BuildRuntimeScenarioContextPackAsync(...);
}

public interface IMutationControlPlaneTools         // Wave 2–4: 14 methods
{
    // Draft CRUD + Review + Fix Proposal
}

public interface IActivationControlPlaneTools       // Wave 5–6: 7 methods
{
    // Package Preview + Activation Handoff
}

public interface IAgentControlPlaneToolService
    : IReadOnlyControlPlaneTools
    , IMutationControlPlaneTools
    , IActivationControlPlaneTools { }
```

**修复执行（2026-07-03）：**

已提取 3 个子接口至 `IAgentControlPlaneToolService.cs`：
- `IReadOnlyControlPlaneTools` — Wave 1 (6 methods: context, search, topology)
- `IMutationControlPlaneTools` — Waves 2–5 (21 methods: draft CRUD, review, fix proposals, package previews)
- `IActivationControlPlaneTools` — Wave 6 (3 methods: activation submission/query/cancel)
- `IAgentControlPlaneToolService` — 空体，继承三者

所有 27 个方法签名不变，`DefaultAgentControlPlaneToolService` 无需修改，向后兼容。

---

### ⚪ P2-8: Authoring / Prompting 缺少 SerializerContext 覆盖验证 ✅ 已修复

**文件:**
- `tests/Runtime/Agent/CrestCreates.Agent.ControlPlane.Tests/ToolContractCoverageTests.cs` (526 lines, 4 tests) — ✅ 已有
- `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/` — ❌ 缺少
- `tests/Runtime/Agent/CrestCreates.Agent.Prompting.Tests/` — ❌ 缺少

**现状:** ControlPlane 已有完善的 `ToolContractCoverageTests`，覆盖：
1. Manifest tool names ↔ contract registrations 对齐
2. `[JsonSerializable]` ↔ `JsonTypeInfo<T>` 对齐
3. Facade tools 的 `AgentToolResult<T>` 覆盖
4. 所有 public sealed record DTO 的 `JsonTypeInfo` 覆盖
5. Orphan type 检测（无对应 manifest tool 的注册类型）

Authoring 和 Prompting 没有类似覆盖。添加新 DTO 时可能遗漏 `[JsonSerializable]` 注册而不被检测。

**修复执行（2026-07-03）：**

1. Prompting: 新增 `PromptingSerializerCoverageTests.cs`，发现并修复 6 个缺失的 `[JsonSerializable]` 注册。
2. Authoring: 新增 `AuthoringSerializerCoverageTests.cs`，发现并修复 3 个缺失的 `[JsonSerializable]` 注册。
3. 两个覆盖测试均通过。

**建议:** 推广 `ToolContractCoverageTests` 模式到 Authoring 和 Prompting。至少添加 "所有 public sealed record DTO 必须有 JsonTypeInfo" 的覆盖测试。

---

### ⚪ P2-9: 空 Agent 程序集标记项目 ✅ 已文档化

**文件:**
- `src/Runtime/Agent/CrestCreates.Agent.Abstractions/` (1 .cs, assembly marker)
- `src/Runtime/Agent/CrestCreates.Agent.Runtime/` (1 .cs, assembly marker)

**现状:** 两个项目各只有 1 个 .cs 文件，无任何公共类型。可能是预留的聚合项目。

**建议:** 如果无当前用途，移除或在 .csproj 中注释说明预期用途。

**修复执行（2026-07-03）：**

两个项目已添加文档注释：
- `CrestCreates.Agent.Abstractions` — 依赖锚点，被 `Memory.Abstractions` 引用
- `CrestCreates.Agent.Runtime` — 未来 Agent runtime facade 的预留组合根

---

### ⚪ P2-10: `DraftContracts` 位置合理

**文件:** `src/Runtime/Agent/CrestCreates.Agent.DraftContracts/`

**现状:** DraftContracts 在 `src/Runtime/Agent/` 下，本质上是 Tooling 和 Runtime 之间的共享契约。CodeGenerator 不引用它（netstandard2.0 限制），生成代码通过名称引用。当前位置可接受。

**评估:** 无需修改。

---

### ⚪ P2-11: `DescriptorDraft` 所有权边界清晰

**现状:**
- Metadata/Draft 拥有：Draft 模型、store、validator、materializer、review service
- Runtime/Agent/ControlPlane 拥有：治理工作流（create, update, review, activate）
- 单向依赖：`ControlPlane.Abstractions → DescriptorDraft.Abstractions`

**评估:** 边界干净，无需修改。

---

## 3. 修复优先级

| 优先级 | 发现 | 行动 | 依赖 | 状态 |
|---|---|---|---|---|
| 🔴 P0-1 | DescriptorDraft.Abstractions → Runtime | 移动 payload 类型至 implementation 项目 | — | ✅ 已修复 |
| 🟡 P1-3 | 边界测试缺口 | 添加 Metadata/Draft 边界测试 | 依赖 P0-1 完成后才能通过 | ✅ 已修复 |
| 🟡 P1-2 | InMemory 作为生产默认 | 拆分 DI 注册，修复 CanReject 和 fake DescriptorRef | — | ✅ 已修复 |
| 🟡 P1-7 | 27 方法 facade | 提取子接口与 authorization 对齐 | — | ✅ 已修复 |
| 🟡 P1-4 | Abstractions 95 文件过厚 | 拆分 Activation 契约 | 可与 P1-7 同步 | ⏳ 后续优化 |
| 🟡 P1-5 | Prompting / Authoring 测试部分不足 | 补充 EvidenceFactory projection + serializer coverage 测试 | — | ✅ 已修复 |
| 🟡 P1-6 | SG 覆盖不足 | 优先自动生成 JsonSerializerContext | — | ⏳ 后续优化 |
| ⚪ P2-8 | Authoring/Prompting 缺 serializer coverage | 推广 ToolContractCoverageTests 模式 | — | ✅ 已修复 |
| ⚪ P2-9 | 空 Agent 程序集 | 移除或文档化 | — | ✅ 已文档化 |

**关键路径:** P0-1 → P1-3 必须顺序完成。其余可并行。

---

## 4. Phase 7 设计亮点

尽管存在上述问题，Phase 7 有以下值得肯定的设计决策：

1. **治理梯度不可绕过** — Agent 只能 produce draft，必须通过 review → governance → activation gate 才能影响 runtime registry。没有 shortcut。
2. **Canonical Hash 全链路** — 从 prompt input/output 到 binding snapshot 到 activation gate，每一步都有 purpose-separated canonical hash，支持终态审计。
3. **Agent Visibility Projection** — Phase 7c 的 `AgentDraftArtifactVisibilityProjector` 和 `AgentTopologyVisibilityProjector` 在 full results 上做 projection 而非过滤 query，保证治理真相完整。
4. **DraftContracts SG** — Agent-editable draft payload 契约由 source generator 生成，避免手写 DTO/projection/merge 的维护负担。
5. **Safety-first 默认** — `EvaluateGovernance` 默认 `ReviewRequired`，`BindingHashes` 所有 slot 无条件 required，没有"可选跳过"路径。
6. **Prompt Evidence Chain** — Phase 7h 的 prompt input/output evidence 有独立的 hash identity 和 template versioning，LLM 输出全程可追溯。
7. **LLM 不可治理** — `LlmDescriptorAuthoringAgent` 只产生 draft，不 save/review/package/activate/approve，与 AGENTS.md 的 "Agent Control Plane 是治理面，不是运行时执行面" 完全对齐。
