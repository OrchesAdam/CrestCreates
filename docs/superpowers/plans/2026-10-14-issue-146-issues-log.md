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
