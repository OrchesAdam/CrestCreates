# Issue #133 实施日志 — B12 unsupported-intent 确定性负例与 live eval 用例

场景来源：Phase 10c spec B12（[设计规格](../specs/2026-09-07-phase-10c-asset-business-evolution-design.md) §期望结果表）
裁定基线：#122 校准记录 §5（[复核文档](../../review/2026-10-10-framework-architecture-review-follow-up.md)）——「确认缺口（静态）；行为层为候选缺口/待验证」
父 Issue：#133（不重开 #87/#88）

## 基线

| 项 | 值 |
| --- | --- |
| 实施基线 SHA | `9909365ab5c05b3e0ce610d264ef3c1b3b4572bb`（= `origin/master`，2026-11-03 fetch 核对） |
| 分支 | `feat/issue-133-b12-unsupported-intent-negative-20261103-093408` |
| SDK | .NET 10.0.100（`global.json`，`rollForward: latestMinor`） |
| 生产代码变更 | 无（`git diff -- src/` 为空；本轮只交付测试与文档） |
| 契约变更 | 无（遵守 Issue 边界：「不改变 authoring / 控制面既有契约，除非负例证明显式结果无法表达」） |

## 交付物

| 文件 | 类型 | 内容 |
| --- | --- | --- |
| `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.E2E.Tests/AssetUnsupportedIntentNegativeCaseTests.cs` | 新增 | 6 条确定性用例：1 条绿色对照 + 4 条 B12 负例 + 1 条绕过 authoring 白名单的工件边界用例 |
| `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.E2E.Tests/AssetActivationGateObserver.cs` | 新增 | 测试侧 `IRuntimeActivationGate` 观测器（记录调用/成功计数） |
| `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.E2E.Tests/AssetDeepSeekLiveAuthoringEvaluationTests.cs` | 修改 | 新增 opt-in live eval B12 用例 + `UnsupportedIntentEvaluationArtifact`/`...SemanticResult` + `ConfigureUnsupportedIntentServices`；`WriteArtifact` 形参放宽为 `object` |
| `samples/AssetManagement/tests/CrestCreates.Sample.AssetManagement.E2E.Tests/AssetLiveCompositionTests.cs` | 修改 | 新增无网络接线探针，验证 B12 live 用例的 DI 替换（gate 观测器 + agent）确实生效 |
| `docs/review/2026-10-10-framework-architecture-review-follow-up.md` | 修改 | §5 B12 裁定回填（§5.1） |
| `memory.md` | 修改 | Known gaps 中 B12 行改为交付状态与证据边界 |

## 逐条记录

### I-01 缺口复核：静态结论维持，落点选定（2026-11-03）

复核 #122 记录的三条现状，逐条核对当前 master 代码：

| #122 现状描述 | 复核结果 | 证据位置 |
| --- | --- | --- |
| `src/Runtime/Agent/` 无 clarification / unsupported-intent 结果契约 | 成立一半：`DescriptorAuthoringStatus`（`Abstractions/Authoring/DescriptorAuthoringStatus.cs:3-11`）无 unsupported/clarification 值，但 `Blocked` + `AUTHORING_UNKNOWN_DESCRIPTOR_KIND` / `AUTHORING_UNSUPPORTED_DRAFT_OPERATION` / `AUTHORING_GOVERNANCE_BOUNDARY_VIOLATION`（`DescriptorAuthoringDiagnosticCodes.cs:28-38`）已能表达「显式不支持」；「needs-clarification」作为可与畸形输出区分的显式结果确实无法表达（见 I-04） | 上述两文件 |
| 确定性 parser 只阻断结构级不支持 | 成立：`JsonDescriptorAuthoringOutputParser.TryParseDescriptorKind:279-301`（仅 HumanTask/Workflow）、`TryParseOperation:303-323`（仅 Create/Update）、memoryRef 权威声明 `:158-172` | `src/Runtime/Agent/CrestCreates.Agent.Authoring/Parsing/JsonDescriptorAuthoringOutputParser.cs` |
| 未找到 B12 negative case | 成立：既有 `OutputParserTests`/`LlmDescriptorAuthoringAgentTests` 只断 authoring 层返回码，没有任何用例把「伪造能力」响应放进真实 Asset 链路并断言 review/approval 边界与零部署；`Agent_DoesNot_Activate_Or_Mutate_Runtime`（`LlmDescriptorAuthoringAgentTests.cs:162-174`）只断非空字段，对副作用无计数，不能算 B12 负例 | `tests/Runtime/Agent/CrestCreates.Agent.Authoring.Tests/` |

落点选择：负例落在 Asset E2E 真实组合链路（`AssetControlPlaneApprovalHarness`），理由是 B12 声明的是「既有 Asset 维护演化链路」上的行为，需要真实 `DefaultDescriptorDraftReviewService` + 真实 `DefaultDescriptorActivationRequestService` + 真实控制面工具面 + 真实 HumanTask 批准，而不是仅测 parser。依赖边界（Authoring 不引用 ControlPlane）也不允许把这些断言放进 `CrestCreates.Agent.Authoring.Tests`。

### I-02 确定性 provider 替身与归因纪律（2026-11-03）

`B12RecordedModelClient` 读取真实提示链产生的 `Prompt.PromptInputHash` 并把它回显进录制响应。这一点是归因前提：若固定一个假 hash，parser 会先以 `AUTHORING_PROMPT_HASH_MISMATCH`（Blocker）失败，负例就会因为旁因而「通过」。同理，`BuildNeedsClarificationResponse` 与 `BuildEmptyAttemptResponse` 结构合法（`contractVersion = "7g.v1"` + 合法 plan），只是 `items` 为空，确保失败原因就是「无可用条目」。

副作用观测全部由测试侧装饰器/存储计数承担，不给生产加公共扩展点：

- `AssetActivationGateObserver`：唯一授权突变 runtime 状态的组件的调用/成功计数；
- `InMemoryAgentToolInvocationAuditor`：控制面工具调用记录（含状态与 touched ids）；
- `IDescriptorDraftStore.ListAsync` / `IAgentReviewArtifactStore.ListAsync`：工件是否落盘；
- `IDescriptorCatalog` 身份集合（`Kind|Namespace|Id|Version` 有序）与部署基线比对。

### I-03 用例与断言（2026-11-03）

| 用例 | 断言要点 |
| --- | --- |
| `SupportedIntent_OnTheSameChain_DeploysOnlyAfterHumanApproval`（绿色对照） | 同一组合、同一录制替身机制下：`Succeeded` → 1 草稿 → 提交为 `UnderReview` 且此时 gate 计数 0 → 人工批准后 `Activated` 且 gate 调用 1/成功 1 → 装载后目录新增 `ht_asset_maintenance_initial_review`。证明「零部署/工件不通过边界」类断言不是空断言 |
| `IntentNeedingNewDomainAlgorithm_YieldsExplicitUnsupportedResult_AndDeploysNothing` | `Blocked` + 恰好一条诊断，码为 `AUTHORING_UNKNOWN_DESCRIPTOR_KIND`、Severity=Error、消息含 `Capability`；草稿集为空、草稿库空、评审工件空、控制面审计记录为空、gate 0/0、部署目录等于基线；再伪造提交 → `NotFound`（`TOOL_TARGET_NOT_FOUND`），gate 仍 0、目录仍等于基线 |
| `IntentClaimingLifecycleRemoval_YieldsExplicitUnsupportedResult_AndKeepsDeployedWorkflow` | `Blocked` + `AUTHORING_UNSUPPORTED_DRAFT_OPERATION`；同一边界收口断言；额外断言 `wf_asset_maintenance_review` 仍以 v1 存在且类型为 `WorkflowDescriptor` |
| `IntentClaimingAuthoritativeMemory_YieldsExplicitGovernanceBoundaryResult_AndWritesNoArtifact` | `Blocked` + `AUTHORING_GOVERNANCE_BOUNDARY_VIOLATION`，消息含 `authoritative`；边界收口断言（无任何工件、无部署） |
| `IntentMissingPolicyInformation_FailsClosed_AndClarificationIsNotDistinguishable` | 缺策略信息的拒绝式响应 → `InvalidProviderOutput` + `AUTHORING_INVALID_PROVIDER_OUTPUT`，边界收口断言；随后与「什么都不返回」的响应比对：状态相同、诊断码集合逐项相同 → 记录「needs-clarification 与无效输出在当前契约下不可区分」这一事实 |
| `FabricatedDraftBypassingAuthoring_StopsAtReviewBoundary_AndNeverReachesApproval` | 绕过 authoring 白名单、直接以 `Operation=Remove` 的合法载荷工件进入真实链路：准备度预览 `IsReady=false` 且含 `NOT_ACTIVATION_ELIGIBLE`；真实评审产出工件但 `IsActivationEligible=false`、`MaterializationSummary.IsMaterialized=false`、诊断含 `UNSUPPORTED_OPERATION`（Error）；包预览 `Failed`；伪造绑定提交 → `InvalidRequest` + `ACTIVATION_PACKAGE_PREVIEW_NOT_FOUND`；无 `TouchedActivationRequestIds` 审计、gate 0/0、部署目录等于基线 |

`BaseVersion` 在 Remove 伪造工件上显式提供，使 `DefaultDescriptorDraftValidator` 通过校验（`DefaultDescriptorDraftValidator.cs:61-66`），失败原因唯一归到 `DefaultDescriptorDraftMaterializer.cs:26` 的 `Remove => Unsupported(...)`，而不是校验器噪声。

### I-04 契约层发现（不在本 Issue 范围内改动）

B12 的期望文本是「显式 unsupported / needs-clarification 结果」。当前契约可以把「显式不支持」表达为 `Blocked` + 结构级诊断码，但没有「需要澄清」的显式形态：模型按提示正确拒绝（合法 plan + `items: []`）得到的是 `InvalidProviderOutput`/`AUTHORING_INVALID_PROVIDER_OUTPUT`，与「模型什么都没产出」在状态与诊断码上完全一致，调用方无法区分「需要更多信息」与「输出无效」。

按 Issue 边界，本轮不改契约，只用 `IntentMissingPolicyInformation_FailsClosed_AndClarificationIsNotDistinguishable` 把该事实钉成可复核证据。若要把 needs-clarification 做成一等结果，需要同时动 wire 契约版本（现 `7g.v1`）、提示披露、`DescriptorAuthoringProviderOutputDto`、JsonContracts root 与 AOT fixture，属独立设计裁定，等维护者决定是否另建 Issue。

### I-05 opt-in live eval B12 用例（2026-11-03）

`DeepSeek_UnsupportedIntent_YieldsRestrictedResult_AndFabricatesNoCapability`，沿用既有 live eval 模式：

- 开关仍是 `AssetLiveEvalFact`（`CREST_ASSET_LIVE_EVAL=1`），默认 `Skip`，不设 CI 门禁；
- artifact 走 `CREST_ASSET_LIVE_EVAL_UNSUPPORTED_ARTIFACT`（缺省临时路径），记录 runId、requestedModel、context（focus ref/scope/描述符数/diagnostic 码）、authoring（status、诊断码、诊断分类、prompt 输入/输出 hash、observed provider/model、draftCount）、OutputBudget（`CREST_ASSET_LIVE_EVAL_MAX_OUTPUT_TOKENS` 解析结果与 `OUTPUT_BUDGET_*` 码）、HTTP 响应元数据、逐草稿/逐评审记录、semantic 结论；
- 结果分类：`ExplicitUnsupportedResult`（Blocked）、`NeedsClarificationNotExpressible`（InvalidProviderOutput，非预算耗尽）、`SubsetDraftOnlyNoUnsupportedClaim`（有草稿但没有可批准地声称部署面变更）、`OutputBudgetExhausted`、`ProviderUnavailable`、`AuthoringFailure`、`InvalidOutputBudget`、`UnhandledEvaluationFailure`；
- 无条件硬断言（与模型输出形态无关）：`FabricatedCapabilityObserved == false`（评审通过且指向 `wf_asset_maintenance_review` / `ht_asset_maintenance_review` 的草稿即判为伪造能力）、草稿库保存数 0、gate 调用 0、成功 0、部署目录不变；结果必须落在三种受限形态之一。
- `AssetActivationGateObserver` 的替换由 `ConfigureUnsupportedIntentServices` 统一提供，并被 `UnsupportedIntentEvalWiring_ReplacesGateWithObserver_WithoutNetwork` 在无网络条件下验证（可解析 agent 与观测器、观测器计数 0、HTTP 状态 0）。

诚实边界：本轮环境没有 `DEEPSEEK_API_KEY`，该 live 用例**未真实执行**，只验证了编译、默认 `Skip` 与接线探针；行为层「候选缺口/待验证」需要维护者用凭据跑一次并回填 artifact 结论后才能升级为已证实/已排除。

### I-06 判别力自证（变异审计，2026-11-03）

逐条关闭被测能力后重跑该测试类（`--filter FullyQualifiedName~AssetUnsupportedIntentNegativeCaseTests`），每条变异只打红对应负例、其余 5 条仍绿：

| 变异（临时改生产源码） | 目标 | 结果 |
| --- | --- | --- |
| `TryParseDescriptorKind` 额外接受 `Capability` | 新增领域算法类意图的白名单阻断 | `IntentNeedingNewDomainAlgorithm_...` Failed，1 fail / 5 pass |
| `TryParseOperation` 额外接受 `Remove` | 生命周期移除类意图的阻断 | `IntentClaimingLifecycleRemoval_...` Failed，1 fail / 5 pass |
| 权威记忆声明检查改为永不匹配的字符串 | 治理边界阻断 | `IntentClaimingAuthoritativeMemory_...` Failed，1 fail / 5 pass |
| 空 `items` 不再判 `InvalidProviderOutput` | 缺策略信息时的收口失败 | `IntentMissingPolicyInformation_...` Failed，1 fail / 5 pass |
| `Remove => Unsupported` 改为走 `MaterializeCreate` | 绕过白名单工件的评审/审批边界 | `FabricatedDraftBypassingAuthoring_...` Failed，1 fail / 5 pass |

变异全部就地还原，最终 `git diff -- src/` 为空（生产源码字节级回到基线），还原后该类 6/6 通过。

### I-07 本地验证（2026-11-03）

| 套件 | 结果 | 说明 |
| --- | --- | --- |
| `CrestCreates.Sample.AssetManagement.E2E.Tests`（全量） | 49 passed / 2 skipped / 0 failed，耗时 30s | 2 条 skip 即两条 live eval 用例（默认跳过）；需要 `ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING`，本轮用本地 PostgreSQL 容器（`postgres:16-alpine`，`127.0.0.1:5433`）提供；未设置该前置时同一套件有 19 条环境性失败（与 #129 日志记录一致，非本轮引入） |
| `CrestCreates.Agent.Authoring.Tests` | 76 passed / 0 failed | 未受本轮改动影响，回归确认 |
| `CrestCreates.Agent.ControlPlane.Tests` | 575 passed / 0 failed | 同上 |
| `CrestCreates.DescriptorDraft.Tests` | 132 passed / 0 failed | 同上 |
| `CrestCreates.Sample.AssetManagement.Tests` | 29 passed / 0 failed | 同上 |
| NativeAOT 门禁 | 未运行 | 本轮为测试/文档增量，无生产代码变化，未新增原生门禁，支持声明不变 |

## 未做的事与边界

- 未新增框架抽象、未建通用评估平台（spec Non-goals）。
- 未改变 `DescriptorAuthoringStatus`、诊断码集合、wire 契约 `7g.v1`、控制面契约。
- 未把 live eval 设为无条件 CI 门禁。
- 未重开 #87/#88，未推导 durable activation 状态。
- 行为层（真实模型是否伪造能力）仍未证实：缺凭据，见 I-05。
- Asset 之外的样例（LibraryManagement / SaaSHelpdesk）不套用本负例结论；需要各自链路的 fixture 才能声明。
