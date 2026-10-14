# #146 实施问题日志（S1–S4 实况与边界）

- 分支：`feat/issue-146-local-event-framework-hardening-20261014-090027`（基线 master `5d3de8c5`，PR #145 合并后）。
- 契约：Issue #146 详细设计 rev.1 评论（2026-10-14，comment 6158450713）+ 正文验收矩阵。
- Spec：`docs/superpowers/specs/2026-10-14-issue-146-local-event-framework-hardening-design.md`（用户设计，逐字入库）。
- 计划：`docs/superpowers/plans/2026-10-14-issue-146-local-event-framework-hardening-implementation.md`（用户交接计划，逐字入库）。
- 编排核对（开工时）：#127 已合并；#128 未开工；唯一在途 PR 为 #147（#127 合并状态 docs 同步，不触碰工具链）→ 无需共享 CodeGenerator/JsonContracts 协调。

## S1 — 契约锁定与失败基线（本提交）

### 失败基线（真实行为错误，非"新 API 不存在"）

单元（`tests/Runtime/Eventing/CrestCreates.EventBus.Tests/LocalEventHardeningBaselineTests.cs`，3/3 按设计失败）：

| 基线 | 旧实现真实错误（运行输出） |
| --- | --- |
| H01 `Host_B_borrowing_a_process_global_codec_must_fail_freeze` | freeze **未抛** `LocalEventDispatchConfigurationException`（Host B 未激活所属贡献，仅凭进程全局表通过） |
| H02 `Conflicting_process_global_codecs_for_one_type_must_not_be_first_wins` | freeze **未抛**（同 Type 第二份不同 codec 被 TryAdd 静默忽略） |
| P01 `Transient_handler_is_constructed_once_and_executed_as_the_same_instance` | 构造次数 Expected 1 / Actual **2**（Probe 与 Invoke 双重解析） |

集成（`tests/Runtime/Eventing/CrestCreates.EventBus.Local.Tests.Integration/LocalEventHardeningBaselineIntegrationTests.cs`，4/4 按设计失败）：

| 基线 | 旧实现真实错误（运行输出） |
| --- | --- |
| C01 `Recorder_must_capture_the_ambient_tenant_and_correlation` | 记录 TenantId Expected "hardening-tenant-A" / Actual **null** |
| C01b `Replay_must_execute_under_the_recorded_tenant_not_the_callers_tenant` | handler 观察到 Expected 记录租户 A / Actual 调用方 **B**（A→B 泄漏） |
| G01 `Recorder_must_keep_the_accepted_identity_when_the_registry_reports_another_version` | 记录集合 **空**（registry Built 且 GetByPayloadType 返回 v2 时，v1 绑定失败被整体丢弃） |
| R01 `Decode_failure_must_not_mark_retrying_nor_consume_the_retry_budget` | 坏 JSON 重放后 RetryCount Expected 0 / Actual **1**（MarkRetrying 先于解码，状态/预算被改） |

套件状态（本 head）：单元 37 过 + 3 基线失败（40）；集成 47 过 + 4 基线失败（51）——**既有用例零回归，失败全部为设计目标行为**。

### 支撑实验与核对

- Typed bridge 编译与行为实验（`/tmp/crest-146-s1/bridge-spike`，三程序集 Contracts/Bridge/App）：公开薄桥（abstract `EventInvoker.Prepare` + `PreparedInvocation`）跨程序集编译通过；generated-style 闭合 stand-in 一次解析 Handler 数组、`ExecuteAsync` 只用该数组，运行结果 `constructs=1 handled=1+1`（P01 目标形状可达成）。handler-only 程序集不得引用 owner internal JSON context 的负例已有 #126 `LocalEventCrossAssemblyTests`（CS0122）覆盖，不重复建设。
- Bootstrap 挂接核对：#126 的 Channel intake 是 `BackgroundChannelLocalEventBusConsumer : BackgroundService`（StartAsync 即消费），与 bootstrap 无顺序约束；`BootstrapCoordinator`（拓扑排序 IBootstrapTask）仅 Runtime.Delivery 注册；`EventRegistryBootstrapper`（TaskId `event-registry`）未被任何组合注册；#127 RabbitMQ composition hosted service 直接 Build registry。结论（与 Spec §3.3 一致）：S3 readiness 不得假设启动顺序——以"registry 已 Built 即通过 + 未 Built fail-closed 且不缓存为永久失败"为契约；hosted 场景可在既有 bootstrap 完成后重试，非 hosted 首次执行前检查。
- ContextVersion 历史 null / 版本 alias / 释放失败与结算 Unknown 矩阵：已在 Spec §7.3、§8 锁定（用户设计逐字入库），S1 不再重复定义；S2/S3 按此实现。

### 计划偏差（主动记录）

- "旧 key 改名" 的最小失败用例延后至 S2/S3 与 alias API 同步落地：现有实现下该场景的唯一可写形式必然引用尚不存在的显式 replay-key alias API（违反"不只断言新 API 不存在"），且非空 key 无 fallback 的拒绝行为本身是 #126 的既有正确语义（Spec I01/I02 保留它）。其验收按 Spec I01–I03 在 S3 提供，S1 不制造假基线。
- typed bridge 编译样例以 /tmp 三程序集实验（上方）交付，不预置生产 API（设计审查门禁要求 S2 前不改生产公共面）。

### 设计审查门禁

按计划："设计审查通过后再实施 S2/S3"。状态更新（2026-10-14）：设计审查 R1 已到（1×P1+2×P2），修订已完成（见下节），当前等待复审放行 S2。

## S1 设计审查 R1 修订（PR #148，审查 head `6f38f971`）

审查结论：1×P1 + 2×P2，设计门禁暂不通过；7 条基线红色符合 S1 约定、旧 key 偏差可接受。修订（commit 见本分支）：

| 发现 | 修订 | 落点 |
| --- | --- | --- |
| R1-LE146-01（P1）独立 LocalEvent Host 的 registry 启动责任 | Spec 升 rev.2 并重写 §3.3：①Governance.Required 时 Local 组合 TryAddEnumerable 既有 `EventRegistryBootstrapper` 与 `BootstrapCoordinator` + 新增 `local-governance-readiness` 任务（依赖 `event-registry`，Gate 模式，零 provider 装配为启动期可操作诊断）；②任务由同一 coordinator 拓扑驱动，intake 只 await gate、不跨 hosted service 等待；LocalEvent+RabbitMQ 两种注册顺序等价（coordinator 唯一、Build 单次）；③非 hosted 未 Built 明确拒绝、不缓存、bootstrap 后重试通过；④无 Governance 的普通通知零 bootstrap 依赖 | Spec §3.3 四点契约 + 新增验收 **G06/G07**；计划 S3 增装配/驱动条目并纳入验证行 |
| R1-LE146-02（P2）上下文正向基线缺设施 | 两个正向 fixture 补齐目标契约设施：recorder 例补 `ITenantProvider`；replay 例补 `IAuditOperationContextAccessor` + `IAuditIdentityGenerator`（既有生产接口），并断言恢复出的 TenantId、原 CorrelationId 与调用方租户帧恢复；新增两条**缺设施拒绝负例**（专用无依赖 Handler：今日观测到业务真实执行 → `Assert.False` 失败、状态/预算被改，证明缺失的是 admission 拒绝而非 DI 崩溃）；Spec §7.2 "若声明需关联恢复"改为无条件规则：非空持久 CorrelationId 必须恢复、缺能力执行前拒绝、无开关、不静默降级 | 基线文件 2 改 + 2 增；Spec §7.2 |
| R1-LE146-03（P2）R01 不覆盖冻结后工厂变化 | 新增 H03 独立基线 2 条：先成功冻结，再把可变工厂产物置 null；`Factory_mutation_after_freeze_must_not_break_failure_recording`（今日 DLQ 集合为空——记录丢失）与 `..._must_not_break_replay`（今日 "resolved no JSON codec at replay time" 拒绝） | 基线文件 +2；计划勾选项说明修正 |

范围澄清（回应审查）：G01 只证明"非治理事件不被 advisory registry 的另一版本破坏记录"，**不等于**已交付 Governance.Required 精确版本拒绝（G02–G05 仍待 S3）。

修订后计数：单元 37 过 + 3 基线失败（40）；集成 47 过 + 8 基线失败（55，原 4 + 缺设施 2 + H03 2）；既有零回归。

## S1 设计审查 R2（PR #148，审查 head `d32f8b42`）

- R2-LE146-01（P2）缺 audit 负例的 tenant provider 为空、未登记租户 A：按 Spec"先租户、后关联恢复"的检查顺序，"未知租户拒绝"足以单独满足 `Success=false`、零 Handler、Pending/预算不变，即使关联能力检查完全遗漏也可假绿。修订（按审查最小方案）：登记有效租户 A，使唯一缺失条件保持为 audit accessor/identity；并在调用 executor 前加**解析守卫**（provider 对记录 TenantId 解析非空且 Id Ordinal 匹配），防后续 fixture 漂移重新引入未知租户路径。保持 accessor/identity 未注册，继续断言零业务调用、Pending 与预算不变。修订后该用例仍按 S1 红色且失败原因纯净：今日观测为业务真实执行（Assert.False Expected False / Actual True）。S3 引入结构化 ProblemCode 后把关联能力缺失断言进一步收紧。

## S2 — 生命周期收口（本切片，design-review 放行后实施）

### 交付（对照 Spec §3/§4/§8）

- **声明与冻结分离**：新增 `LocalEventRegistration`（builder 声明 DTO，携带装配期 codec 工厂与元数据）与 `LocalEventJsonCodecRegistration`（per-Host codec 贡献，`AddCodecs(IServiceCollection)`）；`LocalEventBinding` 成为真正 frozen 执行项（`Codec` 冻结期解析一次、无可变集合、无工厂/Provider/scope/Handler 引用；构造 internal + IVT）；旧 `LocalEventJsonCodecRegistry` 归档 `99_RecycleBin/issue-146-local-event-hardening/`（无全局表、无 fallback）。
- **逐项冻结校验**：`Freeze` 不再按 ContributorId 跳过（H04：同 id 冲突不被吞、同贡献重复启用幂等，均有测试）；codec 贡献逐项校验（factory 结果非空/精确 Type/`Options.IsReadOnly`/同源同实例/跨源冲突，与注册顺序无关）；注册级元数据单值一致（Replayable/版本/来源合并为排序不可变集合）；replayable 无 codec → 含 AddLocalEvents 指引的确定性违规。
- **单次准备、同一批实例**：`LocalEventInvoker<TEvent>` 改为统一泛型 Prepare 协议（单次 `GetServices<ILocalEventHandler<T>>().ToArray()`、required 前置校验、`LocalEventPreparedInvocation` 持有实例数组且单次执行、`AnyHandlerInvoked` 事实）；dispatcher/bus/Channel consumer/replay 全部经共用 `LocalEventExecution` 核心；P01（Transient 单次构造）转绿。
- **重放状态机重排（Spec §8 矩阵）**：解码（frozen codec）与 Prepare 全部先于 `MarkRetrying`；`MarkRetrying` 未确认（提交后抛）→ NotStarted + StateWrite.Unknown + 零 Handler 且不回滚 Pending（R02 故障注入测试）；零 Handler/构造失败 → 拒绝且状态/预算不变（P02a/P02b）；成功 → Completed 事实先于释放，释放失败独立报告且不产生第二条死信（R03/P03：async-only disposable 恰好一次）；Handler 失败+释放失败 → Failed + Release.Failed + 预算只计一次（R04）；协作取消 → 有界恢复 Pending、重试计数不增加、向调用方传播。`DeadLetterRetryResult` additive：`Execution/StateWrite/Release/AnyHandlerInvoked/ProblemCode`（位置构造调用保持兼容）。
- **recorder 迁移**：使用 binding 冻结身份与冻结 codec（不再 advisory `GetByPayloadType`、不再执行期调用工厂）；G01 转绿，H03（冻结后工厂变化）记录/重放两侧转绿。
- **#126 语义修正用例**：`CorruptPayload_CountsTowardBudget_AndArchivesAtLimit` 按 #146 R01 更新为 `CorruptPayload_Is_Refused_Before_Any_State_Transition_And_Keeps_Budget`（坏 JSON 不计预算、不入档、零业务调用）。
- **保留显式 validator 行为**：发布/Channel 入口的可选 validator 调用未动（S3 原子迁移治理策略时处理）。

### 迁移面与结果（本地）

- 工具链：`LocalEventCodeWriter` 生成 `LocalEventRegistration.Create<Event>()` + 无参闭合 invoker + `LocalEventJsonCodecContributions.AddCodecs(services)`；`JsonContractSourceWriter` 本地块同迁（LocalEvent JSON 命名策略不变：不加 camelCase）。CodeGenerator 定向 14/14、JsonContracts 131/131。
- 测试迁移：单元 `LocalEventDispatchRegistryTests` 全迁移并新增 DI 贡献用例；S1 基线文件 H01/H02/P01 按目标语义改写转绿（新增同源冲突用例）；集成 `LocalEventTestComposition`/scoped-store/AotHost/Capability 测试同步；新增 `LocalEventHardeningStateMachineTests`（H04×2、P02×2、R02、P03、R03、R04，共 8 条）。
- 计数：单元 42/42；集成 63 中 59 过 + **4 条 S3 范围内红**（上下文捕获/恢复/缺设施拒绝——按设计留待 S3）；Event 41/41、生成器全量 365/365、Capability 178/178、RabbitMQ 单元 58/58、RabbitMQ 真实 broker 集成 37/37、边界 173/178（5 项既有环境态）、全量构建 0 错误。
- 回归修正记录：首轮批跑中 RabbitMQ 集成全红为本地 broker 容器停机（已重启后 37/37）；LocalEvent 原生门禁一度失败为 AotHost refusal 场景仍断言 #126 旧预算语义，已按 #146 R01 更新（坏 JSON 零预算、不入档）后门禁通过（S4 正式留证与证据 json）。

## S2 实现审查 R1（PR #148，审查 head `ccae06f2`）

审查结论：2×P1 + 1×P2，S2 暂不通过。三个审查探针按惯例逐字入库（`ReviewS2LifecycleProbes.cs`，入库基线 3/3 红 → 修复后 3/3 绿），并按审查要求补 4 条自带组合回归。

| 发现 | 修订 | 落点与验证 |
| --- | --- | --- |
| S2-R1-LE146-01（P1）只读 options 不等于冻结 JsonTypeInfo | `BuildCodecIndex` 通过 options 只读校验后**锁定 metadata 本身**：`info.IsReadOnly` 为假时调用 `JsonTypeInfo.MakeReadOnly()`（冻结后属性重命名 / 增删属性 / 清空属性列表均抛 `InvalidOperationException`，序列化行为不变）；锁定抛错或锁定后仍报告可变 → 确定性违规拒绝；source-gen 实例本已只读（跳过，无副作用）。 | `LocalEventDispatchRegistry.BuildCodecIndex`；探针 `Readonly_options_must_not_leave_frozen_codec_metadata_mutable`（只读 options + 可变 metadata）转绿 |
| S2-R1-LE146-02（P1）Channel 把成功后的释放错误当成可重放业务失败 | `ProcessEventAsync` 拆分生命周期：dispatcher 调用与 scope 释放不再共享 catch；**Handler 完成点即执行事实**——dispatcher 正常返回后释放异常只记结构化诊断，不进入 recorder、不产生可重放死信；业务失败 + 同时释放失败时业务失败仍恰好记录一条 Pending。 | `BackgroundChannelLocalEventBusConsumer`；探针 `Channel_success_must_not_become_replayable_failure_because_scope_release_throws` 转绿；自带回归 2 条（成功+释放失败零死信、业务失败+释放失败仍一条） |
| S2-R1-LE146-03（P2）协作取消丢弃同时发生的释放错误 | 三个取消分支（Prepare / MarkRetrying / Execute）全部保留 `TryDisposeScopeAsync` 返回值：结构化错误日志 + 在被重抛的同一 `OperationCanceledException` 上附加 `Data` 事实（`LocalEventReplayExecution` / `LocalEventReplaySettlement` / `LocalEventReplayRelease`）；`throw;` 保持调用方取消语义。 | `LocalDeadLetterReplayExecutor`；探针 `Cooperative_cancellation_must_not_silently_drop_release_failure` 转绿；自带回归 2 条（取消+释放失败断言 Data 事实与恰好一次释放、取消+恢复写失败断言 `RestorePendingUnconfirmed` 且状态停在 Retrying） |

- 探针入库：`ReviewS2LifecycleProbes.cs` 逐字 `cp` 入库（含 `ReviewDisposeState`/`ReviewThrowingRelease`/`ReviewCancelHandler`/`ReviewSuccessfulChannelHandler`/`ReviewLogs`），命名空间与既有集成套件一致、无生产测试扩展点。
- 契约对照：Spec §4"codec metadata 及关联 options 在进入 binding 前变为只读；若无法安全锁定，冻结失败"与 H03 验收行"可变 metadata 被锁定或拒绝"为本轮实现补齐项，Spec 无需修订。
- 修复后计数：单元 42/42；集成 70 中 66 过 + **4 条 S3 范围内红**（不变）；Event 41/41；Capability 178/178；CodeGenerator 365/365；JsonContracts 131/131；边界 173/178（5 项既有环境态）；LocalEvent 真实 NativeAOT 门禁通过。

## S2 实现复审 R2（PR #148，审查 head `33ba7040`）

审查结论：R1 三项（2×P1+1×P2）全部关闭；新增 1×P2，S2 门禁待修正。

| 发现 | 修订 | 落点与验证 |
| --- | --- | --- |
| S2-R2-LE146-01（P2）执行中协作取消被记录为业务执行失败 | 执行期取消分支（`prepared.ExecuteAsync` 的 caller-cancellation catch）的 `Data["LocalEventReplayExecution"]` 由条件写入 `Failed/NotStarted` 改为按 Spec §8 / `LocalEventExecutionOutcome` 统一报 **`Cancelled`**；Handler 是否已进入保持独立事实 `Data["LocalEventReplayAnyHandlerInvoked"]`。Prepare / MarkRetrying 两个取消分支维持 `NotStarted`（与 §8 矩阵行一致：准备前停止 / MarkRetrying 未确认）。原 OCE 传播、释放错误保留、Pending 有界恢复与预算不变行为全部保持。 | `LocalDeadLetterReplayExecutor`；两条组合回归断言改为 `LocalEventExecutionOutcome.Cancelled.ToString()` 并新增 AnyHandlerInvoked 事实断言（审查在临时副本中改期望即复现 Expected Cancelled / Actual Failed） |

## S3 — 身份、策略与上下文（design/S2 放行后实施）

### 交付（对照 Spec §3.3/§5/§6/§7/§8/§9）

- **稳定键与 alias**：`LocalEventAttribute` 增 `ReplayKey/Governance/TenantMode`；新增 repeatable `LocalEventReplayAliasAttribute` 与 `LocalEventReplayAlias`；冻结校验键格式（Ordinal、无首尾空白/控制字符）、alias 版本必须等于目标 ReplayVersion、alias 与现行键共用冲突空间（双向检测）；`TryGetByReplayKey` 现行键→alias；未知非空键不降级 payload identity（I02）；重放不改原行键（I01 迁移用例断言）。
- **声明策略面**：`LocalEventPolicyOptions`（ReplayKey/Aliases/Governance/TenantMode/执行 check/validator 激活/历史 host 认可/治理历史采用）进入 `LocalEventRegistration` → frozen `LocalEventBinding`；单值身份元数据交叉一致、激活开关加法合并；freeze 用 `IServiceProviderIsService` 校验 check/validator 的 DI 注册（缺注册即组装失败）；检测到 IEventValidator 注册而零激活时给出迁移诊断日志。
- **共用 admission**：`LocalEventAdmission` 覆盖发布/直接 dispatcher/Channel 入队与消费/重放：Governance.Required 精确 descriptor（payload+version 唯一、State.Active）、persisted descriptorId 匹配、null-id 历史行需显式采用映射、非治理声明读取带 id 记录拒绝降级；执行 check 结构化拒绝；validator 显式激活。拒绝不接受、不建 DLQ；Channel 消费拒绝用原 work item 快照落诊断行。
- **bootstrap 就绪（§3.3 rev.2）**：`AddCrestLocalEventGovernance` = AddEventKernel + TryAddEnumerable 既有 `EventRegistryBootstrapper`（IBootstrapTask）+ 既有 `BootstrapCoordinator`（IHostedService）+ 新 `local-governance-readiness` 任务（依赖 event-registry；零 descriptor provider 为启动期可操作装配错误）；`LocalEventGovernanceReadiness` gate 由 admission 等待（hosted）；非 hosted 未 Built 直接可操作拒绝且不缓存（bootstrap 后重试通过，G06③）；生成器在含 Governance.Required 声明的程序集 `AddLocalEvents()` 自动调用；LocalEvent+RabbitMQ 双注册顺序等价（coordinator 单实例、registry 单次 Build，G07）。
- **上下文捕获与恢复（§7）**：`LocalEventExecutionContextSnapshot`（版本=1 或 legacy null）/`LocalEventWorkItem`/`LocalEventAcceptedIdentity`；发布/入队边界捕获（audit 与 current tenant 冲突或不可恢复即捕获失败）、Channel 消费恢复、recorder 使用原快照+已接受身份（不再 advisory 选择版本）；恢复协议分离 await `ITenantProvider` 解析与执行帧同步 `ICurrentTenant.Change`/audit `Push`（LIFO frame、逐层独立释放、聚合次要错误）；`ICurrentTenant.Change(ITenantInfo?)` nullable host 语义。
- **租户模式与历史矩阵（§7.3）**：TenantMode.Optional/Required/HostOnly × v1/legacy/未知 ContextVersion 全矩阵在 admission 前执行：未知租户/名称 fallback 得到不同 Id/缺 provider/缺 current tenant/缺 audit 设施/未知版本全部执行前拒绝且不落 host；legacy null-context 默认拒绝，显式 per-binding `AllowHistoricalHostContext` 才允许且不覆盖 Required/HostOnly。
- **持久化（§9，S3 范围内）**：`DeadLetterMessage.LocalContextVersion` additive 可空；EF 实体/映射/store 往返；`DeadLetterSchemaUpgrade` 提供可重复 PG 脚本（`ADD COLUMN IF NOT EXISTS`，不 DROP/不重写）与只读列检测；真实旧 schema 双次升级与 store readiness 能力接口留待 S4 真实 PG fixture。

### 夹具与计数（本地，S3 实施后）

- 新增集成 `LocalEventGovernanceAndContextTests` 22 条（I01×2/I02/I03×2、G02×2、G03×2、G04、G05×3、G06×3、G07、C02×2、C03×2、C04/C05 组合、Channel 捕获恢复）；EF 模型 +2（可空列、往返含 legacy null 保真）。
- 4 条 S3 约定红基线（recorder 捕获、A/B tenant 恢复、缺 provider/audit 拒绝）全部转绿；5 条旧夹具按目标语义修正：modern 记录带 ContextVersion=1（`CreateModernMessage`/AotHost SeedMessage/H03 用例），legacy 工厂保持 null，两条 legacy 身份语义用例补显式历史 host 策略。
- 计数：单元 42/42；集成 **94/94**；CodeGenerator 367/367（+2 策略发射）；JsonContracts 131/131；LocalEvent 真实 NativeAOT 门禁在本 head 通过（linux-x64）；全量构建 0 错误；边界/Event/Capability/RabbitMQ 单元见同批日志。

## S3 实现审查 R1（PR #148，审查 head `73bb4213`）

审查结论：4×P1 + 3×P2，S3 暂不通过；11 条独立运行期契约探针 + 抽象依赖编译对照全部复现。修订逐项如下，并新增 `LocalEventGovernanceReviewR1Tests`（12 条）转稳定回归。

| 发现 | 修订 | 落点与验证 |
| --- | --- | --- |
| S3-R1-LE146-01（P1）租户模式未覆盖发布/直接分发/入队边界 | `LocalEventAdmission` 前置"上下文格式与租户可恢复性"：HostOnly 有租户 / Required 无有效租户 / 非空租户但缺 `ITenantProvider`（Optional 亦拒）在**所有接受边界**（同步发布、直接 dispatcher、Channel 入队）拒绝，零 Handler、零入队副作用；消费侧 `ResolveAsync` 复查保留。 | 3 边界 × 3 模式矩阵回归（publish/direct/enqueue 各断言 ProblemCode、零业务、Channel 负例断言队列无副作用） |
| S3-R1-LE146-02（P1）冻结 binding 仍引用可变执行检查列表 | `LocalEventBinding` 改收 `ImmutableArray<Type> executionCheckTypes` 并在构造时 `CreateRange` 独立复制；Freeze 传入 `registration.ExecutionCheckTypes.ToImmutableArray()`；宿主原集合在冻结后清空不再影响 binding。 | 回归：freeze 后 Clear 原 List，binding 检查数不变且分发仍被拒绝 |
| S3-R1-LE146-03（P1）Required 的空 TenantId 绕过校验按 host 重放 | 新增 `LocalEventExecutionContextSnapshot.FromPersisted`（空/空白 id 归一为 null）与 `Capture` 归一；`ResolveAsync`/admission 的模式判定统一用 `IsNullOrEmpty`：Required 不允许空串，损坏值不再被转换成可执行 host。 | 回归：v1 + Required + TenantId="" 的重放拒绝且预算/状态不变；Channel 空租户 work item 落一条诊断行、零业务 |
| S3-R1-LE146-04（P1）治理属性令仅引用抽象的契约程序集编译失败 | 生成器仅当编译实际引用 `CrestCreates.EventBus.Local` 时才在内联 `AddLocalEvents()` 发出 `AddCrestLocalEventGovernance(services)`（`ReferencesLocalRuntime`）；契约程序集只贡献抽象侧治理声明，Host 的 Local 组合显式装配。 | 生成器回归：仅 Abstractions 引用 + Governance.Required 编译 0 错误且生成文本不含 Local；含 Local 引用仍发出调用 |
| S3-R1-LE146-05（P2）非 hosted 治理调用一直等待未启动的 gate | admission 对 readiness gate 采用**有界等待**（5s，链接调用方 CT）：gate 未在界内就绪则回落 `GovernanceRegistryNotReady` 可操作拒绝且不缓存；调用方取消仍按取消传播；gate fault 保持 fail-closed。 | 回归：组合了治理但未驱动 coordinator 的 provider → 有界拒绝（含耗时上界断言）→ 启动 coordinator 后重试通过 |
| S3-R1-LE146-06（P2）重放 admission 普通异常跳过 scope/帧清理 | executor 对 `AdmitAsync` 增加普通异常分支：LIFO 释放业务 scope 与 context frame、记录独立释放事实，返回 `AdmissionFailed`（Execution=NotStarted、不动预算、不建新死信）。 | 回归：check 工厂"先解析可释放对象再抛"→ 释放恰好一次、tenant frame 恢复、行保持 Pending/预算 0 |
| S3-R1-LE146-07（P2）上下文激活第二步失败遗失 tenant frame | `Activate` 对部分成功的取得过程负责：后续步骤（identity/Push）抛错时按 LIFO 撤销已进入的帧并清空，原错误保持；清理也失败则 AggregateException 双事实报告。 | 回归：audit Push 抛错 → replay 返回 `ContextRestoreFailed`、租户帧恢复至调用方、行/预算不变 |

修复后本地计数：单元 42/42；集成 **106/106**（+12 回归）；CodeGenerator 全量、JsonContracts、LocalEvent 真实 NativeAOT 门禁、全量构建见同批日志；边界/Event/Capability/RabbitMQ 单元不受本修订影响、S3 落地时计数保持。

## S3 实现审查 R2（PR #148，审查 head `b6f7b973`）

审查结论：原七项关闭六项，新 2×P1 + 1×P2；修订如下，ReviewR1 回归套件扩至 20 条。

| 发现 | 修订 | 落点与验证 |
| --- | --- | --- |
| S3-R2-LE146-01（P1，承接 01 未闭环）边界只见服务存在、缺有效解析/精确 Id/关联能力 | `LocalEventContextBridge.ValidateRecoverabilityAsync`（返回解析出的 tenant）成为所有接受/执行边界共用前置：格式、损坏值、模式、（非空租户）provider 存在 + **权威解析 + Id Ordinal 相等**、（非空关联）audit accessor/identity 能力；admission 全边界调用，`ResolveAsync` 复用之并追加 ICurrentTenant 激活能力。 | 新回归：publish 正例（解析通过）；UnknownTenant（publish + enqueue 双边界、零副作用）；名称 fallback 得到不同 Id → TenantIdMismatch；有 correlation 无 identity generator → MissingAuditFacility |
| S3-R2-LE146-02（P1）空持久租户被归一成 host 执行 | `FromPersisted` **保留原始 tenant 值**（不再归一）；`ValidateRecoverabilityAsync` 先拒绝损坏值（`InvalidTenantId`，空/空白，先于版本与模式判定、先于建 scope）——真正 null 才是明确 host；无全局 Normalize 授予 host 含义。 | 回归：Optional/HostOnly/Required × TenantId="" 三模式重放全部 `InvalidTenantId`、NotStarted、Pending/预算不变；Channel 损坏 work item 诊断行断言 "damaged" |
| S3-R2-LE146-03（P2）激活+撤销双失败吞掉原取消并误报 Release.Completed | `Activate`：撤销失败时——原错误为 OCE → 保留 OCE 身份并把清理失败附加到 `Data["LocalEventContextCleanupFailure"]`；非取消 → 抛 `LocalEventContextActivationException`（携带 CleanupFailure）；executor 新增该异常分支：清理失败合并进 `Release=Failed`（不再只写日志/文本）。`DisposeAndCapture` 多失败聚合修正为完整 AggregateException。 | 回归：Push 普通失败+tenant 撤销失败 → `ContextRestoreFailed` + **Release=Failed** + NotStarted/Pending/预算 0；Push 阶段取消+撤销失败 → 原 OCE 正常传播且 `Data` 保留撤销失败、状态/预算不变 |

## S3 实现审查 R3（PR #148，审查 head `47bd9c19`）

审查结论：两项 P1 已关闭；剩余 1×P2（S3-R2-LE146-02 的非调用方 OCE 分支）。修订：

- **非调用方 OCE + 帧撤销失败误报 Release.Completed**：`Activate` 的 OCE 路径把清理失败以**异常对象**附加到 `Data["LocalEventContextCleanupFailure"]`（此前为字符串）；executor 新增 `ExtractFrameCleanupFailure`（同时识别 `LocalEventContextActivationException.CleanupFailure` 与 OCE Data），在**调用方取消分支**与**普通恢复失败分支**均合并进释放事实——非调用方 OCE 走 generic 恢复失败 → `ContextRestoreFailed` + `Release=Failed`；调用方同一 OCE 传播与清理事实保持。
- 回归：`Non_caller_cancellation_with_failed_unwind_reports_release_failed`（调用方 token 未取消、Push 抛非调用方 OCE、撤销失败 → NotStarted + Release.Failed + Pending/预算 0）；上轮 13 条探针与 20 条 ReviewR1 套件全部保持转绿。
