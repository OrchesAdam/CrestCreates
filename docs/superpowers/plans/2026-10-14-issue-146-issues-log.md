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

按计划："设计审查通过后再实施 S2/S3"。本分支当前状态：Spec/计划入库 + 7 条失败基线 + 实验结论；等待设计审查意见后进入 S2（生命周期收口）。
