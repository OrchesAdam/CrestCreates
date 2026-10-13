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
