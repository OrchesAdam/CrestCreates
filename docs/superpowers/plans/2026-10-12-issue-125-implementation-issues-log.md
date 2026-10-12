# Issue #125 实施问题记录（S1–S4，统一审查）

日期：2026-10-12
用途：记录 #125 实施过程中发现的问题、偏离设计与遗留项，供功能完成后统一审查。
设计依据：`docs/superpowers/specs/2026-10-12-issue-125-cap-entry-consolidation-design.md`（rev.4，审查通过）
分支：`feat/issue-125-cap-entry-consolidation-20261012-133633`（PR #140 承载）
交付纪律：GitHub CI 全局暂停（开发期本地优先；本地 podman Testcontainers 证据）；不从本记录推导合并许可。

## 记录

| # | 类别 | 说明 | 影响 | 处置/建议 |
| --- | --- | --- | --- | --- |
| I-101 | 缺陷（生成器，实施中暴露） | `CompensationExecutorRegistry` 生成代码对**实例属性** `Name` 生成 `name switch { Foo.Name => ... }` 常量模式（CS0120，无法编译）；此前从未被真实消费（既有测试的 executor 无 Name 属性） | 带 `Name` 属性的执行器应用无法编译 | 已修 `CompensationExecutorCodeWriter`：`GetExecutor` 改为 `GetAll().FirstOrDefault(e => e.Name == name)` 运行时匹配，并补 `using System.Linq;`/`using Microsoft.Extensions.DependencyInjection;`（后者为生成产物原有缺引）；测试项目消费真实生成产物回归 |
| I-102 | 遗留（仓储契约，实施中暴露） | `CrestCreates.Data.Abstractions.IRepository<TEntity, TKey>` 全仓无 EF 实现（EF 侧仅 Domain 契约体系）；原 `PersistentTransactionLogger` 从未有可运行的装配路径 | 持久化链此前不可运行（#125 清单 G-04 的实证） | 集成包提供 `EfCoreCapRepository<TEntity, TKey>`（仅覆盖 Data.Abstractions 契约、不自行 SaveChanges、跟随受管链路由）；真实链由 PG 集成用例验证 |
| I-103 | 环境（启动竞态） | CAP 的 `IStorageInitializer` 在 `BackgroundService` 内异步执行：`host.StartAsync` 返回时 `cap."published"` 可能尚不存在 | 集成用例首跑 42P01 | 集成夹具在启动后等待 `cap."published"` 可见（60s 有界）；生产语义不受影响 |
| I-104 | 配置（组合声明） | `StorageProvider` 默认值仍为 `SqlServer`（调用方显式配置）；原子发布集成仅支持 PostgreSql 单库 | 未配置 StorageProvider=PostgreSql 时窗口内发布确定性拒绝 `StorageIdentityMismatch`（非静默） | 保持默认值（不扩大行为变更面）；诊断消息指明组合要求；文档/AGENTS.md 已注明 |
| I-105 | 偏离设计（供审查裁定） | ① 集成扩展命名 `AddCrestCapEfCoreIntegration()`（设计文写作 `AddCrestCapPersistence<TContext>()`；实现按「显式 ModelBuilder 配置 `ApplyCrestCapPersistence()` + 仓储/提供者注册」拆分，语义一致）；② 矩阵 4「提交确认 + 清理失败」与 11「生命周期调用计数 = 0」未做真实注入用例——内核侧语义由 #137 探针覆盖，CAP 侧投递完全由存储扫描驱动（与内核提交后代码路径无关），守卫子类以构造性保护（Commit/Rollback 覆写抛确定性异常，误触即测试失败）；如需真实注入用例请审查裁定 | 覆盖方式与矩阵文字不完全一一对应 | 记入本表并随 PR 评论说明；审查如要求，可在 S 后续轮次补注入型用例 |
| I-106 | 环境（本地验证路径） | CI 暂停期间：集成套件本地经 `DOCKER_HOST=unix:///run/user/1000/podman/podman.sock` 运行（docker=podman 别名）；`ci.yml`/`full-validation.yml` 已预置 «Runtime / CAP atomic publish» 步骤（恢复 CI 后生效） | 无 | 已记录至项目记忆；恢复 CI 指令待用户指示 |
| I-108 | 缺陷（传输配置，实施中暴露，预存问题） | CAP `UseRabbitMQ(string)` 的原生语义是 **HostName**（v10.0.1 源证：`CAP.Options.Extensions.cs` 单参重载 `o.HostName = hostName`），既有扩展把它当「连接串」直接传入 → 非纯主机名的配置（含 `amqp://` URI）在投递期才暴露（行 Failed、重试耗尽；broker 侧零连接） | 真实 PG+RabbitMQ 集成首跑暴露；此前从未有真实 broker 证据 | 已修：扩展支持 `amqp(s)://user:pass@host:port[/vhost]` URI（解析 HostName/Port/凭据/VHost）并保留纯主机名原生语义；`TransportConnectionString` 文档注明；Kafka 语义（bootstrap servers）一并注明 |
| I-107 | 测试颗粒度（声明纪律） | 补偿链顺序重试已覆盖；**并发补偿与进程崩溃（`Executing` 残留）不承诺**（设计 §3.3 声明）；测试不以顺序 mock 宣称 durable compensation 闭环 | 无（如实声明） | 保持声明；`Executing` 残留列为已知限制 |

（后续实施发现问题将追加到本表。）
