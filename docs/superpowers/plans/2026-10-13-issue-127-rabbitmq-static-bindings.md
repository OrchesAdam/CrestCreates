# #127 RabbitMQ 静态订阅与事件契约 — 实施计划

- 契约：Issue #127 详细设计评论（2026-10-13）+ Spec `docs/superpowers/specs/2026-10-13-issue-127-rabbitmq-static-bindings-design.md`。
- 分支：`feat/issue-127-rabbitmq-static-bindings-20261013-145133`（基线 `6dd608a3` = `1cad6e35` + #126 合并后文档）。
- 验证纪律：本地全绿才算证据；broker 不可用不得静默 Skip；不恢复 CI；不自动合并。

---

## S1 — 锁定契约和编译/SDK 实验（本提交）

### S1.1 调查清单（完成）

- [x] 读最新 master、`memory.md`、#126 JsonContracts 工具链（BuildTasks pre-CoreCompile → STJ SG、`LocalEventJsonCodecRegistry`、`AddLocalEvents` 模式）。
- [x] 现状代码事实 F1–F10（Spec §1.1）：反射桥、短名扫描、订阅字符串三义、发布/订阅身份不一致、自制 confirm 账本、channel 许可泄漏、三套装配、`IEventValidator` DI 缺口、事件内核接线缺口、手工 consumer 测试。
- [x] 迁移清单（Spec §8 表）与失真文档定位（`2026-04-26-rabbitmq-event-bus-design.md`）。

### S1.2 实验 A — confirm 行为（完成，实测）

工程 `/tmp/crest-127-s1/confirm-exp`（net10.0 + RabbitMQ.Client 7.2.0），对 `crestcreates-test-rabbitmq` 容器实测：

| 用例 | 实测结果 |
| --- | --- |
| tracking ON 正常发布 | `completedBeforeAwait=False`，await 完成于 broker ack 后 |
| tracking ON + mandatory 不可路由 | await 抛 `PublishReturnException`（"312 NO_ROUTE"），channel 保持 open，`BasicReturnAsync` 同步触发 |
| return 后复用 channel | 再次发布成功 |
| tracking OFF + 不可路由 | await 静默（返回不可观察） |

锁定：主链保持 tracking 双开；移除自制账本；unroutable 走 await 异常路径；cancel/timeout/nack/disconnect 独立映射。

### S1.3 实验 B — 最小 NativeAOT broker（完成，实测）

工程 `/tmp/crest-127-s1/aot-exp`（`PublishAot=true`、`JsonSerializerIsReflectionEnabledByDefault=false`、STJ Source-Gen envelope + payload）：

- 干净重建 publish：exit 0；“Generating native code”；**0 条 IL2026/IL3050**（`/tmp/crest-127-s1/aot-exp-publish.log`）。
- 原生产物（7.5MB）对真实 broker：声明 direct exchange/queue/binding → tracking 发布（await confirm）→ `AsyncEventingBasicConsumer` 消费 → typed JSON round-trip（decimal + `List<string>`）→ Ack → 输出 `CRESTCREATES_S1_AOT_BROKER_EXPERIMENT_OK`、exit 0（`/tmp/crest-127-s1/aot-exp-run.log`）。
- 判定：锁定组合原生路径**无 blocker**；S4 正式 fixture 风险主要在“正式主链装配 + harness 编排”，不在 SDK。

### S1.4 实验 C/D — 接线与生成顺序（完成，文件级证据）

- [x] `[CrestEvent] → IEventDescriptorProvider → IEventRegistry` 生成接线不存在（F8）；两条候选路径（DI / ModuleInitializer 静态 bag）均未接线。
- [x] 锁定 **DI 显式扩展**为主链；静态 bag 路径不扩展。
- [x] 跨程序集 JSON 生成顺序沿 #126 结论：BuildTasks 磁盘 roots → STJ SG（单项目内）；codec 只由事件所属程序集生成；Host 装配顺序靠“先注册扩展、后冻结解析”。

### S1.5 交付物（完成）

- [x] Spec（本文档目录下 `2026-10-13-issue-127-rabbitmq-static-bindings-design.md`）。
- [x] 本计划 + 问题日志（`2026-10-13-issue-127-issues-log.md`）。

---

## S2 — 静态生成、codec 和唯一装配

### S2.1 事件侧生成（CodeGenerator）

1. `EventDescriptorSourceGenerator`（`src/Tooling/CrestCreates.CodeGenerator/EventDescriptorGenerator/`）：
   - 完整 symbol `CrestCreates.Event.Abstractions.CrestEventAttribute`；
   - 每程序集 `GeneratedEventDescriptorProvider : IEventDescriptorProvider`（PayloadType = typeof(T)、State=Active、Id 缺省 `GenerateId`、ChangeKind 默认）；
   - `CrestCreates.Generated.<Asm>.EventRegistrations.AddEventDescriptors(services)`：TryAddEnumerable provider；声明 Integration 事件时调用 `EventJsonCodecContributions.Register()`（工具链存在）或报 `CCCEV001`；
   - 诊断 CCCEV001（缺工具链）、CCCEV002（非法声明）、CCCEV003（程序集内重复 identity）。
2. 生成器测试（`tests/Tooling/...`）：合法/非法签名、开泛型、不可访问、重复 identity、生成代码可编译、诊断 ID/位置。

### S2.2 JSON 工具链扩展（JsonContracts.BuildTasks）

3. `IntegrationEventJsonSurfaceDiscovery`：发现 `[CrestEvent(Scope=Integration)]`；复用 closure 校验（CJC015/016/017 对应新增 CJC018–020 或复用同码分段，S2 定）；产出 roots（独立生成 context，避免与 LocalEvent codec 重复生成同一 Type 的两份元数据 → 共享 context 或按需去重，S2 以“同一 Type 单份元数据”为准）与 `EventJsonCodecContributions`。
4. `Common.props` 增 `CrestCreatesEventJsonContracts` marker + `CompilerVisibleProperty`（对照 #126 `CrestCreatesLocalEventJsonContracts`）。
5. `EventJsonCodecRegistry`（`CrestCreates.Event.Abstractions`）：`Register(Type, Func<JsonTypeInfo?>)` TryAdd 幂等 + `Resolve`。
6. 工具链测试：round-trip 真值断言、闭包负例、多程序集（owner + handler-only）。

### S2.3 RabbitMQ 传输层骨架

7. 新契约（`src/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ/Registration/`）：
   - `RabbitMqEventBinding`、`RabbitMqSubscriptionBinding`（闭合 invoker）、`IRabbitMqRegistrationContributor`、`IRabbitMqBindingRegistry` + `RabbitMqBindingFreeze`（§3.2 校验集，`RabbitMqCompositionException`）、queue plan/alias 表。
8. `AddRabbitMqEventBus` 唯一入口重写（options 校验 + builder 累积 + PublisherOnly + required 清单 + 手工 typed binding）；`RabbitMqEventBusModule` 与旧 overload 转发。
9. `RabbitMqCompositionHostedService`（EventRegistry Built 检查 → 冻结 → readiness gate）+ consumer 第一步 `await readiness`。
10. 事件内核接线补齐：`AddEventKernel` 增补 validators / `IEventResolver` / TryAdd `IEventValidator`；`AddRabbitMqEventBus` 调用。
11. `RabbitMqSubscriptionSourceGenerator` 重写 + 新属性参数 `Version`/`RoutingKey`（`RabbitMqSubscribeAttribute` additive）+ CCCRMQ001–005。
12. 冻结负例测试（§9 冻结行全项）+ 正反注册顺序 + 幂等 + publisher-only + required 缺失诊断 + “失败先于 broker I/O”（mock 连接工厂断言零 I/O 或未创建连接）。

### S2.4 装配收口

13. `AddRabbitMqEventBus<TContext>`/context overload → typed codec 显式贡献适配器（转发唯一入口，逐 binding 校验，无法静态提供 → 明确报错）。
14. consumer 反射 fallback（`GetSubscriptions`/`GetEventType`）从代码中移除（S3 重写 consumer 时闭合）。

退出条件：编译期/装配期负例全过；多程序集真实 JSON round-trip 通过；构建 0 错误；旧注册路径全部转发新入口。

---

## S3 — 发布消费与结算闭环

1. Envelope v2（`EventId`/`EventVersion` 写出 + 解析 + alias 表）与 wire 索引；发布索引按具体 Type（泛型/非泛型同表）。
2. Publisher 重写：SDK tracking 确认、lease 精确释放、confirm timeout 组合 CT、异常分类映射；正常发布与重投共享确认/属性复制核心。
3. Consumer 重写：按队列计划消费、每 delivery async scope、跨 handler 顺序执行/失败即停、校验后执行（§5.6 拒绝零调用）。
4. 结算状态机（§6 表 + 细则）：retry 计数/延迟/属性保留/`x-retry-count` 宽容解析/耗尽 Nack(false)/Ack 失败单独诊断/终态不再抛。
5. 专属 consumer channel + settlement 串行门 + `ConsumerDispatchConcurrency` 显式配置；停机顺序实现。
6. SDK recovery 验证（管理 API 断开连接 → 消费恢复、无重复注册）。
7. 集成矩阵（真实 broker，§9 正常消费/拒绝/重试 DLQ/结算故障/生命周期行）+ 旧手工 consumer 测试迁移或归档 + 旧 body/headers fixture。

退出条件：§9 除 native 行外全绿；既有 RabbitMQ/Kafka/CAP 场景回归通过。

---

## S4 — 证据、迁移与收口

1. `tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.AotHost` + `.Tests`：harness 准备 broker（readiness + 有限超时 + 进程回收），原生 publish/link/run；跨程序集/同短名/坏消息/未知 identity/重试 DLQ 断言 + sentinel + 证据 JSON（`tests/artifacts/aot-native-evidence/rabbitmq-static-bindings.json`）。
2. trim 与 native 分别留证；不支持项如实记录（若发现 native 阻塞按 Spec §10 判据处理）。
3. 回归：#126 工具链/LocalEvent、CodeGenerator、JsonContracts、依赖边界、受影响集成；全量构建。
4. 归档 `99_RecycleBin/issue-127-rabbitmq-static-bindings/`：旧反射桥、失效 writer 形态、过时测试、旧 Spec 失真段；修正 `2026-04-26-rabbitmq-event-bus-design.md` 或标注归档。
5. 更新 `memory.md`、AGENTS.md 支持声明、#123 evidence matrix（新增门禁行）；issues log 汇总验证记录。
6. 迁移文档（Spec §8 表落地到 README/spec）+ PR（不自动合并）。

---

## 验证记录（本地）

| 验证 | 结果 | 日志 |
| --- | --- | --- |
| S1 实验 A（confirm 行为） | 通过（4 用例） | `/tmp/crest-127-s1/confirm-exp-run.log` |
| S1 实验 B（最小 NativeAOT + broker） | 通过（publish exit 0 / 0 trim 警告 / run exit 0 / sentinel） | `/tmp/crest-127-s1/aot-exp-publish.log`、`/tmp/crest-127-s1/aot-exp-run.log` |
| S2 RabbitMQ 单元（冻结矩阵等） | 50/50 | `dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.Tests` |
| S2 生成器（EventDescriptor/RabbitMqSubscription） | 21/21 | `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "EventDescriptorGenerator|RabbitMqGenerator"` |
| S2 JsonContracts（含 Integration discovery/writer） | 131/131 | `dotnet test tests/Tooling/CrestCreates.JsonContracts.BuildTasks.Tests` |
| S3 真实 broker 集成矩阵 | 27/27 | `dotnet test tests/Runtime/Eventing/CrestCreates.EventBus.RabbitMQ.Tests.Integration` |
| S4 trim 门禁（publish/link/run） | 通过（exit 0 + sentinel + 六场景 PASS） | `tests/artifacts/aot-native-evidence/rabbitmq-static-bindings-trim.json` |
| S4 NativeAOT 门禁（publish/link/run） | 通过（exit 0 + sentinel + 六场景 PASS；publish 无 IL2026/IL3050） | `tests/artifacts/aot-native-evidence/rabbitmq-static-bindings-native-aot.json` |
| S4 回归（全量构建/Event/LocalEvent/边界/CodeGen/JsonContracts 包/CAP 单元） | 见 issues log | `/tmp/crest-127-regression.log` |
