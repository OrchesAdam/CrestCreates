# #126 实施问题日志（S1–S4 实况与边界）

- 分支：`feat/issue-126-typed-dispatch-replay-20261013-085538`（基线 master `a546bbb1`）。
- 契约：Issue #126 详细设计评论（2026-10-13）+ Spec `docs/superpowers/specs/2026-10-13-issue-126-local-event-typed-dispatch-replay-design.md`。
- 计划：`docs/superpowers/plans/2026-10-13-issue-126-typed-dispatch-replay-implementation.md`（含 S1 实验全记录）。

## S1 — Spec 与 JSON 工具链实验（commit `884fd55c`）

- 实验工程 `/tmp/crest-126-s1-json-exp`，六项检查全部通过（详录于计划文档）：编译期磁盘源文件注入的 `[JsonSerializable]` roots 被官方 STJ 生成器消费；SG 输出属性对 STJ 生成器不可见（CS0534 硬证据）；`build_property` 需配套 `CompilerVisibleProperty`；反射 JSON 关闭时环境序列化确定性抛错。
- 发现（记录，不在本 Issue 修复）：`DynamicApiAotSourceGenerator` 读取的 `build_property.CrestCreatesDynamicApiAotGeneration` 全仓无 `CompilerVisibleProperty` 声明，禁用开关实为死代码（两处设置它的项目已用 `CrestCreatesCodeGeneration=false` 整体关闭生成器）。
- 锁定：codec roots 只走「CoreCompile 前磁盘源文件」路径（扩展现有 JsonContracts.BuildTasks 工具链）；SG 从不产出/引用 `[JsonSerializable]` 或上下文类名；工具链存在性经 `CrestCreatesLocalEventJsonContracts` 属性判定，SG 侧缺工具链 + Replayable 声明 = 编译错误诊断 CCCLE004。

## S2 — 唯一强类型 dispatcher 与生成注册（commit `ccb193ba`）

- 新增 Abstractions：`ILocalEventDispatchRegistry`、`LocalEventBinding`、`LocalEventInvoker`(+泛型/Inert)、`LocalEventDispatch`（静态分发核心：DI 枚举顺序、期望 handler 校验、逐 handler 取消检查）、`LocalEventDispatchRegistrationBuilder`、`ILocalEventRegistrationContributor`、`LocalEventAttribute`、`LocalEventLegacyIdentityAttribute`、`LocalEventIdentity`（本地 replay key = `{程序集简单名}:{metadata 全名}`；PayloadTypeFullName 规范化）、`LocalEventJsonCodecRegistry`（静态 codec 索引）、异常族与 `LocalEventRecordOutcome`。
- EventBus.Local：`LocalEventDispatchRegistry.Freeze`（contributor 按 Id 去重 → 同一 Type 合并 fan-out/冲突校验 → Replayable 必须解析出同类型 codec → replay key/身份/alias 唯一性校验）；重写 dispatcher/bus/manager + `AddCrestLocalEventDispatch`（同步 flavor）/`AddCrestLocalChannelEventDispatch`（channel flavor）；`ILocalEventDispatcher`/`ILocalEventBus` 默认注册为 TryAdd，行为注入点不变。
- 生成器：`LocalEventGenerator`（语义发现闭合 `ILocalEventHandler<TEvent>` + `[LocalEvent]` 声明；产出 contributor/typed invoker/预期 handler 清单/DI 绑定/每程序集公开 `LocalEventRegistrations.AddLocalEvents()`；诊断 CCCLE001–005）。
- 迁移：`AddCrestWeb`、两个模块、HumanTask 声明（`HumanTaskCompletedEvent` 增 `[LocalEvent]`，`AddHumanTaskRuntime` 调用其所属程序集生成扩展）、samples/test harness 六处手工接线全部改走 `AddCrestLocalEventDispatch`；`LocalEventBus`（空子类）与 `ILocalDeadLetterStore`（obsolete 死契约）归档 `99_RecycleBin/issue-126-local-event-typed-dispatch/`。
- 判定：`LocalEventDispatchRegistry` 冻结时校验，拒绝路径 = `LocalEventDispatchConfigurationException`（逐条违规行）；未声明事件 = `UnknownLocalEventException`（先于 handler/入队）；预期 handler 缺失 = `LocalEventRequiredHandlerMissingException`（先于任何 handler 执行）。

## S3 — typed codec、共用重放核心与状态/取消修复（commit `ccb193ba`）

- JsonContracts.BuildTasks.Core：`LocalEventJsonSurfaceDiscovery`（扫描 `[LocalEvent(Replayable=true)]` → 生成 `CrestCreates.Generated.LocalEventJsonSerializerContext` roots + `LocalEventJsonCodecContributions.Register()`；诊断 CJC015/016）；`Common.props` 增 `CrestCreatesLocalEventJsonContracts` + `CompilerVisibleProperty`。
- 共用件：`LocalEventDeadLetterRecorder`（同步/Channel 唯一失败记录组件；dispatch-only 不记录并报告；序列化失败保留原异常）；`LocalDeadLetterReplayExecutor`（load→预检→MarkRetrying→新 scope 解码分发→状态更新；仅 Pending 接受普通重试；预算取消息持久化 MaxRetries；预检失败不改状态；坏 JSON 计预算；执行前取消不动作、执行中共取消尽力恢复 Pending 并传播；状态写入用独立有界 token，失败必报告不冒充成功；重放直接走 dispatcher 不经 Publish）。
- `InMemoryDeadLetterStore` 容量检查改为仅限制新键；EF `DeadLetterEntity`/`DeadLetterDbContext`/`EfCoreDeadLetterStore` 增可空 `LocalReplayKey`（additive，列含义不变）。
- `DeadLetterMessage` 尾部追加可空 `LocalReplayKey`（旧构造调用零改动）。

## S4 — native 门禁、架构守卫与收口（本提交）

- 新 fixture `tests/Runtime/Eventing/CrestCreates.EventBus.Local.AotHost`（linux-x64，`JsonSerializerIsReflectionEnabledByDefault=false`，导入 JsonContracts 工具链）：五场景全过 —— ①泛型/非泛型同索引分发 + scoped 实例；②失败真实入 DLQ（断言 LocalReplayKey/版本/payload round-trip）→ 翻开关 → Manager 重放 → Retried；③另一条消息经**真实后台重试服务**（可控间隔 + 有界等待）成功；④未声明事件、未知 key/版本、坏 JSON 计预算、零 handler 拒绝、缺 codec 冻结期拒绝；⑤channel enqueue→consumer→失败入 DLQ→Manager 重放闭环。sentinel `CRESTCREATES_LOCALEVENT_TYPED_REPLAY_NATIVE_OK`。
- 门禁测试 `CrestCreates.EventBus.Local.AotHost.Tests`：真实 `dotnet publish -p:CrestCreatesPublishMode=aot -r linux-x64`（日志含 "Generating native code"）、执行原生产物、断言退出码 0 / sentinel / publish 日志无 IL2026、IL3050；证据 `tests/artifacts/aot-native-evidence/local-event-typed-replay.json`（sha/publishCommand/exit codes/log/output）。运行时约 65s（本机）。
- 架构守卫 `LocalEventTypedDispatchArchitectureTests`：正式路径禁反射桥/动态 JSON resolver/程序集加载；全 src 唯一索引/唯一重放核心/唯一 recorder 实现；入口文件必须引用统一索引与核心。
- CI 预备（保持暂停）：`ci.yml` 与 `full-validation.yml` 的 NativeAOT execute gates 段各增一条 LocalEvent 门禁 step。

## 验证记录（本地，全部通过）

| 验证 | 结果 | 日志 |
| --- | --- | --- |
| EventBus 单元（迁移 + 新增） | 37/37 | `/tmp/crest-126-unit1.log` |
| EventBus 集成（迁移 + 新增 + EF 模型） | 31/31 | 见会话命令输出 |
| CodeGenerator LocalEvent 生成器测试 | 10/10 | 同上 |
| JsonContracts discovery/writer 测试 | 6/6 | 同上 |
| Workflow 回归 | 100/100 | `/tmp/crest-126-wf.log` |
| HumanTask 回归 | 31/31 | `/tmp/crest-126-ht.log` |
| Agent ControlPlane 回归（含验收 harness 迁移） | 575/575 | `/tmp/crest-126-cp.log` |
| AssetManagement E2E | 19 失败/41（**与 master 完全相同**：master 同为 19/41） | `/tmp/crest-126-asset.log`、`/tmp/crest-master-asset.log` |
| Boundary 守卫（含新守卫） | 3/3（新守卫） | 同上 |
| NativeAOT publish/link/run 门禁 | 通过（sentinel + 证据 json） | `/tmp/crest-126-aot-gate.log` |
| Web IntegrationTests（迁移后接线） | 47 失败/54（**与 master 完全相同**：`/tmp/crest-master-webit.log` 亦为 47/54） | `/tmp/crest-126-webit2.log` |
| Boundary 全量 | 176/178；2 项失败均为**证据账本环境态**（见下） | 会话命令输出 |
| 全量构建 | 0 错误 | `/tmp/crest-126-s2-build4.log` |

环境态失败甄别（非本 Issue 缺陷，均已在 master 工作区复现或解释）：

- **Web IntegrationTests 47/54**：所有失败在宿主启动的 OpenIddict 种子阶段（`42P01: relation "OpenIddictScopes" does not exist`），master（a546bbb1，未含本分支改动）运行同一套件为**完全相同的 47/54**；本机 PG/迁移环境既存问题，与本 Issue 无关。
- **Boundary `EvidenceLedgerSummary_Report` / `EvidenceLedgerGuard_RequiredTuplesMustBeExecuted`**：`ControlPlaneReferenceDataEvidenceLedger` 按**工作树**累积 `tests/artifacts/control-plane-evidence/executed-*.jsonl`（由跨项目全量证据运行写入）。master 工作树累积 2101 行 → Summary 通过；本分支新工作树仅 60 行 → Summary 报 pending 568。Guard 在 master 上同样失败（1/14）。二者均为证据运行状态问题，非代码回归。
- Boundary 其余 176 项（含 3 项新 LocalEvent 守卫与全部依赖边界）通过。
- **AssetManagement E2E 19/41**：master（未含本分支改动）运行同一套件为**完全相同的 19/41**；预存环境态，与本 Issue 无关。

## 已知边界与不算数的东西

- Native 门禁只覆盖本 fixture 组合（linux-x64、内存 DLQ store、单进程 hosted 场景）；**不**推导 EF DLQ Provider、MediatorAdapter、RabbitMQ/Kafka、整套 Runtime 的 NativeAOT 状态。
- 跨程序集启用是显式契约：宿主必须调用每个贡献 handler/声明事件的程序集的生成扩展；未调用 = 未声明（publish 抛 Unknown），不存在扫描兜底。手工 DI 注册但未走任何贡献的 handler 不在分发范围内（`Declare(Type)` 的 inert 声明为零 handler no-op，文档已注明）。
- 重放并发互斥（原子 claim/CAS）、进程崩溃遗留 Retrying 恢复、per-handler checkpoint 均不承诺（与设计一致）。
- `MediatorLocalEventBus` 保持独立适配器，不接统一索引；samples 的 `ProcurementLocalEventBus`/`AssetLocalEventBus` 等自定义总线保持原样（非框架主链装配）。
- 工具链导入约束：声明 Replayable 的项目必须导入 `CrestCreates.JsonContracts.Build`（缺则 CCCLE004 编译错误）；handler-only 程序集无需导入。
- 环境摩擦（既有，与本次改动无关）：`CrestCreates.Agent.Memory.TypeForwardLegacyConsumer` 处于主 slnx 之外，新工作树需先 `dotnet restore` 该工程才能通过 Projection.Tests 的内嵌 MSBuild 目标。
