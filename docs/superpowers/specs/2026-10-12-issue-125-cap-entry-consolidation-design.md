# Issue #125 CAP 入口收口与事务承诺核实 — 设计与现状清单

日期：2026-10-12（rev.3，响应第二轮审查 `docs/review/2026-10-12-pr-140-cap-design-review-round2.md`：R2-D140-01 扫描配置约束 + 非阻塞说明；此前 rev.2 响应首轮 D140-01…04）
基线：master `1dc4a9cc`（PR #139 / #137 合并后）
输入：
- Issue #125 正文（含验收项与「纠正审查」）
- `docs/review/2026-10-10-framework-overall-architecture-review-follow-up.md` E7 修正（不能整体判玩具；核心问题是入口语义分裂与假补偿）
- #137 交接文档 `docs/superpowers/specs/2026-10-10-issue-137-cap-handoff.md`（CAP 参与契约、拒绝项、原子发布边界）
- CAP 10.0.1 锁定版本**源码级核实**（§1.8；引用文件均为 v10.0.1 tag）
- 代码事实（本文所有引用以当前基线为准）

> rev.3 变更摘要：补扫投递的**配置前提与启动校验**（有效 `FailedRetryCount>0`、扫描间隔有效正值、lookback 受支持范围 [30s,∞)、以最终生效值为准；新增框架 option 透传 lookback）；投递延迟改写为「名义扫描等待，非时限」；参与判定公共结果**不再暴露可强转句柄**（原生句柄仅经 Provider borrower）；补迁移/行为取舍（窗口内即时发布转为扫描等待、窗口内延迟拒绝、`FailedRetryCount=0` 不再是合法配置）；S1/S3 验收补配置负例与宽松超时断言；memory.md 头部残留与 PR 正文同步。
>
> rev.2 变更摘要：重写 §3.4 原子发布协议（参与判定三值 + 瞬态绑定 + 扫描恢复投递）；补 §1.8 源码核实记录；按 D140-03 增加存储身份绑定规则；按 D140-04 收窄 executor 校验承诺；按 Q1–Q6 裁定更新 §3.1/§3.5/§6；修订 §4 切片与验收矩阵。

---

## 0. 结论摘要

1. **入口分裂确认**：同一包内两条注册路径——`[CrestModule]` 模块入口注册**内存实现**，扩展入口注册**持久化实现并调用 `AddCap`**。模块会被 BuildTasks 自动发现，在任何引用该包的应用里**无需显式调用扩展**即完成注册；解析结果取决于注册顺序，存在静默回落内存实现的风险（G-01）。
2. **唯一正式入口**：扩展 `AddCrestCapDistributedTransaction` 收口为唯一正式入口；模块归档（99_RecycleBin）。依据是**唯一装配原则与现状能力**（零消费者、双入口分裂、模块路径无法完成配置绑定装配），并在 S1 验证模块清单/自动注册产物不再残留（Q1 裁定）。
3. **内存件与死代码归档**：`TransactionLogger`、`DefaultTransactionCompensator`、`UnitOfWorkIntegrationExtensions`、参与者列表引擎（`IDistributedTransaction`/`DistributedTransaction`/`DistributedTransactionManager`/`ITransactionParticipant`，Q2 裁定同意）归档；归档后**补偿的注册与触发责任在文档中明确**（CAP 不会自动调用自定义补偿器）。
4. **原子发布协议（rev.2 重写；rev.3 补配置前提）**：参与判定三值（确无可见窗口 / 已获准 / 拒绝参与），由内核侧最小平台补充提供（公共结果只报决策/原因，不含可强转句柄）；发布路径「瞬态绑定 + 事务内写行 + 不 flush（缓冲丢弃）+ CAP 存储扫描恢复投递」；扫描配置以**最终生效值**启动校验（有效 `FailedRetryCount > 0`、间隔有效正值、lookback ≥ 30s），投递延迟声明为**名义扫描等待、非时限**；所有拒绝路径在写入前确定性失败；不调用 CAP 的 Commit/Rollback/Dispose 是硬边界。行为取舍（窗口内即时消息改扫描等待、窗口内 `PublishDelayAsync` 拒绝、「禁用重试」不可用）进入迁移说明（§3.8）。
5. **存储身份绑定**（D140-03）：声明支持的 Provider/数据库组合（EF+Npgsql ↔ CAP PostgreSql，单一固定数据库）；发布前校验 CAP 存储目标与参与资源的数据库身份（规范字段比较，非原始串文本）；不匹配在多库双向场景下拒绝于写入前。
6. **证据**：PG + RabbitMQ Testcontainers 集成门禁覆盖审查给出的完整故障矩阵（正常/回滚/放弃/Unknown/提交确认后清理失败/连续发布/RequiresNew/结束后普通发布/延迟消息/重启恢复）；executor fixture 消费**真实生成产物**。
7. **不做**：不另造通用事务引擎；不把内存参与者列表或依次 commit 推导为分布式原子；不整体重命名/搬走 CAP Provider（E7）。

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
- 不调用 `AddCap`，不读取任何配置；`CapEventBus` 依赖 `ICapPublisher`（`AddCap` 提供）与 `IEventValidator` → 模块单独使用时装配不完整。
- `ModuleBase.OnConfigureServices(IServiceCollection)` 无配置上下文；其余钩子（`OnPreInitializeAsync` 等 / `OnApplicationInitializationAsync(IHost)`）均在服务注册之后运行。

### 1.2 入口 B — 扩展（手动调用）

`.../Extensions/DistributedTransactionCapServiceCollectionExtensions.cs`

- `AddCrestCapDistributedTransaction(IConfiguration, Action<DistributedTransactionCapOptions>?)`：绑定 `DistributedTransaction:CAP`；注册持久化 `ITransactionLogger → PersistentTransactionLogger`、`ITransactionCompensator → PersistentTransactionCompensator`（:37-38）；可选注册 `CompensationRetryBackgroundService`（:41-45）；调用 `services.AddCap(...)`（:48）并 `ConfigureStorage`/`ConfigureTransport`。
- `ConfigureStorage`：`StorageConnectionString` 空 → 抛；Provider 不在 {SqlServer, PostgreSql} → 抛（:64-88，fail closed 已有）。`ConfigureTransport` 同理（:90-105）。

### 1.3 配置链

`Options/DistributedTransactionCapOptions.cs`（节名 `DistributedTransaction:CAP`）

| 配置 | 默认值 | 现状评估 |
| --- | --- | --- |
| `StorageProvider` | `SqlServer` | 显式枚举；非法值注册期抛 |
| `StorageConnectionString` | 空串 | 注册期抛（fail closed） |
| `TransportProvider` | `RabbitMQ` | 显式枚举 |
| `TransportConnectionString` | `"localhost"` | **非空默认值**：未配置时静默指向本机（G-03） |
| `DefaultGroup` | `"crestcreates"` | 无校验需求 |
| `FailedRetryCount/Interval` | 5 / 60s | 透传 CAP（CAP 默认 50/60s，此处覆盖为 5/60s） |
| `CompensationMaxRetries/RetryInterval` | 3 / 30s | 补偿重试参数 |
| `EnableCompensationBackgroundWorker` | true | 后台重试服务开关 |

### 1.4 持久化链

- `PersistentTransactionLogger`：`IRepository<TransactionLog, Guid>` 读写 `TransactionLog`。
- `PersistentTransactionCompensator`：`IRepository<TransactionCompensation, Guid>` + `ICompensationExecutorRegistry` + options；注册补偿、查询 Pending/Retrying、按 CreatedAt 倒序执行、指数退避（上限 300s）、执行器缺失或抛异常 → `Retrying`（达 MaxRetries → `Failed`）。
- **实体映射缺口**：`TransactionLog`/`TransactionCompensation` 全仓无框架侧 EF 映射（G-04）。
- **执行器注册表缺口**：`ICompensationExecutorRegistry` 由生成器产物提供（`CompensationExecutorCodeWriter.cs:33,100`），应用须显式调用 `AddCompensationExecutors()`；全仓无生产消费者（G-05）。

### 1.5 发布链

- `CapDistributedEventPublisher`：包装 `ICapPublisher.PublishAsync(topic, message, headers, ct)` / `PublishDelayAsync`；**不携带事务**。
- `CapEventBus : DistributedEventBusBase`：发布 → topic 映射（`命名空间.类型名`）→ publisher；`Subscribe/Unsubscribe` 抛 `NotSupportedException`。

### 1.6 补偿执行链

- 真实链（持久化路径）：UoW/业务失败 → `RegisterCompensationAsync`（持久化 Pending）→ `CompensateAsync` 或后台 `ProcessRetryingCompensationsAsync` → `ExecuteCompensationAsync` → `ICompensationExecutor.ExecuteAsync` → 成功 Completed / 失败 Retrying / 达上限 Failed。
- 假链（内存路径）：`DefaultTransactionCompensator` 对 Pending 项直接 `MarkCompensationCompletedAsync`（不调用执行器）；`CanCompensateAsync` 恒 `true`。
- 参与者引擎：`DistributedTransactionManager`/`DistributedTransaction` 为内存参与者列表伪 2PC；`BeginAsync` 空操作。
- `UnitOfWorkIntegrationExtensions.RegisterWithDistributedTransaction`：零消费者死扩展（#137 交接已判）。

### 1.7 消费与测试现状

- **生产消费者：零**（samples 不引用；两入口均无调用方）。
- 测试仅覆盖内存 manager（Moq）；CI "Runtime / Audit & DistributedTransaction" 步骤执行该测试项目；无 CAP 集成门禁。
- 质量债务：CAP 包内 9 处 CS1998、废话注释。

### 1.8 CAP 10.0.1 机制核实（源码级；v10.0.1 tag）

> 本节为设计定案输入：核实锁定版本的真实行为，避免把架构未知量推到实现期。**运行时行为证据仍由 S3 CI 用例提供**；以下为源码推导结论（高置信）。

| # | 结论 | 依据（v10.0.1 源码） |
| --- | --- | --- |
| M-01 | 事务发布时消息行**在发布调用内**写入给定事务（`StoreMessageAsync(name, message, transaction)` 用 `IDbContextTransaction.GetDbTransaction()` 的连接 + 事务执行 INSERT；`StatusName='Scheduled'`、`ExpiresAt=NULL`），同时把 `MediumMessage` 放入**内存缓冲**；`AutoCommit=false` 时不自动提交 | `ICapPublisher.Default.cs`（PublishInternalAsync :153-179）；`IDataStorage.PostgreSql.cs` StoreMessageAsync :114-158 |
| M-02 | 缓冲**只**经 `CapTransactionBase.FlushAsync()` 投递（延迟消息入调度器、即时消息入发布队列）；`Flush`/`FlushAsync` 为 `protected virtual`；标准 `CommitAsync` = 提交底层事务 + `FlushAsync()` | `ICapTransaction.Base.cs` :92-133；`ICapTransaction.PostgreSql.cs` :41-56 |
| M-03 | `CapTransactionBase.Dispose()` 会对 `DbTransaction`（若可 IDisposable）**执行释放**——CAP 生命周期操作会触碰借用句柄，必须规避 | `ICapTransaction.Base.cs` :138-142 |
| M-04 | `ICapPublisher.Transaction` 由 AsyncLocal `CapTransactionHolder` 承载，可设置任意的 `CapTransactionBase` 子类实例（发布路径会强转 `(CapTransactionBase)Transaction`） | `ICapPublisher.Default.cs` :30-52；PublishInternalAsync :170 |
| M-05 | 未 flush 但已随事务提交的 `Scheduled` 行会被**重试扫描**捡起投递：`StatusName IN ('Failed','Scheduled') AND Retries < FailedRetryCount AND Version=... AND Added < now - FallbackWindowLookbackSeconds`（默认 240s），处理器每 `FailedRetryInterval`（默认 60s；本仓选项覆盖为 60s）运行一次并 `EnqueueToPublish` | `IDataStorage.PostgreSql.cs` GetMessagesOfNeedRetryAsync :341-380；`IProcessor.NeedRetry.cs` :36-83；`CAP.Options.cs` 默认值 :26-37 |
| M-06 | 延迟行调度扫描只处理 `Delayed`/`Queued` 状态（不含 `Scheduled`）→ 事务分支写入的延迟消息在无 flush 时**不会**按延迟调度 | `IDataStorage.PostgreSql.cs` ScheduleMessagesOfDelayedAsync :245-260 |
| M-07 | CAP PG 表由存储初始化器在**配置的存储连接**上创建：`CREATE SCHEMA IF NOT EXISTS "{schema}"`（默认 schema `cap`）+ published/received/lock 表；无事务的存储操作与重试查询走该配置连接 | `IStorageInitializer.PostgreSql.cs`；`CAP.EFOptions.cs`（`DefaultSchema="cap"`）；`IDataStorage.PostgreSql.cs` :141-155 |

---

## 2. 缺口清单

| # | 类别 | 缺口 | 证据 | 影响 |
| --- | --- | --- | --- | --- |
| G-01 | 缺陷 | 双入口注册分裂：模块注册内存实现且自动发现；解析结果依赖注册顺序 | 1.1 / 1.2 | 静默回落内存、假补偿上线风险；违反唯一主链 |
| G-02 | 缺陷 | 模块路径无法完成配置绑定的 CAP 装配（结构缺口） | `ModuleBase.OnConfigureServices(IServiceCollection)` 签名；模块不调 `AddCap` | 「正式配置缺失明确失败」在模块路径不成立 |
| G-03 | 缺陷 | `TransportConnectionString` 默认 `"localhost"` | `DistributedTransactionCapOptions.cs` | 延迟暴露、可能误连本机 broker |
| G-04 | 遗留 | 持久化实体无框架侧映射/装配文档 | 1.4 | 持久化链实际不可运行（未验证） |
| G-05 | 缺陷 | 扩展不校验 executor registry；「registry 可解析 ≠ 必需 executor 完整」 | 1.4；D140-04 | 运行期才暴露的补偿失败 |
| G-06 | 缺陷 | 发布链不接入受管事务 | 1.5 | 「原子发布」声明不成立 |
| G-07 | 缺陷 | 内存参与者引擎承载伪 2PC 语义 | 1.6 | 误导上层；与唯一内核冲突 |
| G-08 | 遗留 | 死扩展 + 参与者桥 | 1.6 | 双轨风险 |
| G-09 | 缺陷 | 假补偿：不调用执行器即标完成 | 1.6 | 「失败不能只把状态改成 completed」的反面样本 |
| G-10 | 测试 | 无持久化/补偿执行/发布/故障恢复测试 | 1.7 | 绿灯信号不证明任何 CAP 语义 |
| G-11 | 声明 | CAP 集成无 JIT/trim/AOT 或「未验证」声明 | AGENTS.md 分层规则 | 支持表缺项 |
| G-12 | 文档 | 事务 API 保证未核实；无 exactly-once/2PC 声明边界 | §1.8 前无依据 | 文档与实现不符 |
| G-13 | 质量 | 9 处 CS1998、废话注释 | §1.7 | 维护噪声 |
| G-14 | 边界 | 原子发布适配落位：Runtime 不引具体 ORM Provider | `DependencyBoundaryTests.cs:48-53`；#137 交接 §4.4 | 落位错误 = 边界违规 |

---

## 3. 设计

### 3.1 唯一正式入口与模块处置（G-01/G-02；Q1 裁定）

- **唯一正式入口**：`AddCrestCapDistributedTransaction(IConfiguration, configure?)`。
- **模块归档**：依据 = 唯一装配原则 + 现状能力（零消费者、双入口分裂、模块路径无法完成配置绑定装配）；这是对 #125 原「模块委托统一平台装配」方案的**替代**而非其实现——不为此改造通用模块框架。S1 验收包含：**验证生成模块清单/自动注册产物不再包含该模块**（BuildTasks 产物检查），并确认无编译引用残留。
- **归档后不变量**：`ITransactionLogger`/`ITransactionCompensator` 只有持久化一处注册；验收项「模块/扩展组合及注册顺序不会切回内存实现」以「无内存实现可回落」+ 启动校验直接成立。

### 3.2 配置与启动校验（G-03/G-05；D140-04 收窄）

| 校验 | 时机 | 行为 |
| --- | --- | --- |
| `StorageConnectionString` 非空 | 注册期 | 抛（现状保留） |
| `StorageProvider` ∈ {SqlServer, PostgreSql} | 注册期 | 抛（现状保留） |
| `TransportConnectionString` 非空且**无默认值** | 注册期 | 移除 `"localhost"` 默认；空则抛（**行为变化，PR 明示**） |
| `TransportProvider` ∈ {RabbitMQ, Kafka} | 注册期 | 抛（现状保留） |
| **扫描模式配置组（§3.4.2 投递前提）** | 注册期 | 校验对象为**最终生效值**：配置节绑定 + `configure` 回调合并后的同一实例，且该实例就是传给 `AddCap` 的实例——杜绝「先校验后被其他配置覆盖」 |
| ├ 最终生效 `FailedRetryCount > 0` | 注册期 | ≤0 → 启动失败：扫描模式下 0/负值使 `Retries < FailedRetryCount` 恒 false，**事务消息没有首次投递**；文档明示「禁用重试」在此模式下不可用 |
| ├ `FailedRetryIntervalSeconds` 有效正值 | 注册期 | ≤0 → 启动失败（防止零间隔轮询） |
| ├ `FallbackWindowLookbackSeconds` ∈ **[30s, ∞)**（新增框架 option，默认 240，经 `capOptions.FallbackWindowLookbackSeconds` 透传） | 注册期 | <30s（CAP 建议下限 `MinSuggestedValueForFallbackWindowLookbackSeconds`）或 ≤0 → 启动失败并提示受支持范围 |
| `ICompensationExecutorRegistry` 可解析 | 启动期 | 不可解析 → 启动失败 + 修复指引（`AddCompensationExecutors()`） |
| **声明必需执行器集合**（options `RequiredCompensationExecutors: string[]`，默认空）逐一可解析 | 启动期 | 缺失 → 启动失败并列出缺失名称 |
| 持久化仓库可解析 | 启动期 | `IRepository<TransactionLog>`/`IRepository<TransactionCompensation>` 不可解析 → 启动失败 |
| **新注册补偿的 participant 在执行前可执行** | `RegisterCompensationAsync` 写入 Pending **之前** | 无执行器 → 确定性异常（不写 Pending） |
| 历史补偿记录缺执行器 | 执行时 | 保留现有可检查 `Failed` 路径（不删除） |

**承诺收窄（D140-04）**：仅声明「registry 装配存在 + 声明的必需集合覆盖 + 新注册前校验」；不声明「缺失不可达」——历史记录、nonce 名称与版本演化的执行器缺失仍有明确失败路径。声明的必需集合来源 = 应用 options（生成器提供全量注册；应用声明其期望集合）；若应用不声明，启动校验退化为 registry 存在性 + 注册时校验。

### 3.3 持久化与补偿链落位（G-04/G-10）

- **实体映射责任**：集成装配包提供**显式强类型**配置：`TransactionLogConfiguration : IEntityTypeConfiguration<TransactionLog>` 与 `TransactionCompensationConfiguration`（表名、主键、`TransactionId`/`Status` 索引、列长）；应用在自身 `OnModelCreating` 显式 `modelBuilder.ApplyConfiguration(...)` 应用（文档 + 示例）。**不**自动修改任意应用 DbContext 模型、**不**新增 runtime 扫描 fallback。
- **仓储绑定**：集成包提供 `AddCrestCapPersistence<TContext>()`，仅负责「注册本上下文 + 注册 `IRepository<TransactionLog/TransactionCompensation>` 的 EF 实现 + 注册生成的执行器扩展调用指引」；其行为与限制在文档中逐条列出（不生成迁移；迁移由应用侧负责）。
- **补偿链测试**：
  - 顺序路径：成功/失败退避重试/达上限 Failed/executor 缺失（注册时拒绝 + 历史行 Failed）；
  - **明确声明限制**：并发补偿与进程崩溃恢复（`Executing` 状态残留）不在本项承诺内；测试不得用顺序 mock 宣称 durable compensation 闭环；`Executing` 残留列为已知限制（S4 支持表记录）。

### 3.4 原子发布协议（G-06/G-14；D140-01/02/03；rev.2 重写）

#### 3.4.1 参与判定（唯一权威与最小平台补充）

`TryAcquireCurrentLease` 的 `false` 只表达「未获准参与」，**不能**推导「无受管窗口」（未 Start、非事务、终态、释放、不可见链、无事务关联等都可能 false；部分 Provider 无 lease provider）。为此定义**参与判定三值**并由内核提供唯一权威（最小平台补充，归属 Data.Abstractions，紧邻现有 `TryGetCurrentManagedTransaction`；CAP 不复制 ambient 状态机）。公共结果**只报告决策与拒绝原因，不暴露任何句柄/上下文对象**（注释性约束可被强转绕过——原生 Context/Transaction 一律经 #137 Provider 借用边界获取，见下）：

```csharp
public enum UnitOfWorkParticipationDecision { NoVisibleWindow, Granted, Refused }

public enum UnitOfWorkParticipationRefusalReason
{
    NotStarted, NonTransactional, Terminal, Released,
    NoTransactionAssociation,   // 事务型已开始但 Provider 未曝光受管事务关联
    ResourceMismatch            // 租户/资源身份与读方不匹配
}

public readonly struct UnitOfWorkParticipationResult
{
    public UnitOfWorkParticipationDecision Decision { get; }
    public UnitOfWorkParticipationRefusalReason? RefusalReason { get; }
}

// 内核唯一权威（复用现有帧遍历；TryGetCurrentManagedTransaction 内部复用同一判定，
// lease/borrower 的原生句柄比对路径不变——句柄只存在于 Provider 适配层）
public static UnitOfWorkParticipationResult UnitOfWorkExecutionContext.ResolveParticipation(UnitOfWorkChainNode? readerNode);
```

**Granted 之后的原生资源获取**：CAP 集成先调用 `ResolveParticipation`；`Granted` 仅表示可以进入借用流程——原生 `DbContext`/`IDbContextTransaction` 经 #137 Provider 借用边界（EF 为 `IEfCoreTransactionBorrower`，含资源/事务引用比对）取得；容器内的租户/资源校验与 borrower 判定与内核同源（复核要求：实施时逐项核对）。适配器侧的拒绝（未受管原生事务、存储身份不匹配、借用边界缺失）以带错误码的确定性异常表达，均先于任何写入。

**判定与行为表**（验收：每种结果断言「是否调用 CAP 非事务发布/是否写消息表」，拒绝用例必须证明**未产生任何非事务副作用**）：

| 情形 | 判定 | 行为 |
| --- | --- | --- |
| 无受管帧（无 UoW） | NoVisibleWindow | 普通发布（CAP 非事务路径：CAP 自连接存行 + 即时投递）；文档明示与业务落库非原子 |
| 独立 DI 根 / 他链窗口（不可见） | NoVisibleWindow | 同上（**不**做全局扫描探测其他宿主） |
| 可见 owner：未 Start | Refused(NotStarted) | 确定性失败；消息不写、无发布副作用 |
| 可见 owner：非事务模式 | Refused(NonTransactional) | 同上 |
| 可见 owner：终态（Completed/RolledBack/Failed）或已释放 | Refused(Terminal) | 同上 |
| 可见 owner：事务型已开始但无事务关联句柄（Provider 未曝光/非 EF） | Refused(NoTransactionAssociation) | 同上（当前 CAP 集成仅声明支持 EF+Npgsql） |
| 可见 owner：租户/资源不匹配（内核数据准入拒绝） | Refused(ResourceMismatch) | 同上 |
| scoped 上下文存在**原生未受管事务**且无可见窗口（可检测时） | 适配器拒绝（`UnmanagedNativeTransaction`） | 确定性失败；CAP 不加入未受管事务、不通过连接嗅探探测（无可检测上下文时按无窗口处理，文档明示） |
| Granted 但集成借用边界不可用（装配缺失） | 适配器拒绝（`BorrowerUnavailable`） | 确定性失败（装配诊断：引用集成包并注册） |
| Granted 但 CAP 存储身份 ≠ 参与资源身份（§3.4.4） | 适配器拒绝（`StorageIdentityMismatch`） | 同上（在**写入前**） |
| Granted + 身份一致 + 借用成功 | Granted | 事务内写行（§3.4.2），投递走扫描恢复（§3.4.2） |

所有 Refused/装配失败路径都**先于** `ICapPublisher` 调用，保证零副作用（S3 用例逐一断言）。

#### 3.4.2 发布生命周期协议（D140-02）

**硬边界**：不调用 CAP 的 Commit/Rollback/Dispose（M-03 证明 CAP Dispose 会释放借用句柄）；借用句柄生命周期唯一归 #137 内核。

每次 Granted 发布：

1. 参与判定 → Granted（含 `TransactionHandle`）；
2. 身份校验（§3.4.4）→ 通过；
3. 集成借用边界取原生 `IDbContextTransaction`（经 #137 `IEfCoreTransactionBorrower`，资源/事务引用比对由 borrower 保证）；
4. **瞬态绑定**：`previous = publisher.Transaction; publisher.Transaction = guardedCapTransaction;`（`guardedCapTransaction` 为集成层的 `CapTransactionBase` 子类：`DbTransaction = <借用句柄>`、`AutoCommit = false`；**覆写 Commit/Rollback/Dispose 为守护**——抛确定性异常或仅解除字段引用，绝不触达借用句柄）；
5. `await publisher.PublishAsync(...)`：CAP 事务分支在**同一事务**内写 `cap.published` 行（M-01），并把消息加入**本实例缓冲**；
6. `finally { publisher.Transaction = previous; }` 瞬态恢复（不跨调用遗留；RequiresNew 内层发布同样只影响本次调用，不污染外层 publisher 状态）。

**缓冲处置**：不调用 flush、不保留跨发布缓冲——实例随调用结束丢弃（缓冲即丢弃）。**同一 owner 连续发布** = 逐条独立行（无共享缓冲状态）；**结束后普通发布** = 判定回落 NoVisibleWindow，走 CAP 原生路径。

**投递（提交确认后）**：采用 **CAP 存储扫描恢复作为正式投递机制**（M-05）：
- 提交确认后，行已随事务提交（`Scheduled`）；CAP `NeedRetryProcessor` 每 `FailedRetryInterval` 扫描 `StatusName IN ('Failed','Scheduled') AND Retries < FailedRetryCount AND Added < now - FallbackWindowLookbackSeconds` 并投递；
- **声明（名义扫描等待，非投递时限）**：健康低负载环境下，提交后的消息通常在 `FallbackWindowLookbackSeconds`（默认 240s）与 `+FailedRetryInterval`（默认 60s）**量级**内被扫描投递；实际等待取决于消息 `Added` 时间（扫描按 Added 判龄——长事务提交后可能很快被下一轮选中）、提交可见性、积压、锁竞争、批次上限（LIMIT 200）、停机与 broker 故障，可能显著延长。该量级为**能力描述**，不构成提交后投递时限；S3 以**有界宽松超时**断言最终投递，不按精确区间断言；
- **配置前提（并入 §3.2 启动校验）**：有效 `FailedRetryCount > 0`（0/负值使扫描恒不选中新行 → 事务消息没有首次投递，「禁用重试」在此模式下不可用）；`FailedRetryIntervalSeconds` 有效正值；新增框架 option `FallbackWindowLookbackSeconds ≥ 30s`（默认 240，透传 CAP 同名选项）；全部以**最终生效值**校验（与传给 `AddCap` 的同一实例）；
- **不依赖 flush**：适配器从不调用 `FlushAsync`/`CommitAsync`（守护）。

**各退出路径**：
- **正常 commit**：行提交 → 扫描投递（延迟声明如上）；
- **回滚/放弃/RolledBack**：行随事务回滚消失；零投递、零残留；
- **Unknown（提交派发失败/响应丢失）**：行存在 ⇔ 事务已提交（同一事务）——扫描按**数据库事实**投递；CAP 适配层不宣称「已发布/已回滚」；与 #137「Unknown 不被改写」一致；
- **提交确认 + 句柄清理失败（Committed + Failed）**：行已提交 → 扫描投递；与 #137「提交事实保留」一致；
- **延迟消息（`PublishDelayAsync`）在 Granted 窗口内**：M-06——事务分支行不会按延迟调度，声明**不支持** → Grant 后、写入前确定性 `NotSupportedException`（零副作用）；窗口外延迟发布保持 CAP 原生行为。

**关于 UoW 完成通知**：rev.1 曾考虑内核 post-commit 挂钩（用于 flush 时机）；扫描恢复机制消除了该需求——**不需要**新增内核完成通知能力，本项不引入第二执行内核。

#### 3.4.3 原子性与投递失败的分别报告

- **落库原子性**（本机制承诺）：消息行与业务数据同库同事务；回滚 ⇒ 无行无投递；提交 ⇒ 行存在——由 #137 内核提交/回滚唯一决定；
- **提交后投递**（CAP 语义，独立报告）：broker 不可用 ⇒ 行保持 `Scheduled/Failed`，按 `Retries < FailedRetryCount`（本仓 5）重试；耗尽 ⇒ 行停留 `Failed`（CAP 监控/仪表盘可见）——**不影响**「业务已提交」事实；
- 文档与测试分别陈述、分别断言，不合并为单一"成功"。

#### 3.4.4 存储身份绑定（D140-03；G-14 落位）

- **声明支持组合（本项唯一）**：EF Core + Npgsql 参与资源 ↔ CAP `PostgreSql` 存储；**同一固定数据库**。其他组合（SqlServer 存储、租户库、跨库）**确定性拒绝**（`Refused(StorageIdentityMismatch)` 或装配期诊断），拒绝发生在**写入前**，且不作为「每租户 CAP 恢复基础设施」扩展。
- **身份规则**（不按原始连接串文本比较）：从**借用事务的活连接**取 `NpgsqlConnection` 的规范字段（`Host`/`Port`/`Database`），与从 `StorageConnectionString` 解析的同名字段比较（忽略大小写差异的部分按 Npgsql 语义处理）；schema/table 由 CAP 初始化器在存储连接上创建（M-07，默认 schema `cap`）——同一物理库 ⇒ 同一 schema/表可被后台扫描访问；
- **诊断纪律**：输出 host/database 标识，**不输出连接凭据**；
- **Provider 不匹配**（CAP 存储 Provider 与参与资源驱动不一致）→ 拒绝；**数据库租户切换**（活动 UoW 中切库）由 #137 数据准入先行拒绝。

### 3.5 内存件处置清单（G-07/G-08/G-09）

| 对象 | 处置 |
| --- | --- |
| `DistributedTransactionCapModule` | 归档（S1 验证模块清单/自动注册产物无残留） |
| `TransactionLogger`（内存）、`DefaultTransactionCompensator`（假补偿） | 归档 |
| `UnitOfWorkIntegrationExtensions` + `UnitOfWorkTransactionParticipant` + `ITransactionParticipantRegistry` | 归档 |
| `IDistributedTransaction` / `DistributedTransaction` / `DistributedTransactionManager` / `ITransactionParticipant` / `IDistributedTransactionManager` | 归档（Q2 裁定同意；包含其零消费者抽象） |
| 旧测试项目（内存 manager 测试） | **随引擎归档或改造**（不保留内存路径断言；新测试面向持久化/发布真实路径） |

归档统一落 `99_RecycleBin/issue-125-cap-consolidation/` + README（原路径、原因、替代路径）。**归档后文档必须写明**：补偿的注册与触发是应用/业务责任（UoW 失败路径调用 `RegisterCompensationAsync`、由 `CompensateAsync` 或后台服务触发），CAP 不会自动调用自定义补偿器（Q2 裁定）。

### 3.6 支持声明（G-11）

`CrestCreates.DistributedTransaction(.CAP)` 与集成适配包逐项声明：JIT（S2/S3 证据）；trim **未验证**；NativeAOT **未验证**（不借 Core/UoW 原生证据推导）。CAP 存储/传输 SDK 为第三方组件，其能力不并入框架声明。

### 3.7 文档修正（G-12）

- memory.md 与支持表更新（入口、归档、证据、限制：补偿并发/进程崩溃恢复不承诺、`Executing` 残留为已知限制）；
- 事务 API 保证按 §3.4.3 + §1.8 修正；不承诺 exactly-once/2PC/跨库原子。

### 3.8 迁移与行为取舍（必须进入迁移说明与支持表）

扫描投递是一项**产品行为取舍**，不能只写「原子发布已支持」，以下变化须在迁移说明与支持表逐条突出：

1. 受管事务内的**即时消息**：从「提交后立即投递」变为「提交后经扫描等待投递」（名义量级 240–300s；`FallbackWindowLookbackSeconds`/`FailedRetryIntervalSeconds` 可调小，最小 lookback 30s，见 §3.2/§3.4.2）；
2. 受管事务内的 **`PublishDelayAsync`**：确定性拒绝（CAP 事务分支延迟调度限制，M-06）；窗口外延迟保持 CAP 原生行为；
3. **「禁用重试」语义变化**：`FailedRetryCount = 0` 在扫描模式下会关闭事务消息首次投递，配置校验直接拒绝——不能用它关闭重试；
4. 支持表按**组合**声明：PG+RabbitMQ 为第一组待验证组合（S3 真实 DB/broker 证据），不推导 SqlServer/Kafka/trim/NativeAOT；补偿并发与 `Executing` 崩溃恢复不承诺。

---

## 4. 实施切片

| 切片 | 内容 | 验收映射 |
| --- | --- | --- |
| S0（本设计 rev.2 已完成） | CAP 10.0.1 源码核实（§1.8 M-01…M-07）；参与判定与协议定案 | Q6：架构未知量在实施前收敛（运行时证据由 S3 提供） |
| S1 入口收口 | 扩展唯一入口；模块/内存件/死桥归档；配置校验落地（含**扫描模式配置组**：最终生效 `FailedRetryCount`/`FailedRetryIntervalSeconds`/`FallbackWindowLookbackSeconds` 校验与新增 lookback option 透传）；启动校验（registry + Required 集合 + 仓库可解析）；模块清单产物验证；测试项目改造/归档 | 验收 1、5；D140-04（启动部分）；R2-D140-01 |
| S2 持久化与补偿真实性 | 实体配置类 + `AddCrestCapPersistence` + 文档；注册前执行器校验；顺序补偿链测试（成功/重试/Failed/缺失）；**fixture 消费真实生成产物**；PG Testcontainers | 验收 2；D140-04（运行部分） |
| S3 原子发布 | 参与判定 API + 集成借用适配 + 守护 CapTransaction + 身份绑定；**故障矩阵**（见下，含扫描配置负例/正例）；PG + RabbitMQ Testcontainers（可调小 lookback 加速证据；本地经 podman socket 可跑） | 验收 2/3；D140-01/02/03；R2-D140-01 |
| S4 声明与收尾 | 支持表、文档修正、memory.md、issues log | 验收 4/5 |

**S3 故障矩阵（每项真实 DB/broker + 可控故障）**：
1. 正常 commit：提交前 broker 无消息；提交后在**有界宽松超时**内投递（名义扫描等待，见 §3.4.2，不按精确区间断言）；行 Succeeded/清理；
2. 回滚/放弃：零行、零消息；
3. Unknown（提交派发故障注入）：行存在 ⇔ 提交事实；扫描按事实投递；不宣称；
4. 提交确认 + 清理失败：行投递正常；
5. 同 owner 连续多次发布：逐条独立行、无重复、无丢；
6. RequiresNew 内外均发布：各自事务、各自投递；
7. 结束后普通发布：无窗口路径正常；
8. 窗口内延迟发布：确定性拒绝、零副作用；窗口外延迟保持原生；
9. 重启恢复：broker 停止期间提交 → 重启后扫描投递（M-05 条件）；
10. 拒绝矩阵全量：对 §3.4.1 每个 Refused/适配器拒绝情形断言无行、无消息、无 publisher 状态残留；
11. 生命周期调用计数：适配器对借用句柄的 Commit/Rollback/Dispose 调用次数 = 0；
12. **扫描配置约束**：`FailedRetryCount` 0/负值、`FailedRetryIntervalSeconds` ≤0、`FallbackWindowLookbackSeconds` <30s → 启动失败（负例，且不产出任何可发布装配）；最低允许配置（count=1、lookback=30s、小间隔）仍完成事务消息**首次投递**（正例）。

---

## 5. 明确不做（防扩界）

- 不新增通用事务引擎 / 不引入第二 UoW manager（消费 #137 契约；§3.4.2 明确取消内核完成通知需求）；
- 不实现跨库、跨 ORM、每租户库的 CAP 恢复；不承诺 exactly-once/2PC；
- 不整体重命名或搬走 CAP Provider 项目（E7）；
- 不为测试便利添加生产不使用的公开扩展点；
- 不在本项内改造 CAP SDK 本体（仅适配与装配）。

---

## 6. Q1…Q6 裁定落实情况

| 项 | 裁定建议（审查方） | 落实 |
| --- | --- | --- |
| Q1 模块 | 可以归档，理由改为唯一装配与现状能力（非"签名证明不可能"）；验证生成模块清单/自动注册产物无残留；无需改造通用模块框架 | §3.1 已改措辞与依据；S1 增加产物验证 |
| Q2 内存引擎 | 同意归档实现与零消费者抽象；文档说明归档后补偿注册/触发责任 | §3.5 已补文档要求 |
| Q3 落位 | 同意独立集成项目；EF/具体 CAP storage 耦合与实体映射留集成层 | §3.3/§3.4.4 已明确；Runtime 仅消费强类型适配契约 |
| Q4 默认连接 | 同意移除 localhost 默认，显式配置、失败关闭 | §3.2 落地（行为变化已标注） |
| Q5 broker | 同意 PG + RabbitMQ 作为第一组证据；不推导 Kafka/SqlServer 已验证 | §3.6/§4 已限定 |
| Q6 核实深度 | 必须实测；设计通过前明确方案；可先最小 spike | §1.8 源码级核实先行；S3 运行时门禁；`ICapPublisher.Transaction` 包装行为按 M-01/M-02/M-04 定义 |

---

（本设计为 rev.2，供审查；实施以审查通过版本为准。）
