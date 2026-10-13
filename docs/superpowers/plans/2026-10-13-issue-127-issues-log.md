# #127 实施问题日志（S1–S4 实况与边界）

- 分支：`feat/issue-127-rabbitmq-static-bindings-20261013-145133`（基线 master `6dd608a3`）。
- 契约：Issue #127 详细设计评论（2026-10-13）+ Spec `docs/superpowers/specs/2026-10-13-issue-127-rabbitmq-static-bindings-design.md`。
- 计划：`docs/superpowers/plans/2026-10-13-issue-127-rabbitmq-static-bindings.md`（含 S1 实验全记录）。

## S1 — Spec 与 SDK/接线实验（本提交）

- 现状代码事实 F1–F10（Spec §1.1）全部文件级核实，其中关键结论：`GetSubscriptions` 反射桥在任何真实 Host 恒为空（生成注册表在业务程序集，反射查框架程序集）；`[CrestEvent] → IEventDescriptorProvider → IEventRegistry` 生成接线全仓不存在（`AddEventKernel` 零调用方、三个事件 registry validator 零 DI 注册、仓库内 `[CrestEvent]` 声明数 0）。
- 实验 A（confirm，`/tmp/crest-127-s1/confirm-exp`）：tracking ON 时 `await BasicPublishAsync` 参与确认等待；unroutable → `PublishReturnException`、channel 存活；tracking OFF 静默。锁定移除自制 confirm 账本。
- 实验 B（最小 NativeAOT + broker，`/tmp/crest-127-s1/aot-exp`）：干净 publish 0 条 IL2026/IL3050；原生产物对真实 broker 完成发布（await confirm）→ 消费 → typed JSON round-trip → Ack → sentinel、exit 0。判定锁定组合无 native blocker。
- 实验 C（bootstrap 接线）：DI 版为锁定主链；`DescriptorProviderRegistry`（ModuleInitializer 静态 bag）不扩展、不新增使用。
- 实验 D（跨程序集 JSON 顺序）：沿 #126 结论（BuildTasks 磁盘 roots → STJ SG）；codec 只由事件所属程序集生成；装配顺序靠“先注册、后冻结解析”。
- 已发现问题（记录，不在本 Issue 修复）：`CrestCreates.EventBus.Kafka` 走同一 `EventNamingConvention` 短名/下划线路径（F4 同类），本任务不动 Kafka，仅在 RabbitMQ 路径停止使用该路由约定。
