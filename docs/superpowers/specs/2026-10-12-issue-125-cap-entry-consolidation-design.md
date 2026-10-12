# Issue #125 CAP 入口收口与事务承诺核实 — 设计与现状清单

日期：2026-10-12
基线：master `1dc4a9cc`（PR #139 / #137 合并后）
输入：
- Issue #125 正文（含验收项与「纠正审查」）
- `docs/review/2026-10-10-framework-overall-architecture-review-follow-up.md` E7 修正（不能整体判玩具；核心问题是入口语义分裂与假补偿）
- #137 交接文档 `docs/superpowers/specs/2026-10-10-issue-137-cap-handoff.md`（CAP 参与契约、拒绝项、原子发布边界）
- 代码事实（本文所有引用以当前基线为准，file:line 供逐条复核）

---

## 0. 结论摘要

1. **入口分裂确认**：同一包内两条注册路径——`[CrestModule]` 模块入口注册**内存实现**，扩展入口注册**持久化实现并调用 `AddCap`**。模块会被 BuildTasks 自动发现，在任何引用该包的应用里**无需显式调用扩展**即完成注册；扩展随后被调用（或顺序相反）时，`ITransactionLogger` / `ITransactionCompensator` 的解析结果取决于**注册顺序**，存在静默回落内存实现的风险（G-01）。
2. **唯一正式入口方向**：扩展 `AddCrestCapDistributedTransaction` 收口为唯一正式入口；**模块入口归档**（99_RecycleBin）。依据：模块的 `OnConfigureServices` 拿不到 `IConfiguration`，而 `AddCap` 的 storage/transport 装配需要配置——模块入口在结构上只能产出「未装配 AddCap 的半成品注册」，无法满足「正式配置缺 storage/transport 时明确失败」（G-02）。
3. **内存件与死代码归档**：`TransactionLogger`（内存字典 + Console）、`DefaultTransactionCompensator`（假补偿：不调用执行器即标记完成、`CanCompensateAsync` 恒 true）、`UnitOfWorkIntegrationExtensions`（#137 已判死扩展）随本项一并归档；参与者列表引擎（`IDistributedTransaction`/`DistributedTransaction`/`DistributedTransactionManager`/`ITransactionParticipant`）建议归档（Q2 待审查裁定）。
4. **真实语义补齐**：持久化链（仓库 + 实体映射 + executor registry）具备可运行的装配与启动校验；补偿失败如实记录（Failed/Retrying），不得只把状态改成 completed；原子发布消费 #137 的受管窗口/借用契约（同库同事务或确定性失败），适配器放**集成层**（Runtime 不引具体 ORM Provider）。
5. **证据**：PG（CAP 存储/消息表 + 业务库）+ RabbitMQ Testcontainers 集成门禁，覆盖发布、失败重试、补偿 executor 调用、UoW 回滚时消息不发布、提交确认失败不宣称成功；支持声明逐项（JIT/trim/AOT）如实标注，无证据不声称。
6. **不做**：不另造通用事务引擎；不把「依次 commit」或内存参与者列表推导为分布式原子；不整体重命名/搬走 CAP Provider（E7 约束）。

---

## 1. 现状清单（证据）

### 1.1 入口 A — 模块（自动发现）

`src/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction.CAP/Modules/DistributedTransactionCapModule.cs`

- `[CrestModule] DistributedTransactionCapModule : ModuleBase`，`OnConfigureServices` 注册：
  - `IDistributedTransactionManager → DistributedTransactionManager`（内存参与者引擎）
  - `ITransactionLogger → TransactionLogger`（**内存**）
  - `ITransactionCompensator → DefaultTransactionCompensator`（**假补偿**）
  - `ICapTopicNameProvider → DefaultCapTopicNameProvider`
  - `IDistributedEventPublisher → CapDistributedEventPublisher`
  - `IEventBus → CapEventBus`
- 不调用 `AddCap`，不读取任何配置。`CapEventBus` 依赖 `ICapPublisher`（由 `AddCap` 提供）与 `IEventValidator`（Eventing 提供）→ 模块单独使用时装配不完整。
- `ModuleBase.OnConfigureServices(IServiceCollection)` 无配置上下文；`IModule` 其余钩子（`OnPreInitializeAsync` 等 / `OnApplicationInitializationAsync(IHost)`）均在**服务注册之后**运行，无法在注册期完成配置绑定的 CAP 装配。

### 1.2 入口 B — 扩展（手动调用）

`src/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction.CAP/Extensions/DistributedTransactionCapServiceCollectionExtensions.cs`

- `AddCrestCapDistributedTransaction(IConfiguration, Action<DistributedTransactionCapOptions>?)`：
  - 绑定 `DistributedTransaction: CAP` 节；注册**持久化** `ITransactionLogger → PersistentTransactionLogger`、`ITransactionCompensator → PersistentTransactionCompensator`（:37-38），其余与模块相同的 topic/publisher/event bus；
  - 可选注册 `CompensationRetryBackgroundService`（`EnableCompensationBackgroundWorker` 默认 true，:41-45）；
  - 调用 `services.AddCap(...)`（:48）：设置 group/retry 参数与 `ConfigureStorage`/`ConfigureTransport`；
  - `ConfigureStorage`：`StorageConnectionString` 为空即抛 `ArgumentException.ThrowIfNullOrWhiteSpace`；`StorageProvider` 不在 {SqlServer, PostgreSql} 抛 `NotSupportedException`（:64-88）——**fail closed 已有**；
  - `ConfigureTransport`：`TransportConnectionString` 为空即抛；不在 {RabbitMQ, Kafka} 抛（:90-105）。

### 1.3 配置链

`Options/DistributedTransactionCapOptions.cs`（节名 `DistributedTransaction:CAP`）

| 配置 | 默认值 | 现状评估 |
| --- | --- | --- |
| `StorageProvider` | `SqlServer` | 显式枚举；非法值注册期抛 |
| `StorageConnectionString` | 空串 | 注册期抛（fail closed） |
| `TransportProvider` | `RabbitMQ` | 显式枚举 |
| `TransportConnectionString` | `"localhost"` | **非空默认值**：未配置时静默指向本机 RabbitMQ，可能延迟到运行期才暴露（G-03） |
| `DefaultGroup` | `"crestcreates"` | 无校验需求 |
| `FailedRetryCount/Interval` | 5 / 60s | 透传给 CAP |
| `CompensationMaxRetries/RetryInterval` | 3 / 30s | 补偿重试参数 |
| `EnableCompensationBackgroundWorker` | true | 后台重试服务开关 |

### 1.4 持久化链

- `PersistentTransactionLogger`（:14-90）：`IRepository<TransactionLog, Guid>` 读写 `TransactionLog`（Id/Status/Message/ErrorDetails/ParticipantCount/UpdatedAt）。
- `PersistentTransactionCompensator`（:13-190）：`IRepository<TransactionCompensation, Guid>` + `ICompensationExecutorRegistry` + options；注册补偿、查询 Pending/Retrying、按 CreatedAt 倒序执行、指数退避（上限 300s）、执行器缺失或抛异常 → `Retrying`（达 MaxRetries → `Failed`）。
- **实体映射缺口**：`TransactionLog` / `TransactionCompensation`（`Models/`）在框架其它位置**没有任何 EF 映射或 DbContext 注册**（全仓 grep 仅包内命中）；持久化链要求应用自行注册 `IRepository<>` 与建表，当前**无装配文档、无验证**（G-04）。
- **执行器注册表缺口**：`ICompensationExecutorRegistry` 由 Source Generator（`CompensationExecutorCodeWriter.cs:33,100`）生成 `CompensationExecutorRegistry` + `AddCompensationExecutors()` 扩展，须应用**显式调用**；全仓无生产消费者（仅生成器测试与 2026-04-26 设计文档引用）。扩展入口未校验其存在 → 解析失败推迟到运行期（G-05）。

### 1.5 发布链

- `CapDistributedEventPublisher`：包装 `ICapPublisher.PublishAsync(topic, message, headers, ct)` / `PublishDelayAsync`；**不携带事务**（未使用 `ICapPublisher.Transaction`）。
- `CapEventBus : DistributedEventBusBase`：`PublishAsync` → topic 映射（`DefaultCapTopicNameProvider`，`命名空间.类型名`）→ publisher；`Subscribe/Unsubscribe` 抛 `NotSupportedException`（CAP 以 `[CapSubscribe]` 声明订阅）。
- **原子性现状**：#137 交接文档 §4 明确「消息记录必须与业务数据同一资源/事务、在本 UoW commit 之前落库」；当前发布路径**没有**接入受管事务（G-06）。

### 1.6 补偿执行链

- 真实链（应用配置持久化路径时）：UoW/业务失败 → `RegisterCompensationAsync`（持久化 Pending）→ `CompensateAsync(transactionId)` 或后台 `ProcessRetryingCompensationsAsync` → `ExecuteCompensationAsync` → `ICompensationExecutor.ExecuteAsync(data)` → 成功 Completed / 失败 Retrying（退避）或 Failed。
- 假链（模块/内存路径）：`DefaultTransactionCompensator.CompensateAsync` 对 Pending 项**直接** `MarkCompensationCompletedAsync`（不调用任何执行器）；`CanCompensateAsync` 恒 `true`；`ProcessRetryingCompensationsAsync` 同样直接标完成。
- 参与者引擎（两条入口都注册的 `DistributedTransactionManager`）：`CreateTransactionAsync` → `DistributedTransaction.BeginAsync`（空操作）→ `CommitAsync` 对**内存参与者列表**做 Prepare/Commit（任一 Prepare false 即整体回滚）→ `Status` 记录在内存。不接触 CAP、不接触数据库事务（G-07）。
- `UnitOfWorkIntegrationExtensions.RegisterWithDistributedTransaction`（`CrestCreates.DistributedTransaction/Extensions/`）：零消费者（#137 交接表已判死）；把 `Domain.UnitOfWork.IUnitOfWork` 包成参与者，Prepare 调 `SaveChangesAsync`、Commit 调 `CommitTransactionAsync`（G-08）。

### 1.7 消费与测试现状

- **生产消费者：零**。samples 不引用 CAP 包；`AddCrestCapDistributedTransaction` / `DistributedTransactionCapModule` 在 src/samples 均无调用方（仅定义处命中）。
- 测试：`tests/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction.Tests/DistributedTransactionManagerTests.cs` 仅覆盖**内存** manager（手工 new `TransactionLogger`/`DefaultTransactionCompensator`，Moq），无持久化/补偿真实执行/CAP 发布测试。
- CI：`.github/workflows/ci.yml` "Runtime / Audit & DistributedTransaction" 步骤执行上述测试项目；CAP 集成无任何 CI 门禁。
- 质量债务：9 处 CS1998（CAP 包内）；`TransactionLogger` 等含「这里可以添加实际的…」废话注释。

---

## 2. 缺口清单

| # | 类别 | 缺口 | 证据 | 影响 |
| --- | --- | --- | --- | --- |
| G-01 | 缺陷 | 双入口注册分裂：模块注册内存实现且自动发现；解析结果依赖注册顺序 | `DistributedTransactionCapModule.cs`（内存注册）vs 扩展（持久化 + AddCap） | 静默回落内存、假补偿上线风险；违反「唯一主链」 |
| G-02 | 缺陷 | 模块无法配置绑定装配（无 IConfiguration 通道），单独使用即半装配 | `ModuleBase.OnConfigureServices(IServiceCollection)` 签名；模块不调 `AddCap` | 「正式配置缺失明确失败」无法在模块路径成立 |
| G-03 | 缺陷 | `TransportConnectionString` 默认 `"localhost"` 使缺失配置静默指向本机 | `DistributedTransactionCapOptions.cs` | 延迟暴露、可能误连本机 broker |
| G-04 | 遗留 | 持久化实体无框架侧映射/装配文档；`IRepository<TransactionLog/TransactionCompensation>` 来源未定义 | 全仓 grep 仅包内；无 samples 装配 | 持久化链实际不可运行（未验证） |
| G-05 | 缺陷 | 扩展不校验 `ICompensationExecutorRegistry` 是否存在 | 生成器要求显式 `AddCompensationExecutors()`；扩展未引用 | 补偿执行在运行期才失败（「无 executor found」被记录为 Failed 但启动期无诊断） |
| G-06 | 缺陷 | 发布链不接入受管事务（无 `ICapPublisher.Transaction` 使用） | `CapDistributedEventPublisher` | 消息与业务落库不同事务时，「原子发布」声明不成立 |
| G-07 | 缺陷 | 内存参与者引擎承载伪 2PC 语义（Prepare/Commit 内存列表，`BeginAsync` 空操作） | `DistributedTransaction.cs` / `DistributedTransactionManager.cs` | 与「不从回调或依次 commit 推导原子发布」直接冲突；误导上层 |
| G-08 | 遗留 | `RegisterWithDistributedTransaction` 死扩展 + `UnitOfWorkTransactionParticipant` | #137 交接记录；零消费者 | 双轨风险（误用为参与路径） |
| G-09 | 缺陷 | 假补偿：`DefaultTransactionCompensator` 不调用执行器即标完成、`CanCompensateAsync` 恒 true | `DefaultTransactionCompensator.cs:17-38` | 「失败不能只把状态改成 completed」的反面样本 |
| G-10 | 测试 | 无持久化/补偿执行/发布/故障恢复测试；CI 仅跑内存 manager 测试 | 测试项目清单；ci.yml:224-229 | 绿灯信号不证明任何 CAP 语义 |
| G-11 | 声明 | CAP 集成无 JIT/trim/AOT 或「未验证」声明 | AGENTS.md 分层规则 | 支持表缺项 |
| G-12 | 文档 | 「事务 API 实际保证」未核实：无 exactly-once/2PC 承诺说明；`CanCompensateAsync=true` 语义被文档放大 | 任务书第 4 条；`DefaultTransactionCompensator` | 文档与实现不符 |
| G-13 | 质量 | 9 处 CS1998、废话注释 | 架构审查 §4 | 维护噪声 |
| G-14 | 边界 | 原子发布适配若直接落 Runtime 会导致 Runtime 引具体 ORM Provider（边界测试当前只禁 FreeSql/SqlSugar，原则禁「如」同类） | `DependencyBoundaryTests.cs:48-53`；#137 交接 §4.4 | 落位错误 = 边界违规 |

---

## 3. 设计

### 3.1 唯一正式入口与模块处置（G-01/G-02）

- **唯一正式入口**：`AddCrestCapDistributedTransaction(IConfiguration, configure?)`（扩展）。所有真实注册集中于此。
- **模块入口归档**：`DistributedTransactionCapModule` 移除到 `99_RecycleBin/issue-125-cap-module/`。理由（须在 PR 说明中如实呈现）：
  1. 注册期无配置通道 → 结构上无法完成 CAP 装配；
  2. 自动发现使其在任何引用应用里「无调用即注册」，与显式配置入口形成顺序敏感的双轨；
  3. 归档是「隔离否则归档」原则下可验证的唯一选择（保留但其改为空注册会制造「调用过模块即放心」的错觉，反而更糟）。
- **顺序不变量**：收口后 `ITransactionLogger`/`ITransactionCompensator` 只有持久化注册一处来源；验收项「模块/扩展组合及注册顺序不会切回内存实现」以「无内存实现可回落」直接成立，并以启动校验（§3.2）兜底。

### 3.2 配置与启动校验（G-03/G-05 fail-closed）

| 校验 | 时机 | 行为 |
| --- | --- | --- |
| `StorageConnectionString` 非空 | 注册期 | 抛 `ArgumentException`（现状保留） |
| `StorageProvider` ∈ {SqlServer, PostgreSql} | 注册期 | 抛 `NotSupportedException`（现状保留） |
| `TransportConnectionString` 非空且**无默认值** | 注册期 | 移除 `"localhost"` 默认；空则抛（**行为变化，需在 PR 明示**） |
| `TransportProvider` ∈ {RabbitMQ, Kafka} | 注册期 | 抛（现状保留） |
| `ICompensationExecutorRegistry` 存在 | **应用启动期**（`IValidateOnStart` 选项校验或启动自检 hosted service） | 缺失即启动失败并给出修复指引（`AddCompensationExecutors()`） |
| 持久化仓库可解析 | 启动自检（同上） | `IRepository<TransactionLog>`/`IRepository<TransactionCompensation>` 不可解析即启动失败（替代首次写入才暴露） |

### 3.3 持久化与补偿链落位（G-04/G-05/G-10）

- **实体映射责任**：框架提供「集成装配包」（见 §3.4 同一归属）：
  - 提供 `IEntityTypeConfiguration<TransactionLog>` / `<TransactionCompensation>`（表名、键、索引：`TransactionId`、`Status`）；
  - 提供 `AddCrestCapPersistence<TContext>()`（或等价最小扩展）把配置应用到应用 DbContext 并注册框架仓储所需绑定；
  - 文档章节：实体迁移（应用侧 migration）+ 示例装配（供 CI fixture 复用）。
- **补偿执行真实验证**：executor 正常 → Completed；executor 抛异常 → Retrying（退避）→ 重试成功 Completed；达到 MaxRetries → Failed 且保留 ErrorMessage；**任何路径不得在未调用 executor 时标记 Completed**。
- **失败可见性**：executor 缺失 → `Failed` + 明确消息（现状已有）；启动期校验（§3.2）使该状态在正常装配下不可达。

### 3.4 原子发布契约（G-06/G-14，消费 #137）

约定（相对 #137 交接文档的落地方案）：

1. **受管窗口内发布 = 同事务或确定性失败**：
   - `CapDistributedEventPublisher` 检测当前是否有受管事务窗口（`IUnitOfWorkTransactionLeaseProvider.TryAcquireCurrentLease`）；
   - 有窗口：通过 **EF 适配边界** 借用原生 `DbContext`/`IDbContextTransaction`（#137 `IEfCoreTransactionBorrower`），将其包装为 CAP 事务（`ICapPublisher.Transaction = provider-specific CapTransaction { DbTransaction = <native> }`，AutoCommit=false），发布即写入**同一事务**；由 UoW 内核提交/回滚消息表写入；
   - 无窗口：普通发布（CAP 自身提交后投递语义），文档明示「与业务落库非原子」；
   - 有窗口但借用不可用（非受管事务、独立宿主、资源不匹配等）→ **确定性失败**，不静默降级为非事务发布。
2. **适配器落位**：EF 耦合代码（borrower 消费、CapTransaction 包装、实体映射）放**集成层新项目**（建议 `src/Integrations/` 下，名称待裁定 Q3），Runtime 的 CAP 包保持不引具体 ORM Provider（G-14）。
3. **语义如实声明**（G-12）：
   - 本机制提供「消息与业务数据同库同事务写入」的原子性（由 #137 内核提交/回滚保证）、CAP 侧 at-least-once 投递与重试（CAP 能力）；
   - **不承诺**分布式 exactly-once、2PC、跨库原子；依次 commit 不构成原子；
   - `ICapPublisher` / `CapTransaction` 的真实行为以本项实测（S3 故障用例）为准并写入文档。
4. **故障语义映射**（来自 #137 交接 §6，逐条转成用例）：
   - UoW 回滚 → 消息表写入随事务回滚（broker 侧观察不到消息）；
   - 提交派发期失败（Unknown）→ 消息可能已随事务提交亦可能未提交；不得宣称「已发布/已回滚」二选一；
   - 提交后通知失败（Committed）→ 不影响消息原子性结论；
   - 未完成退出（终结 + 丢弃）→ 消息不得依赖未完成窗口内的写入。

### 3.5 内存件处置清单（G-07/G-08/G-09）

| 对象 | 处置 | 依赖/引用检查结论 |
| --- | --- | --- |
| `DistributedTransactionCapModule` | 归档 | 仅模块发现机制引用（自动）；移除后无编译引用 |
| `TransactionLogger`（内存） | 归档 | 模块与测试引用；测试改为持久化实现或删除（见 S1） |
| `DefaultTransactionCompensator` | 归档 | 同上 |
| `UnitOfWorkIntegrationExtensions` + `UnitOfWorkTransactionParticipant` + `ITransactionParticipantRegistry` | 归档 | 零消费者（#137 交接表） |
| `IDistributedTransaction` / `DistributedTransaction` / `DistributedTransactionManager` / `ITransactionParticipant` / `IDistributedTransactionManager` | **建议归档**（Q2 待裁定） | 仅其自身测试引用；无 CAP/数据库语义；与「唯一内核」冲突 |

归档统一落 `99_RecycleBin/issue-125-cap-consolidation/` 并附 README（原路径、原因、替代路径）。

### 3.6 支持声明（G-11）

`CrestCreates.DistributedTransaction(.CAP)` 与集成适配包在支持表中逐项声明：

- 运行时：JIT 路径（待 S2/S3 测试证据）；
- trim：**未验证**（无 trim 证据，不得标 trimming-verified）；
- NativeAOT：**未验证**（CAP 及其存储/传输 SDK 未做 publish/link/run 门禁；不借 Core/UoW 原生证据推导）。

### 3.7 文档修正（G-12）

- `memory.md` 与支持表更新（入口、归档、证据）；
- 若存在引用「分布式事务保证」的过时表述，按 §3.4.3 修正；
- `CanCompensateAsync` 语义：删除或改为按补偿记录真实判定（跟随 Q2 裁定；若引擎归档则一并消失）。

---

## 4. 实施切片（全部基于本项设计通过后的 master）

| 切片 | 内容 | 验收映射 |
| --- | --- | --- |
| S1 入口收口 | 扩展唯一入口；模块/内存件/死桥归档；配置校验表落地（含 `TransportConnectionString` 默认值移除）；启动自检（executor registry + 仓库可解析）；测试项目改造（不依赖内存件） | 验收 1（顺序不变量）、5（归档）；G-01/02/03/05/08/09 |
| S2 持久化与补偿真实性 | 集成装配包（实体映射 + 装配扩展 + 文档）；补偿链测试（成功/失败重试/Failed/executor 缺失不可达）；PG Testcontainers 证据 | 验收 2（补偿 executor 调用、失败不标 completed）；G-04/10 |
| S3 原子发布 | 受管窗口检测 + EF 适配（CapTransaction 包装）+ 故障用例（回滚不发布、提交失败 Unknown、无窗口语义、借用拒绝确定性失败）；PG + RabbitMQ Testcontainers 证据（发布、失败恢复/重试） | 验收 2/3；G-06/14 |
| S4 声明与收尾 | 支持表逐项声明、文档修正、memory.md 同步、issues log | 验收 4/5；G-11/12/13 |

每个切片独立 PR、基于前一阶段合并后 master；本地测试通过后推送 CI；不自动合并。

---

## 5. 明确不做（防扩界）

- 不新增通用事务引擎 / 不引入新的 UoW manager（消费 #137 契约）；
- 不实现跨库、跨 ORM 原子提交；不承诺 exactly-once/2PC；
- 不整体重命名或搬走 CAP Provider 项目（E7）；
- 不为测试便利添加生产不使用的公开扩展点；
- 不在本项内改造 CAP SDK 本体（仅适配与装配）。

---

## 6. 待审查裁定项

| # | 事项 | 建议 | 备选 |
| --- | --- | --- | --- |
| Q1 | 模块入口处置 | 归档 | 改造为空注册并文档禁启用（不推荐：制造错觉） |
| Q2 | 参与者引擎归档范围 | 归档 `IDistributedTransaction`/`DistributedTransaction`/`DistributedTransactionManager`/`ITransactionParticipant` | 保留抽象、仅归档实现；或整体保留并标注（不推荐：与唯一内核冲突） |
| Q3 | EF 原子发布适配 + 实体映射落位 | 新集成项目（`src/Integrations/` 下） | Runtime.CAP 直接引用 Data.EFCore（违反原则，不推荐） |
| Q4 | `TransportConnectionString` 默认值移除 | 移除（空即抛） | 保留默认但启动告警（弱 fail-closed） |
| Q5 | CI broker 选型 | RabbitMQ Testcontainers（与默认 TransportProvider 一致） | Kafka（CI 时间成本更高） |
| Q6 | 事务 API 核实深度 | S3 中实测 `ICapPublisher.Transaction` 包装既有事务的行为并写故障用例 | 仅文档核对（证据不足，不推荐） |

---

（本设计为 rev.1，供审查；实施以审查通过版本为准。）
