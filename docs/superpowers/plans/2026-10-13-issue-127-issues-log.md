# #127 实施问题日志（S1–S4 实况与边界）

- 分支：`feat/issue-127-rabbitmq-static-bindings-20261013-145133`（基线 master `6dd608a3`）。
- 契约：Issue #127 详细设计评论（2026-10-13）+ Spec `docs/superpowers/specs/2026-10-13-issue-127-rabbitmq-static-bindings-design.md`。
- 计划：`docs/superpowers/plans/2026-10-13-issue-127-rabbitmq-static-bindings.md`（含 S1 实验全记录）。

## S1 — Spec 与 SDK/接线实验（commit `208f1b0d`）

- 现状代码事实 F1–F10（Spec §1.1）全部文件级核实，其中关键结论：`GetSubscriptions` 反射桥在任何真实 Host 恒为空（生成注册表在业务程序集，反射查框架程序集）；`[CrestEvent] → IEventDescriptorProvider → IEventRegistry` 生成接线全仓不存在（`AddEventKernel` 零调用方、三个事件 registry validator 零 DI 注册、仓库内 `[CrestEvent]` 声明数 0）。
- 实验 A（confirm，`/tmp/crest-127-s1/confirm-exp`）：tracking ON 时 `await BasicPublishAsync` 参与确认等待；unroutable → `PublishReturnException`、channel 存活；tracking OFF 静默。锁定移除自制 confirm 账本。
- 实验 B（最小 NativeAOT + broker，`/tmp/crest-127-s1/aot-exp`）：干净 publish 0 条 IL2026/IL3050；原生产物对真实 broker 完成发布（await confirm）→ 消费 → typed JSON round-trip → Ack → sentinel、exit 0。判定锁定组合无 native blocker。
- 实验 C（bootstrap 接线）：DI 版为锁定主链；`DescriptorProviderRegistry`（ModuleInitializer 静态 bag）不扩展、不新增使用。
- 实验 D（跨程序集 JSON 顺序）：沿 #126 结论（BuildTasks 磁盘 roots → STJ SG）；codec 只由事件所属程序集生成；装配顺序靠“先注册、后冻结解析”。
- 已发现问题（记录，不在本 Issue 修复）：`CrestCreates.EventBus.Kafka` 走同一 `EventNamingConvention` 短名/下划线路径（F4 同类），本任务不动 Kafka，仅在 RabbitMQ 路径停止使用该路由约定。

## S2 — 静态生成、codec 与唯一装配（commit `a871f63f`）

- 事件侧：`EventDescriptorSourceGenerator`（`[CrestEvent]` → `GeneratedEventDescriptorProvider` + `AddEventDescriptors()` 扩展；诊断 CCCEV001–003；Id 缺省调用 `GenerateId`，显式 Scope 强制、Version≥1）；Integration 声明缺 JsonContracts 工具链 = 编译错误（对照 CCCLE004）。
- JSON 工具链：`IntegrationEventJsonSurfaceDiscovery`（CJC018/019/020 复用既有闭包校验器）+ `EventJsonSerializerContext` + `EventJsonCodecContributions`（注册进新 `EventJsonCodecRegistry`，Event.Abstractions 静态索引，形态镜像 `LocalEventJsonCodecRegistry`）；`CrestCreatesEventJsonContracts` marker + CompilerVisibleProperty。
- 传输层：`RabbitMqBindingFreeze`（identity 唯一性 / canonical 引用 / 精确 payload Type / codec 快照 / alias 校验 / 队列计划与 prefetch 冲突 / required 清单 / 零订阅、PublisherOnly 语义；`RabbitMqCompositionException` 逐条违规）；`RabbitMqCompositionHostedService`（EventRegistry Built → 冻结 → readiness gate）；consumer 第一步 `await readiness`；`AddRabbitMqEventBus` 唯一入口（module/旧重载转发；旧 context 重载 = typed codec 适配器参与同一冻结）；`AddEventKernel` 补齐 validators/resolver/`RegistryEventValidator` DI。
- 生成器重写：完整 symbol 匹配、`Task (TEvent, CancellationToken)` 精确签名、闭合 invoker（canonical 名绝不进类型位置）、Handlers scoped 注册；CCCRMQ001–005。
- 实现期教训（保留在提交与测试里）：
  1. **冻结时机**：`IHostedService` 全部先构造再 StartAsync——consumer 不能在 ctor 解析冻结 registry（曾在真实 Host 触发“EventRegistry is not built”）。consumer 改为 gate 之后解析；冻结工厂保持“必须 Built”强校验。
  2. **wire 命名策略**：生成 codec context 必须显式 `JsonKnownNamingPolicy.CamelCase`（与 envelope 一致）——否则外部 camelCase JSON 表面“解析成功”实则空字段。
  3. 事件发布入口要求 payload 实现 `IDomainEvent`（`IEventBus` 契约）；fixture 事件据此实现。
- 存档：旧 `RabbitMqSubscriptionInfo`/`RabbitMqSubscriptionModel`/占位 `ConnectionPoolTests`/`PublisherTests` → `99_RecycleBin/issue-127-rabbitmq-static-bindings/`。

## S3 — 发布消费与结算闭环（commit `d5b00543`）

- 三个 fixture 工程（ContractsA 事件 + 探针、HandlersA 处理器、ContractsB 同短名事件 + 显式 RoutingKey 处理器），全部走真实生成管线（toolchain / 生成扩展）。
- 集成 harness：vhost/测试隔离（管理 API 创建/删除）、拓扑就绪等待（StartAsync 返回时 consumer 拓扑尚未声明）、静态 hook（进程级 → 套件串行化）、xunit 日志桥、broker 不可达 = 失败（不 skip）。
- 27 用例：正常消费 6、拒绝 9、重试/DLQ 4、结算/生命周期 4、宿主组合 3、legacy alias 1。全部对真实 broker 通过。
- 实现期教训：
  1. 测试断言必须与实现语义一致：静态 hook 的 `bool` 标志跨线程读写要 `volatile`（曾造成“第一次失败→重放”竞态的假阴性/假阳性）。
  2. 管理 API `get`（ack_requeue_false）的 ack 传播有延迟——深度断言必须轮询。
  3. 新队列无统计时单队列 API 不返回 `messages` 字段——读取必须容错。
  4. 显式 RoutingKey 是**双侧**迁移语义（订阅侧 attribute + 发布侧 `AddPublishedEvent(routingKey:)`），冻结不自动改名——harness 对 ContractsB 双侧接线。

## S4 — 证据、迁移与收口（本提交）

- Native/trim 门禁：`tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.AotHost`（正式 `AddRabbitMqEventBus` + 生成扩展；六场景：跨程序集 typed 载荷、同短名隔离、per-delivery scope、坏 envelope/未知 identity/不完整 identity 入 DLQ 零业务调用、显式 alias、重试后成功、预算耗尽入 DLQ、unroutable typed 失败）+ `.Tests` 双门（trim 与 `CrestCreatesPublishMode=aot` 各自 publish/link/run，linux-x64，断原生 sentinel + 各场景 PASS + publish 日志无 IL2026/IL3050）；证据 `tests/artifacts/aot-native-evidence/rabbitmq-static-bindings-{trim,native-aot}.json`。
- 支持声明边界：本次证据只覆盖该 fixture 组合（linux-x64、单进程、真实 broker 由 harness 提供、direct exchange + DLX 拓扑、guest 凭据）；不推导 EF DLQ、Outbox、Kafka、CAP、多租户/认证链路。
- CI 接线（保持暂停，仅预备）：`ci.yml` 增 `rabbitmq` service + 门禁 step；`full-validation.yml` native-aot-gates job 增 `rabbitmq` service + 门禁 step；**未启用任何 workflow**。
- 文档同步：旧 `2026-04-26-rabbitmq-event-bus-design.md` 增加失真提示；`memory.md`、AGENTS.md 平台状态、#123 evidence matrix 增行。
- 覆盖限制（不承诺）：competing consumers 跨进程、quorum DLX 无损、per-handler checkpoint、重放并发互斥、崩溃窗口去重、`MetadataBootstrapper`（ModuleInitializer 静态 bag）路径不扩展。

## R1 审查修复（2026-10-13，审查 head `1fb4468f`，6×P1 + 1×P2）

审查报告：`docs/review/2026-10-13-pr-145-rabbitmq-implementation-review.md`；探针：
`docs/review/2026-10-13-pr-145-rabbitmq-review-probes.md`。两个探针文件已**原样入库**（`ReviewRegressionTests.cs` /
`ReviewRetryRecoveryTests.cs`），先确认 7/7 失败基线，再逐项修复；R1-RMQ-05 因删除全局注册表 API，探针 #1 以等价的
per-Host 贡献形式适配（场景与断言不变，见下映射）。

| 发现 | 修复 | 仓库回归测试 |
| --- | --- | --- |
| R1-RMQ-01 恢复窗口发布销毁连接/consumer | pool 对非 open 既有连接**有界等待（20s，可取消）SDK 恢复**，禁止 Disposal/替换；专属 consumer channel 终态丢失（channel 关闭且连接已恢复，或连接 >30s 未恢复）抛错停宿主 | 探针 `Publish_during_confirmed_recovery_window_must_preserve_consumer`（先确认目标连接存在于 management 且 `IsOpen=false` 再发布）；`Connection_drop_between_targeted_close_and_recovery_window_preserves_the_consumer`（补强原断连测试：断言原连接被保留 `ReferenceEquals`、恢复窗口内单次发布后消费成功且无重复 consumer）；`Terminal_consumer_channel_loss_fails_the_host_instead_of_staying_falsely_ready`（真实 manifest：`ApplicationStopping` 触发） |
| R1-RMQ-02 重投确认失败无恢复动作 | `HandleBusinessFailureAsync` 的 `RabbitMqPublishException` 分支：有界退避（`max(1s, RetryDelaySeconds)`）后 `Nack(requeue:true)` 受控重入队；不递增重试计数、不消耗业务预算；取消/停机仍不结算 | 探针 `Retry_confirm_timeout_must_schedule_recovery_for_original_unacked_delivery`（mock 注入确认超时，断言 Nack(requeue:true)） |
| R1-RMQ-03 `payload:null` 永久 unacked | 信封必要字段预检（`Payload` 空/null → 确定性拒绝）先于编解码 | 探针 `Envelope_with_JSON_null_Payload_field_must_reach_DLQ`（真实 broker：DLQ=True、unacked=0） |
| R1-RMQ-04 required Handler 只校验元数据 | 冻结期经 `IServiceProviderIsService` 探测 required Handler 的 DI 注册（不构造实例）；delivery 期**整批预解析**全部 Handler 实例后再执行（invoker 改为接收实例），任一缺失零业务效果 | 探针 `Missing_Handler_DI_registration_must_fail_composition_before_broker_IO`（required + 移除注册 → 组合失败）、`Missing_later_Handler_must_not_execute_earlier_Handler`（非 required：执行前整批预解析 → CallLog 空 + DLQ=1）；单测 `Freeze_Rejects_required_handler_that_is_not_registered_in_di` / `Freeze_Allows_unregistered_non_required_handler_because_delivery_pre_resolution_guards_it` |
| R1-RMQ-05 全局 codec 注册表跨 Host 泄漏 | 删除 `EventJsonCodecRegistry`（归档 99_RecycleBin）；改为 per-Host `EventJsonCodecRegistration`（AddEventDescriptors → `AddCodecs(services)`）；同 Origin 幂等、多 Origin 同类目冲突诊断 | 探针 `Host_B_without_own_codec_contribution_must_not_use_Host_A_codec`（per-Host 形式适配）、`Codec_contribution_registration_order_and_idempotent_reactivation_are_host_local`、`Conflicting_codec_origins_for_one_payload_must_fail_freeze`（新增，覆盖双 Host/注册顺序/幂等） |
| R1-RMQ-06 RabbitMQ 破坏 LocalEvent 默认路径 | `AddEventKernel` 不再注册共享 `IEventValidator`；`RabbitMqEventBus` 以工厂构造**自有** `RegistryEventValidator` 实例；LocalEvent 可选依赖保持既有缺席语义 | 探针 `Adding_RabbitMQ_must_not_require_governance_for_plain_LocalEvent`（真实组合：AddCrestLocalEventDispatch + publisher-only RabbitMQ 共存发布不抛） |
| R1-RMQ-07 trim 门禁实际是 native | AotHost csproj 按 `CrestCreatesPublishMode` 求值 `PublishAot`（trim=false / aot=true，`-getProperty` 实证）；门禁按模式分别断言（trim 不得含 native link 文案） | 门禁自身（trim + native 双测试） |

修复后本地结果：单元 53/53（含审查单测探针 + 2 条新增）、集成 37/37（27 原有 + 6 审查探针 + 2 新增 + 补强改写 + 终态看护）、生成器全量 365/365、Event 41/41、LocalEvent 集成 47/47、CAP 单元 41/41、全量构建 0 错误；trim 门禁（无 native link）与 NativeAOT 门禁分别通过并重写证据。

说明：R1-RMQ-05 的探针 #1 原引用被删除的 `EventJsonCodecRegistry` API，属“修复确实移除该 API”的必然适配；场景、双 Host 隔离断言与失败期望保持不变，并补齐了审查要求的注册顺序/幂等/冲突三项覆盖。

## 验证记录（本地）

| 验证 | 结果 | 日志 |
| --- | --- | --- |
| RabbitMQ 单元（冻结矩阵/retry header/options/consumer 生命周期） | 50/50 | `dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.Tests` |
| 生成器（EventDescriptor + RabbitMqSubscription，含跨程序集编译） | 21/21；全量 365/365 | `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests` |
| JsonContracts（含 Integration discovery/writer 5 项） | 131/131 | `dotnet test tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests` |
| 真实 broker 集成矩阵 | 27/27 | `dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.Tests.Integration` |
| Native/trim 门禁 | trim 通过 / native 通过（各 exit 0 + sentinel + 六场景 PASS） | `tests/artifacts/aot-native-evidence/rabbitmq-static-bindings-{trim,native-aot}.json`（sha `d5b00543`） |
| AotHost JIT 冒烟（六场景，先于门禁） | 6/6 + sentinel | 会话命令输出（vhost 手工预置） |
| 全量构建 | 0 错误（先 `dotnet restore` 主 slnx 之外的 TypeForwardLegacyConsumer，见 #126 同款环境摩擦） | `/tmp/crest-127-regression.log` |
| Event 内核回归 | 41/41 | 同上 |
| LocalEvent 集成回归（#126） | 47/47 | 同上 |
| CAP 单元回归（#125） | 41/41 | 同上 |
| 依赖边界全量 | 173/178；5 项失败均为**环境态**（见下） | 同上 |

环境态失败甄别（非本 Issue 缺陷）：

- `EvidenceLedgerGuard_RequiredTuplesMustBeExecuted` / `C07_OrganizationSchema...` / `LegacyInfrastructureHierarchyCache...` / `O15_OrganizationProvider...`：主工作区 master（`6dd608a3`）同一套件**完全相同的 4 项失败**（工作树 DB/环境态，与 #126 记录一致）。
- `EvidenceLedgerSummary_Report`：按**工作树**累积 `tests/artifacts/control-plane-evidence/executed-*.jsonl`；新工作树累积不足 → pending（#126 日志同款）。
- `JsonContracts.Build.PackageTests.Build_InternalManifestRemainsAssemblyScoped`（28/29）：断言英文子串 "inaccessible"，本机 dotnet 输出为 zh-CN 本地化错误文本；主工作区 master 同样失败，属本地化断言环境问题。
