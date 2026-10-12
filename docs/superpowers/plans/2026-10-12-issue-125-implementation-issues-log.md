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
| I-105 | 偏离设计（已由审查修复轮关闭） | ① 集成扩展命名 `AddCrestCapEfCoreIntegration()`（设计文写作 `AddCrestCapPersistence<TContext>()`；实现按「显式 ModelBuilder 配置 `ApplyCrestCapPersistence()` + 仓储/提供者注册」拆分，语义一致）；② 矩阵 4「提交确认 + 清理失败」与 11「生命周期调用计数 = 0」原为构造性覆盖 | 审查要求补注入证据 | **已修复**：审查修复轮以真实注入用例覆盖（ControllableEfUnitOfWork 提交后清理故障、守护事务测试；见 I-113） |
| I-106 | 环境（本地验证路径） | CI 暂停期间：集成套件本地经 `DOCKER_HOST=unix:///run/user/1000/podman/podman.sock` 运行（docker=podman 别名）；`ci.yml`/`full-validation.yml` 已预置 «Runtime / CAP atomic publish» 步骤（恢复 CI 后生效） | 无 | 已记录至项目记忆；恢复 CI 指令待用户指示 |
| I-108 | 缺陷（传输配置，实施中暴露，预存问题） | CAP `UseRabbitMQ(string)` 的原生语义是 **HostName**（v10.0.1 源证：`CAP.Options.Extensions.cs` 单参重载 `o.HostName = hostName`），既有扩展把它当「连接串」直接传入 → 非纯主机名的配置（含 `amqp://` URI）在投递期才暴露（行 Failed、重试耗尽；broker 侧零连接） | 真实 PG+RabbitMQ 集成首跑暴露；此前从未有真实 broker 证据 | 已修：扩展支持 `amqp(s)://user:pass@host:port[/vhost]` URI（解析 HostName/Port/凭据/VHost）并保留纯主机名原生语义；`TransportConnectionString` 文档注明；Kafka 语义（bootstrap servers）一并注明 |
| I-107 | 测试颗粒度（声明纪律） | 补偿链顺序重试已覆盖；**并发补偿与进程崩溃（`Executing` 残留）不承诺**（设计 §3.3 声明）；测试不以顺序 mock 宣称 durable compensation 闭环 | 无（如实声明） | 保持声明；`Executing` 残留列为已知限制 |


## 实现审查修复轮（2026-10-12，R1-CAP-01…05）

对照 `docs/review/2026-10-12-pr-140-cap-implementation-review.md`：

| 审查项 | 修复 |
| --- | --- |
| R1-CAP-01 [P1] 后台补偿执行后状态未落库 | `CompensationRetryBackgroundService` 每个处理周期经**唯一内核**建立持久化边界（scoped `IUnitOfWorkManager.ExecuteAsync`，flush 统一落库）；装配自检新增 `IUnitOfWorkManager` 可解析要求；真实 PG 持久化链用例（注册跨 scope 可见、失败 Retrying 持久化、后台完成 Completed、上限 Failed+ErrorMessage、历史缺执行器 Failed）经专用宿主（补偿周期 1s）驱动**真实后台服务路径** |
| R1-CAP-02 [P1] 仓储跟踪语义使日志链不可用 | `EfCoreCapRepository` 重写跟踪契约：查询本地优先（同键复用同一实例、含未落库 Added 行）+ 数据库查询保持跟踪；`AddAsync` 同键去重返回已跟踪实例；`UpdateAsync` 已跟踪 → no-op（快照自动标记，绝不把 Added 误改 Modified）、detached 同键 → 合并到已跟踪实例、否则附加 Modified；PG 用例覆盖首次写入、同一 UoW 连续更新、提交后新 UoW 更新 |
| R1-CAP-03 [P1] 存储身份读 Crest 输入可被最终配置绕过 | Provider 改读**最终有效** `DotNetCore.CAP.PostgreSqlOptions`（ConnectionString 或 DataSource；空则拒绝），与借用活连接的 host/port/database 规范字段比较；`PostConfigure<PostgreSqlOptions>` 覆盖负例集成用例（A/B 均零新行、publisher 上下文无残留） |
| R1-CAP-04 [P2] RabbitMQ URI 遗漏 TLS 与 vhost 解码 | 解析器补：amqps → 默认 5671 + `ConnectionFactoryOptions` 启用 TLS（ServerName=host）；amqp 无端口 → 5672；vhost 按 AMQP URI 语义取第一个 `/` 之后内容并 `UnescapeDataString`（`%2F` → `/`）；单元测试 6 条（amqp/amqps/显式端口/编码 vhost/纯主机名原生语义） |
| R1-CAP-05 [P2] S3 矩阵未补齐 | 新增集成用例：Unknown 提交故障（停库 → Unknown + 无行无投递）、确认提交 + 清理失败（真实 PG 提交 + 注入清理异常 → Failed+Committed、行照常投递）、未完成退出放弃（零行零投递）、窗口外状态恢复（同一 publisher 绑定无残留 + 立即投递）、拒绝矩阵全量（未 Start/非事务/终态）、最低配置（FailedRetryCount=1）首次投递、broker 离线→扫描可检查失败→恢复后投递；矩阵 11 原生生命周期计数以守护事务测试（IVT）覆盖 |


## 实现复审修复轮（第二轮，2026-10-12，R2-CAP-01…03）

对照 `docs/review/2026-10-12-pr-140-cap-implementation-review-round2.md`：

| 审查项 | 修复 |
| --- | --- |
| R2-CAP-01 [P1] DataSource 被 ConnectionString 遮蔽 | Provider 改为**镜像 CAP 实际消费优先级**（v10.0.1 `PostgreSqlOptions.CreateConnection`：DataSource 存在时一律使用 DataSource）：有效目标 = `DataSource.ConnectionString ?? ConnectionString`，再与借用活连接规范字段比较。**两个正式集成负例**：`PostConfigure<PostgreSqlOptions>` 覆盖 ConnectionString / DataSource 为 B 库 → 发布前 `StorageIdentityMismatch`、A/B 双库零新增行、publisher 上下文无残留。（修正上轮"已有最终覆盖负例"的声称——当时仅 ConnectionString 路径有未入正式测试的规划，本轮补齐为真实用例） |
| R2-CAP-02 [P1] 合并跟踪结果遗漏谓词 | `MergeLocalFirst` 对最终选中身份实例**复核谓词**；`FirstOrDefaultAsync` 对 SQL 旧快照返回的跟踪实例复核；`AnyAsync`/`CountAsync`/`LongCountAsync` 改为「本地优先 + 数据库候选 id 按跟踪实例当前状态复核」的一致语义。回归：同 UoW 连续两次 `CompensateAsync`（真实 PG）执行器调用恰 1 次、状态 Completed、跨 scope Pending 集合为空 |
| R2-CAP-03 [P2] Unknown 未进入提交派发阶段 | flush 阶段用例更名 `Flush_stage_failure_after_publish_records_unknown_without_rows`（准确表述）；新增提交阶段注入：`Commit_dispatch_failure_records_unknown_without_commit_fact`（派发失败、计数=1、无提交事实、零行零投递）与 `Confirmation_lost_after_commit_recovers_by_database_fact`（真实提交+确认丢失 → Unknown 保持、行与业务行一致、按数据库事实扫描恢复投递）；broker 用例补「提交期离线→恢复」变体；拒绝矩阵补 `ResourceMismatch`（窗口内切租户，真实宿主）与 `NoTransactionAssociation`（无受管资源曝光的 owner，集成级）；生命周期计数以 `IRelationalTransactionFactory` 装饰实现（正式绑定路径：Commit=1、Rollback=0、Dispose=1；注：EF Core 10 拦截器已无 Dispose 事件，故用工厂装饰，见实现注释）|
| 说明（Released 拒绝） | 「已释放 owner 帧可见」在公开 API 下不可构造（释放即恢复环境）；该拒绝分支由内核 `EvaluateParticipation` 覆盖并随三值判定单测保护，不在集成层强行构造 |


## 实现复审修复轮（第三轮，2026-10-12，R3-CAP-01…03）

对照 `docs/review/2026-10-12-pr-140-cap-implementation-review-round3.md`：

| 审查项 | 修复 |
| --- | --- |
| R3-CAP-01 [P2] 待删除实体被数据库结果带回 | 仓储读取统一为**最终视图** `QueryFinalViewAsync`：本地跟踪视图（Added/Modified/Unchanged）优先，**Deleted 为显式排除覆盖**（不再与「未跟踪」混同）；数据库候选经 identity resolution 后复核删除状态与谓词，旧快照不得复活待删除行。回归（真实 PG）：同一 UoW 内查询 + `DeleteAsync(row)` 后 `CompensateAsync` → 执行器调用 **0 次**；提交后跨 scope 行确实不存在 |
| R3-CAP-02 [P2] Count 漏掉本地修改后新匹配的既存实体 | Count/LongCount/Any/Find/First/GetAll/GetByIds 全部改为**同一最终视图**（合并本地匹配 + 数据库候选，按主键去重，应用本地不匹配/Deleted 覆盖）——不再按单个查询方法维护不同算法。回归（真实 PG）：Pending→Completed 本地变更后 Completed 视图 Find=1/Count=1、Pending 视图 0/0；反向改为 Retrying 后 Completed 归零、Retrying 为一，Find 与 Count 始终一致 |
| R3-CAP-03 [P2] 存储负例准备条件错误（无区分能力） | 两负例准备修正：初始 UoW / Crest 输入 / CAP ConnectionString **全部指向 A**，先独立初始化 B 的 CAP 表，仅在**最终** `PostConfigure<PostgreSqlOptions>` 改变目标（DataSource 用例保持 ConnectionString=A、DataSource=B）；新增 `capTableWaitConnectionString` 夹具参数按最终目标等待建表。**区分验证（本地）**：临时还原 R2 旧优先级 `ConnectionString ?? DataSource` → `Final_datasource_override_*` **失败（1/2）**；恢复修复后 2/2 通过。用例注释记录区分矩阵（DataSource 用例保护 R2 修复；ConnectionString 用例保护 R1 修复） |

补充记录（诚实边界）：

- 上轮完整套件复跑中出现 broker 恢复用例超时（首跑 33/34，独立复跑通过）。本轮将其与「提交期离线」变体一并重构为**独立数据库 + 专用宿主（重试预算 10）**，消除共享宿主扫描与重试预算的交叉干扰；不得以「某次完整套件全绿」作为时序稳定性的隐含声明，时序波动记录保留在本表。
- 生命周期计数为「按事务实例每操作去重后的窗口计数」（含后台首周期预热重置），是既有证据的准确边界，不声称逐次原生调用审计。

（后续实施发现问题将追加到本表。）
