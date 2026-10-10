# Issue #137 UnitOfWork 事务传播、资源归属与业务参与主链 — 设计记录与现状清单

日期：2026-10-10（rev.3.1：设计经第三轮审计通过；含第一轮 S137-01…07、第二轮 R2-S137-01…03 修订与第三轮计划/文稿修正，见文末第八节）
实施基线：master `988bfd2f`（PR #136 merge，即 #124 闭环后的真实主链）
关联：[Issue #137](https://github.com/OrchesAdam/CrestCreates/issues/137)、[Issue #121](https://github.com/OrchesAdam/CrestCreates/issues/121)、[#124 设计记录](../../review/2026-10-10-issue-124-unitofwork-unified-registration.md)、[#123 证据矩阵](../../review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md)、[第一轮 Spec 审计](../../review/2026-10-10-issue-137-uow-spec-review.md)、[第二轮 Spec 审计](../../review/2026-10-10-issue-137-uow-spec-review-round2.md)
下游：#125（CAP）必须在本项验收并合并后实施；#130 复用本项的 Provider 能力边界。

本文档是 #137 的第 1 项交付（Issue §1「先交付设计与现状清单，再实施」）：真实调用链、公开契约清点与处置、资源所有权表、状态/传播表、设计裁定与测试推导。实施按配套计划文件 `docs/superpowers/plans/2026-10-10-issue-137-uow-transaction-propagation-implementation.md` 拆分为多个 PR，每个 PR 基于前项合并后的 master。

---

## 一、真实调用链（基线事实）

### 1.1 装配链

```
模块/宿主（OrmModuleBase、AddCrestWeb、samples EntityFrameworkCoreModule）
  ├─ services.AddUnitOfWork(defaultProvider?)       # 工厂 + Manager + 注册状态（唯一入口）
  ├─ parent.AddEfCoreUnitOfWork()                    # scoped EfCoreUnitOfWork + Provider 绑定
  │     binding: Factory = sp => sp.GetRequiredService<EfCoreUnitOfWork>()
  │              SupportsRequiresNew = true
  │              AmbientContextFactory = sp => sp.GetRequiredService<IDataBaseContext>()
  ├─ FreeSql/SqlSugar OrmModule                      # 绑定 SupportsRequiresNew = false（fail closed）
  └─ IDataBaseContext 注册（各宿主不同）：
       框架默认（AddCrestCreatesEfCoreDbContext）: IEntityFrameworkCoreDbContext 直绑 CrestCreatesDbContext
       自定义宿主（LibraryManagement/SaaSHelpdesk/测试工厂）: EfCoreDbContextAdapter(业务 DbContext)
```

- 绑定在装配期收集为单例索引 `UnitOfWorkProviderBindingRegistry`（重复绑定确定性失败）；工厂/Manager 为 scoped，从**调用方作用域**解析 UoW（无反射、无扫描）。
- EF Core 是全仓唯一 `SupportsRequiresNew: true` 的绑定；FreeSql/SqlSugar 声明 false，使用 requiresNew 时确定性 `NotSupportedException`。

### 1.2 执行链（三个入口，各自实现同一模式）

| # | 入口 | 触发 | 行为（基线） |
| --- | --- | --- | --- |
| A | `UnitOfWorkMoAttribute`（Rougamo AOP）`src/Framework/Infrastructure/CrestCreates.Aop/Interceptors/UnitOfWorkMoAttribute.cs` | `[UnitOfWorkMo]`（手写服务、生成 CRUD 服务、`CrestAppServiceBase` 的 Create/Update/Delete） | OnEntry：`BeginScope(isTransactional)`（**无 requiresNew**，即 Required）；owner 且事务 → `BeginTransactionAsync`；自维护静态 `AsyncLocal<Stack<IUnitOfWorkScope?>>`。OnSuccess：owner 且事务 → `CommitTransactionAsync`；owner 非事务 → `SaveChangesAsync`。OnException：owner → `RollbackTransactionAsync`。之后 `scope.Dispose()`。Manager 缺失 → warn + skip（无 ORM 兼容路径，保留）。 |
| B | 生成 Dynamic API `DynamicApiGeneratedRuntime.ExecuteAsync`（`src/Framework/Api/CrestCreates.DynamicApi/DynamicApiGeneratedRuntime.cs`） | `DynamicApiAotSourceGenerator` 生成端点：方法/类型含 `[UnitOfWorkMo]` → (requiresUoW=true, requiresTransaction=属性值，默认 true)；否则 GET → (false,false)，非 GET → (true,true)（生成器 678-707 行） | `context.RequestServices.GetService<IUnitOfWorkManager>()`；null → 直接执行；`BeginScope(isTransactional)`；owner 且事务 → Begin / Commit，owner 非事务 → SaveChanges；catch：owner → Rollback，rethrow。**与 A 相同模式的第二份实现（两个重载）。** |
| C | `IUnitOfWorkManager.Execute/ExecuteAsync<T>` | 框架 API / 测试 / AOT fixture | `BeginScope(requiresNew: true)`；`BeginTransactionAsync`；action；`CommitTransactionAsync`；catch → TryRollback（吞清理异常）。**同步版与异步版是两份实现。** |
| D | FreeSql `TransactionalAttribute` + `FreeSqlUnitOfWorkManager` | FreeSql 包内 | 直接使用 FreeSql SDK 的 `UnitOfWorkManager.Begin(propagation, isolation)`；**Provider 内部路径**，不是平台 Manager 第二条主链（#124 已裁定保留并声明）。 |

### 1.3 资源链与 requiresNew 隔离

```
UnitOfWorkManager.BeginScope
  ├─ 有环境 && Required     → 复用（IsOwner=false；**provider/isTransactional 参数被忽略**）
  ├─ 有环境 && RequiresNew  → IServiceScopeFactory.CreateScope()
  │     ① 先 Push 子 scope 的 IDataBaseContext 到 UnitOfWorkAmbientContext（构造不变量：ambient 不得重定向新 UoW 的构造）
  │     ② 子 scope 内 IUnitOfWorkFactory.Create → 子 scope 的 EfCoreUnitOfWork
  │     ③ _currentScope 链记录；退出时逆序恢复（ambient 先恢复，再释放）
  └─ 无环境                 → 从调用方 scope 解析 UoW（DI 跟踪，每请求 scope 一个实例）
```

- `EfCoreUnitOfWork` 构造持有 `IDataBaseContext.GetNativeContext()` 得到的 `DbContext`（构造期捕获）；`BeginTransactionAsync` → `Database.BeginTransactionAsync()`（**无隔离级别、无 CancellationToken**）。
- 上下文选择：`EfCoreDbContextAdapter` 与默认 `CrestCreatesDbContext` 的数据访问成员统一经 `EfCoreAmbientContext.Resolve` 在**每次操作时**解析 effective context；正式仓储（`EfCoreRepository`）经 `_dbContext.Queryable<TEntity>()/Set<TEntity>()` **逐次解析**（不缓存），因此预注入仓储在 requiresNew 期间跟随内层 UoW（#136 R136-1/R2-136-1 已验证）。
- 仓储写入在 `CurrentTransaction == null` 时自动 flush（`SaveChangesIfNoActiveTransactionAsync`）——非事务模式下逐写落库，事务模式下累积到 commit。
- 提交顺序（EfCore）：收集 `IHasDomainEvents` → `SaveChanges` → `Commit` → 释放事务句柄 → 发布域事件（重试≤3、指数退避，最终失败**吞掉并 `Console.WriteLine`**）。`catch → Rollback + rethrow` 包住整段（含发布阶段）。
- 生命周期收尾（#136 R2-136-3）：绑定路径顶层 scope 未完成退出 → `IUnitOfWorkTransactionAbortable.AbortPendingTransaction()` 终结事务（仅 EF 实现）；对象释放归 DI 容器；手动构造路径由 Manager 直接 `Dispose`。
- 未完成退出的残留：abort 只终结事务句柄，**不丢弃 ChangeTracker 中未 flush 的跟踪状态**；同一请求 scope 内连续顶层 UoW 复用同一 scoped UoW/DbContext 实例，残留跟踪写入可被下一次 UoW 的 SaveChanges 带入。

### 1.4 基线缺口清单（本项要解决）

| # | 缺口 | 证据 |
| --- | --- | --- |
| G1 | Options 声明未生效且双定义：`Data.Abstractions.UnitOfWorkOptions`（IsolationLevel/Timeout/过滤器开关）零消费者；`Aop.Abstractions.Options.UnitOfWorkOptions` + `AopOptions.UnitOfWork` 零读取；`IUnitOfWorkEnhanced`（含 GetRepository/过滤器/TenantId 切换）**无实现、无消费者** | 全仓 grep 仅命中定义文件 |
| G2 | 三个入口六处近似 begin/commit/flush/rollback 逻辑；AOP 自维护 scope 栈；同步与异步 Execute 两份实现 | §1.2 |
| G3 | 无状态机：重复完成、终态后操作、乱序释放未定义（部分由 Provider 抛异常，部分静默） | Manager/EfCoreUnitOfWork 无状态字段 |
| G4 | Required 复用无 rollback-only：内层失败被业务捕获后，外层照常提交（部分失败静默提交） | AOP OnException 只处理 owner |
| G5 | 复用参数不诊断：Required 时 provider/isTransactional 参数被**静默忽略** | `UnitOfWorkManager.BeginScope` 复用分支 |
| G6 | 无取消/超时且无异步开始阶段：`IUnitOfWork` 无 CancellationToken；Begin 不传隔离级别；ExecuteAsync 不接收 CT；入口为同步 BeginScope + sync-over-async | `Domain.UnitOfWork.IUnitOfWork`、`EfCoreUnitOfWork` |
| G7 | 未完成退出不丢弃未 flush 跟踪写入；连续顶层 UoW 可能带入上一逻辑 UoW 的残留 | `AbortPendingTransaction` 仅回滚事务 |
| G8 | Ambient 契约为 `public static object? Current` + `Push(object)`（非强类型、可被任意调用方操作）；且无受管链/宿主身份——同一执行流内独立 DI scope 的读方可能读到他人帧，跨 scope 污染无机制阻止 | `UnitOfWorkAmbientContext.cs` |
| G9 | 故障结果四分混淆：TryRollback/TryAbort 吞清理异常；通知失败「先吞后写控制台」；发布阶段异常（如 CT 取消）会被 `catch → Rollback` 包装成「回滚」——**提交已成功后的通知失败可能被报告为失败/回滚**；提交结果未知无法表达；释放后无结果可读 | `EfCoreUnitOfWork.CommitTransactionAsync`、Manager TryRollback |
| G10 | Provider 能力只有 `SupportsRequiresNew` 一个布尔；隔离级别/超时/及时终止/丢弃能力未声明，无法「执行前失败」；Provider Begin 无强类型有效选项（隔离级别无传递通道） | `UnitOfWorkProviderBinding` |
| G11 | 无事务资源身份模型：Provider 相同即视为可复用；同一 EF Core 的不同 Context/连接/租户库无法区分；校验点不存在（更谈不上独立于 ambient 路由） | 复用判定只看 ambient 有无 |
| G12 | CAP 交接面不存在：`RegisterWithDistributedTransaction` 扩展无消费者；无「当前 Connection/Transaction 身份、所有权、存活范围」契约；`IDataBaseTransaction` 公开暴露 Commit/Rollback/Dispose，消费端仍可自行完成事务 | `UnitOfWorkIntegrationExtensions.cs`、`IDataBaseTransaction.cs` |

---

## 二、公开契约清点与处置

| 契约/类型 | 当前实现/消费者 | 处置 |
| --- | --- | --- |
| `Domain.UnitOfWork.IUnitOfWork`（Begin/Commit/Rollback/SaveChanges/Dispose） | 三个入口、Provider 实现、测试 | **保留并迁移签名**。保持最小（Provider 级句柄操作）；Begin 改为接收**强类型有效选项 + CT**（隔离级别/截止时间由内核解析后传入）；生命周期/状态由平台 scope 承担 |
| `IUnitOfWorkManager` | AOP、生成运行时、测试、AOT fixture | **保留并收口**：`BeginScope`（同步建立受管载体/激活）+ `StartAsync`（异步开始）+ `Execute/ExecuteAsync`（内核）；不提供异步返回 scope 的单步开始（违反调用方帧激活协议，见 §4.1）；签名迁移为 options 形态 |
| `IUnitOfWorkScope` | AOP、生成运行时、测试 | **保留并升级为状态化 scope**：`State`、`TransactionOutcome`、`NotificationOutcome`、`IsReleased`、`ExecutionToken`、`StartAsync`、`CompleteAsync`、`RollbackAsync`；join 成功完成 = 记录参与者成功（见 §4.2） |
| `IUnitOfWorkFactory` + `UnitOfWorkProviderBinding` + `Registry` | Provider 包声明、Manager | **保留**；Binding 扩展能力声明、资源键委托与 CAP lease 提供者（见 §4.4/§4.5） |
| `UnitOfWorkRegistrationState` | 注册扩展 | 保留（内部装配状态） |
| `UnitOfWorkAmbientContext`（`public static object` Current/Push） | 写方仅 Manager；读方 EF 适配器/默认 DbContext/测试 | **替换为强类型受管访问器 + 受管链身份**（链节点 descendant-or-self 可见性，见 §4.3）。旧公开 Push 归档 |
| `IUnitOfWorkTransactionAbortable` | Manager 调用、EF 实现 | **迁移**为「未完成退出终结 + 丢弃未 flush 跟踪写入」契约（见 §4.3） |
| `UnitOfWorkMoAttribute` | 手写服务、生成 CRUD、DynamicApi 生成器读取 | **契约保留**（属性签名与语义不变）；实现迁移到内核（AOP 只保留传递 scope 句柄的最小栈） |
| `DynamicApiGeneratedRuntime.ExecuteAsync` | 生成端点（生成器契约） | **签名保留**（生成器测试依赖）；实现迁移到内核 |
| `Data.Abstractions.UnitOfWorkOptions` | **零消费者** | **替换**为唯一生效的 options 类型（同名同位置，含 null 继承规则，见 §4.1）；过滤器开关/软删除等成员删除 |
| `IUnitOfWorkEnhanced`（GetRepository/EnableSoftDeleteFilter/SetTenantId 等） | **无实现、无消费者** | **归档**（`99_RecycleBin/issue-137-...`）。仓储获取走 DI 注入；TenantId 走 `ICurrentTenant` 平台能力；过滤开关走现有过滤注册/IgnoreQueryFilters |
| `Aop.Abstractions.Options.UnitOfWorkOptions` + `AopOptions.UnitOfWork` | 零读取 | **归档**（不迁移；AOP 不再持有独立 UoW 配置） |
| `Data.Abstractions.RepositoryBase.Repository<,>`（构造期捕获 DbSet/QueryableBuilder） | **零消费者**（生成仓储走 `CrestRepositoryBase`） | **归档**（跨 UoW 缓存资源对象的反面样本，防止被误用） |
| `IUnitOfWorkManager.Begin(OrmProvider?)` + `ScopedUnitOfWorkProxy` | 测试、AOT fixture（旧式单 UoW API） | **归档**；调用方迁移到 `BeginScope + StartAsync`（测试/fixture 同步迁移） |
| `UnitOfWorkIntegrationExtensions` / 内部 `UnitOfWorkTransactionParticipant`（DistributedTransaction 包） | 零消费者 | 不在 #137 重开；由 §4.5 CAP 最小契约替代，处置记录交 #125（本项负责交接文档与最小契约） |
| FreeSql `TransactionalAttribute` / `FreeSqlUnitOfWorkManager` | FreeSql 包内部 | 保留（Provider 内部路径），能力声明进 Provider 矩阵 |
| `UnitOfWorkWithEvents`（Provider 基类） | EF/FreeSql UoW | 保留；生命周期顺序与故障语义按 §4.5 固定 |
| `UnitOfWorkProviderBindingRegistry.ResolveProvider` 默认选择规则 | 装配 | 保留（#124 已裁定） |

---

## 三、资源所有权表与状态/传播表

### 3.1 资源所有权表

| 资源 | 创建者 | 所有者（释放责任） | 释放时机 | 重复释放 | 未完成退出路径 |
| --- | --- | --- | --- | --- | --- |
| 调用方 scope UoW（顶层，绑定路径） | DI 容器（从调用方 scope 解析） | 调用方 DI scope | 请求 scope 结束（容器一次释放） | 容器保证一次 | 内核**只终结事务**（回滚+释放事务句柄），不释放对象 |
| requiresNew 子 UoW + 子 DI scope | 内核（受管子 scope） | 子 DI scope（内核释放子 scope 一次） | scope 退出（正常/异常/未完成统一） | 内核幂等 | 子 scope 释放即回收对象与 context |
| 手动构造路径 UoW（自定义工厂/单测） | Manager（经 factory） | Manager | scope 退出 | 幂等 | Manager 直接 Dispose（既有语义保留） |
| 事务句柄（IDbContextTransaction 等） | UoW | UoW 实例 | Commit/Rollback/Abandon 任一，一次 | no-op | Abandon 回滚并释放句柄 |
| Ambient 帧（Context + 资源键 + 链节点 + 租户键） | 内核（隔离 scope 开始时 Push） | 内核 | 隔离 scope 退出，**先于**资源释放恢复 | 仅栈顶可恢复；乱序不允许回退他人环境 | 异常路径与正常路径同一恢复点 |
| 未 flush 跟踪状态（ChangeTracker/pending writes） | context | UoW/context | 提交后自然持久化；未完成退出必须**丢弃未 flush 部分**（新增能力，见 §4.3） | 丢弃幂等 | 与 Abandon 一体；**已 flush 的写入不在丢弃范围**（§4.5 非事务语义） |
| 域事件队列（实体 DomainEvents） | 实体/UoW | UoW | 发布成功后清除；发布失败保留至可检查结果（不得静默吞掉） | — | 回滚路径不发布 |
| 事务结果 / 通知结果记录（不可改写） | 内核 scope | 内核 scope（对象生命周期内可读） | scope 对象回收；**释放后仍可读最终结果** | 只读 | `Unknown` 不因回滚尝试改写（§3.2） |
| CAP 参与 lease（受限借用视图） | Provider（内核窗口内按需签发） | 内核 scope（窗口所有权） | scope 终态/释放即失效 | 幂等失效 | 失效后拒绝继续参与（§4.5） |

### 3.2 状态模型（参与者完成 × 事务结果 × 通知结果 × 释放，四维分开记录）

公共 `State`（参与状态）：

| 状态 | 含义 | 允许的后续操作 | 重复/越界行为（裁定） |
| --- | --- | --- | --- |
| `Active` | 已开始；事务（若声明）已打开或按非事务模式运行 | `CompleteAsync`、`RollbackAsync`、`Dispose` | — |
| `Completed` | 成功完成**已记录**：owner = 数据库 commit 已确认（非事务 = flush 成功）后**立即**记录；join = 参与者成功已记录 | 只读；`Dispose` | 再次 `CompleteAsync` = **幂等 no-op**；`RollbackAsync` = 确定性拒绝 |
| `RolledBack` | 确定回滚完成（仅来自未提交状态） | 只读；`Dispose` | 再次 `RollbackAsync` = 幂等 no-op；`CompleteAsync` = 确定性拒绝 |
| `Failed` | 终态结果**不确定**（如提交结果未知、回滚自身失败）；携带可检查诊断 | 只读；`Dispose` | 一切完成/回滚调用 = 确定性拒绝 |

释放标志（**独立于 `State` 存储**，实现按 `IsReleased` 处理；释放不覆盖已记录的参与状态与事务结果）：
- 资源已释放（对象/子 scope/事务句柄），环境已恢复；**最终事务结果与通知结果仍可读**（如 `Completed+Committed+NotificationFailed` 组合释后仍可读）。
- `IsReleased=true` 后任何完成/回滚/使用操作 = 确定性拒绝（`ObjectDisposedException` 语义）。

事务结果（owner 专用，**不可被后续操作改写**）：`NotStarted → Committed | RolledBack | Unknown`。
- `Committed` 在数据库 commit 确认时**立即置位（早于通知与清理）**；之后的通知/清理/释放失败均保留已提交事实。
- `Unknown`（提交派发期失败、响应丢失等）：允许尝试回滚以清理本地状态，但**回滚尝试不得把结果改写为 `RolledBack`**；结果持续为 `Unknown` 直到有新证据。

通知结果（独立可查）：`None | Succeeded | Failed`。通知失败不改变事务结果，不触发回滚，不自动业务重试。

规则：
- **join 完成 = 记录参与者成功**（不 flush、不 commit、不释放外层）；join 成功完成后的 Dispose 不污染外层。
- **join 未完成退出**（Active + Dispose，或显式 `RollbackAsync`）= 标记 owner rollback-only；owner 完成时**确定性拒绝并回滚**（附原因），不得静默提交部分失败。
- **rollback-only 校验在任何完成期 flush 之前**执行；被标记后不得再 flush。
- **乱序释放**：非栈顶 scope 释放 = 确定性拒绝（保留 #136 既有 `The unit of work scope was disposed out of order` 语义）。
- **显式 Dispose 的清理失败（无原始业务异常）**：产生专门的可检查结果（含清理阶段信息），不以「仅日志」作结。
- 事务结果与参与状态分开读取：`Completed + TransactionOutcome=Committed + NotificationOutcome=Failed` 是一个合法且必须可表达的终态组合。

### 3.3 传播表

| # | 环境 | 请求 | 结果 | 诊断/裁定 |
| --- | --- | --- | --- | --- |
| 1 | 无 | Required + 事务 | 新顶层 scope；Begin；Complete = 校验→flush→commit（确认即记录）→通知→清理 | — |
| 2 | 无 | Required + 非事务 | 新顶层 scope；无事务；Complete = 校验→flush→通知→清理 | 逐写自动 flush、已落库写入不可撤销（§4.5 非事务语义） |
| 3 | 无 | RequiresNew | 与 #1/#2 相同的顶层新建（无环境时退化新建，确定性） | — |
| 4 | 有 | Required + 参数一致 | **join**：IsOwner=false；Complete **记录参与者成功**；不 flush/commit/释放外层；成功后 Dispose 不污染外层 | null 参数继承规则见 §4.1 |
| 5 | 有 | Required + 参数冲突（事务开关 / 显式 provider / 显式隔离级别 / 显式截止时间超出外层剩余） | **执行前确定性失败**，不静默降级 | 消息含内外参数与修正指引 |
| 6 | 有 | RequiresNew + Provider 不支持 | 执行前 `NotSupportedException`（保持 #124 fail closed） | — |
| 7 | 有 | RequiresNew + 支持 | 子 scope 隔离：独立资源/事务；内层 Complete 独立提交；外层回滚**不撤销**已提交内层 | 文档明确 |
| 8 | join | 失败（无论外层是否捕获）或未完成退出 | 外层标记 rollback-only；外层 Complete = **确定性拒绝 + 回滚**（不静默提交部分失败）；已 flush 的写入不被声称撤销 | 终态异常携带原因 |
| 9 | 任意 | 取消（CT 触发） | 合作式取消：action 获 `OperationCanceledException` → 未提交状态回滚（清理用独立 token）→ 原 OCE 上抛；**不得报告成功**。非事务模式：已落库写入保持，结果不得声称全部丢弃 | 清理不被取消跳过 |
| 10 | 任意 | 超时（Options.Timeout 到期） | 内核合作式截止时间：commit 前到期 = 取消语义 + 回滚；commit 派发期失败 = `Unknown`（§3.2）；不得报告成功 | join 只能收紧（min），不得延长 |

---

## 四、设计裁定（To-Be）

### 4.1 统一 Options、唯一异步执行内核与 token 通道

- 唯一 options 类型（替换 `Data.Abstractions.UnitOfWorkOptions` 死定义）：

  ```
  UnitOfWorkOptions {
      bool IsTransactional = true;                    // 事务开关
      UnitOfWorkPropagation Propagation = Required;   // Required | RequiresNew（其他值 fail closed）
      IsolationLevel? IsolationLevel;                 // null=继承/默认（见下表）
      TimeSpan? Timeout;                              // 内核合作式截止时间（禁止声明未生效）
      OrmProvider? Provider;                          // null=继承/按默认解析规则（见下表）
  }
  ```

- **null 与显式参数的继承/校验规则**（消除普通嵌套误判，同时禁止忽略参数）：

  | 参数 | join（有环境） | 新建（无环境） |
  | --- | --- | --- |
  | `Provider = null` | **继承外层**（不重新解析宿主默认） | 按 #124 默认解析规则选择 |
  | `Provider` 显式 | 必须与外层解析结果一致，否则执行前诊断 | 使用指定值（受能力校验） |
  | `IsolationLevel = null` | **继承外层实际级别** | Provider 默认（记录为「未指定」） |
  | `IsolationLevel` 显式 | 必须等于外层实际级别，否则执行前诊断 | 能力校验通过后使用 |
  | `Timeout = null` | 继承外层剩余期限 | 无额外截止 |
  | `Timeout` 显式 | ≤ 外层剩余：收紧生效（取 min）；> 外层剩余：**确定性诊断**（不可延长） | 作为本次截止 |
  | `IsTransactional` | 必须与外层一致，否则执行前诊断 | 按声明 |

- **环境激活/恢复协议（调用方可见性，R2-S137-01）**：AsyncLocal 写入不会从 async 方法体内反向传播到调用方（.NET 10 最小复现：`await BeginScopeAsync()` 返回后调用方仍见旧值）。因此：
  - **激活与恢复必须发生在调用方同步帧**。受支持的开始形态为「同步建立受管载体 + 异步 start」：`var scope = manager.BeginScope(options)`（同步方法，在调用方帧内完成链节点/受管载体登记与 ambient 激活；本身不做 I/O、不做 sync-over-async）→ `await scope.StartAsync(ct)`（异步打开 Provider 事务）。标准写法：
    `await using var scope = manager.BeginScope(options); await scope.StartAsync(ct);`
  - `Task`-返回的方法只有**非 async 实现**（同步段在调用方帧执行）才满足协议；`Dispose/DisposeAsync` 的**环境恢复段必须同步完成于调用方帧**（`DisposeAsync` 以非 async 方法返回异步清理 Task；清理不得被跳过）。
  - **委托式入口**：`ExecuteAsync` 把 action 运行在**内核异步帧内**（执行上下文向下继承生效），是普通业务的首选入口；AOP 采用**包裹式拦截**（被拦截方法体在内核帧内执行；Rougamo RawMo 或等价机制，切片 1 确认具体 API），禁止「OnEntry 写 AsyncLocal 后依赖调用方继承」的旧假设。
  - **构造/开始失败恢复**：失败路径由 using 语义触发调用方帧恢复；内核同时兜底清理资源并登记结构化诊断。调用方遗漏 Dispose 时，残留帧进入终态：读方/内核对终态帧一律**确定性拒绝**（不得静默路由），给出明确诊断与修复指引。
  - 载体是执行流帧（per-EC），不引入可变共享对象重新造成跨调用串用；`Task.Run` 等继承执行上下文的任务仍受 descendant-or-self 读方校验约束。
- **联动 token 通道（R2-S137-03）**：
  - 委托式入口：`ExecuteAsync<TResult>(Func<CancellationToken, Task<TResult>> action, UnitOfWorkOptions?, CancellationToken)`——action 接收内核联动 token（调用方 CT + 截止时间）。
  - 生成端点的调用方 CT = `HttpContext.RequestAborted`；生成器把**内核联动 token** 传入服务方法的 CT 参数（RequestAborted 作为调用方 CT 参与联动，不再被直接透传）。
  - AOP 与手写路径（方法签名不可重写的边界）：**操作层组合为主机制**——正式仓储/Provider 操作在发起数据库动作时，把「传入 CT」与「当前受管执行 token（链校验后）」组合为有效 token（`CreateLinkedTokenSource` 语义），使 AOP 方法、无 CT 参数调用与 RequestAborted 场景都获得内核截止时间覆盖；编译期、无反射、业务无额外仪式。
  - 读取「受管执行 token」必须经受管载体（调用方帧激活、终态帧拒绝），不得用未校验的全局静态。落地分阶段：**切片 1 随激活协议落地支撑 AOP deadline 的最小操作层组合与安全读取校验（该主链能力在切片 1 独立成立）**；切片 2 把读取升级为受管链校验访问器并完善资源准入与仓储覆盖。
  - 支持边界（文档明确）：方法体内**非仓储/Provider 的等待**（如 `Task.Delay`）仅观察其入参 CT；缺少 CT 参数时不承诺被单独取消，但经正式仓储/Provider 的数据库动作始终受组合 token 覆盖。不把「存在公开 `ExecutionToken`」当作业务操作已接通。

- **Provider begin 的强类型有效选项**：内核把 options 解析为 effective begin options（`IsolationLevel`、截止时间）随 CT 传入 Provider `BeginTransactionAsync(effective, ct)`；Provider 不从环境读取配置。
- **合作式语义声明**：截止时间/取消是**合作式**的——内核不强行中断不观察 token 的 Provider 操作；`Task.WhenAny` 超时后直接释放资源不构成取消保证（禁止该实现方式）。commit 已确认与取消竞态的判定按 §3.2/§4.5（可能 `Unknown`）。
- 同步 `Execute<T>` = 异步内核的同步等待包装（唯一实现）；AOP/生成运行时/管理 API/同步包装全部经内核，**任何入口不得自实现 begin/commit/flush/rollback 顺序**。
- 迁移方式：旧签名调用方全部在仓库内（AOP、生成运行时、测试、AOT fixture），**直接迁移不保留双签名**。`[UnitOfWorkMo]` 属性**声明方式**不变；**生成器/生成调用胶水需要按联动 token 协议迁移并回归验证**（生成端点调用代码改传内核联动 token；生成器测试同步更新），不得漏改生成端点。

### 4.2 完成语义（与 §3.2 状态模型逐一对应）

- 状态机由平台 scope 承载（§3.2）；Provider UoW 保留句柄级确定性拒绝（重复 Begin、终态后 Commit 等）。
- `CompleteAsync`（owner 且事务）：**rollback-only 校验（先于任何 flush）** → flush/校验 → 数据库 commit → **立即记录 `Committed`** → 通知 → 清理（释放事务句柄）→ 释放后结果仍可读。
- `CompleteAsync`（owner 非事务）：rollback-only 校验 → flush → 通知 → 清理；已落库写入不可撤销（§4.5）。
- `CompleteAsync`（join）：**记录参与者成功**（幂等）；不触碰外层；join 失败由内核在异常路径标记外层 rollback-only。
- `RollbackAsync`：仅 owner 生效；未提交状态回滚 + 丢弃未 flush 跟踪写入 + 清理；join 调用 = 标记外层 rollback-only（由内核异常路径调用）。对 `Committed` 结果调用 = 确定性拒绝（保留已提交事实）。
- `SaveChanges`（flush）与事务成功完成分开定义：flush 不结束事务、不发布通知；commit 内含 flush；**rollback-only 校验先于任何完成期 flush**。
- 顶层 scope 未完成退出：回滚 + 丢弃 + 释放（不等请求 DI scope 结束）；**未 flush 跟踪状态不得带入同一请求内的下一个顶层 UoW**（§4.3 能力）。
- 原业务异常保留：回滚/释放失败作为**可检查次级结果**（专门异常/诊断对象，含阶段信息），不得替换根因；无原始异常时的清理失败同样必须可检查。
- 释放协议（与 §4.1 激活协议成对）：环境恢复段在**调用方帧同步完成**；`DisposeAsync` 以非 async 方法返回异步清理 Task（清理不跳过）；未完成 Dispose 的残留帧为终态（读方确定性拒绝，不静默串用）；异步释放纳入正式契约（同步 Dispose 保留兼容）。

### 4.3 资源归属、受管链身份与仓储参与

- **强类型受管 Ambient 访问器**（替换 `public static object? Current/Push`）：
  - 公开只读：强类型 `IDataBaseContext?`；写路径（Push/Restore）内部化（仅内核可操作）。
  - 帧内容：Context、逻辑资源键与物理实例身份（见下）、owner 链节点。
- **受管链身份（跨 DI scope 污染的可实现机制）**：
  - 内核为每个**受管 scope** 创建链节点（唯一 ID + 父节点引用）；内核创建的隔离子 scope 节点挂到当前节点下；**非内核创建的普通 DI scope 是独立根**（无父链）。
  - **读方可见性 = descendant-or-self**：读方（适配器/DbContext）以其所在 scope 的链节点解析 ambient；仅当帧 owner 节点等于读方节点或是其后代时帧可见。这同时满足两个方向：
    - 子树内预注入父仓储**跟随**隔离子 UoW（子节点是父节点后代）；
    - 同一执行流内独立 scope（宿主 B / 兄弟 scope）**绝不**看到 A 的帧（非后代）——不依赖「新执行流」假定；`CreateScope` 不创建新执行上下文也不影响该规则。
  - **Manager 与访问器共享权威**：内核 push/restore 时校验帧 owner == 当前受管 scope；受管链内 `Manager.Current` 与仓储实际资源必须同源；不一致 → 确定性诊断（不得静默）。
  - 独立 scope 进入/退出不遮蔽他人帧（其读不到）；内核子 scope 退出按帧链恢复；乱序恢复不允许回退他人环境。
  - `Task.Run` 等执行上下文继承：可见性仍由读方链节点决定；同一受管链内「不支持并行数据库操作」的边界不变（不因 AsyncLocal 检查放宽）。
- **逻辑资源键与物理实例身份（R2-S137-02）**：
  - **逻辑资源键**（选择/准入，稳定声明）：（Provider 绑定身份，Context 声明/连接配置身份，TenantId）。
  - **物理实例身份**（本次 UoW 实际对象）：Context 实例 / Connection 实例 / Transaction 实例——记录在帧内，用于诊断与 CAP 事务身份，**不得被逻辑键替代**。
  - **准入规则（同一校验机制，不做特殊分支）**：读方仅可跟随「帧 owner 在受管链上为自身后代（descendant-or-self）**且**帧的逻辑资源键 == 自身逻辑资源键」的帧。受管 RequiresNew 是**同一逻辑资源的新物理实例**（子 scope 按相同绑定/声明/租户构造新 Context/Connection），父仓储因此合法跟随；同链同租户但**不同 Context 声明/连接配置**（第二资源）→ 确定性拒绝；TenantId 不一致 → 确定性拒绝。
  - **校验时点**：内核在创建 scope 时以请求方当前上下文（重定向前）计算逻辑键并与外层比对；读方（适配器）解析帧时**先校验后路由**。需要第二资源必须新建 UoW。
- **构造不变量**（保留 #136）：隔离层先 Push 再构造 UoW；新建 UoW 绝不因父环境路由被重定向到旧 Context/连接。
- **未完成退出 = 事务终结 + 丢弃未 flush 跟踪写入**：迁移 `IUnitOfWorkTransactionAbortable` 语义（回滚句柄 + 丢弃未 flush 跟踪写入；EF 实现 `ChangeTracker.Clear()`）。已 flush 的写入不在丢弃范围。不支持该能力的 Provider 必须在绑定中声明，并按 Provider 矩阵落位（实现或明确拒绝），不得静默残留。
- **仓储参与规则**：
  - 正式仓储在**操作时**解析当前上下文（现状已满足）；构造期缓存 DbSet/Queryable/原生 Context 的仓储基类已归档（§二）。
  - 直接注入原生 DbContext（非 `IDataBaseContext` 通道）与预缓存的 DbSet/IQueryable 不承诺跟随 requiresNew——限制写入文档与测试断言（负例）。
  - 一个 UoW 内不支持并行数据库操作（单 context/事务，非线程安全）；AsyncLocal 为执行流语义——不得据此声称 DbContext 并发安全。
- **覆盖要求**：多层 RequiresNew、await 续接、成功/异常退出后的父环境恢复（既有 2 层用例 + 新增续接与恢复矩阵）。

### 4.4 Provider 能力与资源身份

- Binding 能力声明（强类型，装配期由 Provider 包声明，内核在**执行业务前**校验）：

  | 能力 | 说明 | EF Core | FreeSql | SqlSugar |
  | --- | --- | --- | --- | --- |
  | SupportsTransactions | 本地事务 | 是 | 是（SDK） | 是（SDK Ado） |
  | SupportsRequiresNew | 隔离子 scope + 环境跟随 | 是（已验证） | 否（fail closed） | 否（fail closed） |
  | SupportedIsolationLevels | 显式隔离级别集合 | **逐项由实际数据库/驱动判定**（SQLite/PG 各自矩阵，验收实测），不笼统宣称「标准级别透传」 | 按实现声明或拒绝 | 按实现声明或拒绝 |
  | SupportsTimeout | 内核截止时间之外的 Provider 级超时（如有） | 无额外声明（内核合作式截止时间生效） | 声明或拒绝 | 声明或拒绝 |
  | PromptTerminationOnAbandon | 未完成退出及时终结事务 | 是（#136） | 实现或声明拒绝（计划切片 3） | 实现或声明拒绝（计划切片 3） |
  | DiscardUncommittedOnAbandon | 未完成退出丢弃未 flush 跟踪状态 | 是（新增） | 声明或拒绝 | 声明或拒绝 |
  | CAP lease 提供者 | §4.5 最小参与的 Provider 所有实现 | 是（计划切片 5） | 声明或拒绝 | 声明或拒绝 |

- **资源身份**：准入判定以**逻辑资源键**（§4.3：绑定身份/Context 声明/连接配置/租户）为准，不以 `OrmProvider` 相同作为唯一依据；**物理实例身份独立记录**（Context/Connection/Transaction 实例，供诊断与 CAP 使用，不被逻辑键替代）；校验在 ambient 重定向**之前**完成（内核侧以请求方当前上下文计算；读方侧先校验后路由）；未指定参数按 §4.1 继承规则处理，不因 null 误判冲突；第二资源（第二 Context 声明/连接配置/租户库）确定性拒绝。
- **不支持的能力在业务执行前失败**：显式 isolation/requiresNew/timeout 与能力不符 → 确定性异常（含 Provider 与能力矩阵摘要），不静默降级。
- **范围裁定**：只承诺单数据库资源的本地事务；多 DbContext 共享事务（如纳入）需真实数据库证明；跨库/跨 ORM 原子提交**明确拒绝**——不提供「依次 Commit 即原子」的聚合器。
- EF Core 用真实 PostgreSQL（Testcontainers、独立 schema，CI 覆盖）+ SQLite 快速回归验收，隔离级别按实测过项逐项声明；FreeSql/SqlSugar「实现已声明能力或明确拒绝」；外部 SDK AOT 边界留 #130。
- 装配仍为显式强类型绑定；能力校验失败在同一次装配/执行路径确定性诊断。

### 4.5 职责收口、生命周期顺序、故障语义与 CAP 交接

- `IUnitOfWorkEnhanced` 职责收口（档案化，§二）：仓储获取（DI 注入）、TenantId 切换（`ICurrentTenant`）、数据过滤开关（现有过滤注册/`IgnoreQueryFilters`）不进入 UoW；无兼容 shim（零消费者）。
- **固定生命周期顺序**：`rollback-only 校验（flush 前）→ flush/校验 → 数据库 commit →【立即记录已提交】→ 已提交通知 → 清理`。回滚路径不发布成功事件。非事务模式：flush 成功后再通知。
- **非事务模式语义（裁定）**：
  - 保留正式仓储**逐写自动 flush**（现状行为）；因此非事务模式**不提供整体原子性**：已持久化写入不可回滚；失败/取消结果**不得声称全部丢弃**。
  - 需要原子回滚的业务必须请求受支持的事务模式；rollback-only 标记在非事务 join 中同样生效（外层拒绝继续完成），但拒绝只阻止后续完成期 flush，不撤销已落库写入。
- **故障分层（结果可检查，与 §3.2 四维对应）**：

  | 故障点 | 事务结果 | 参与状态 | 规则 |
  | --- | --- | --- | --- |
  | 提交前 flush/校验失败 | NotStarted → 回滚尝试 | `RolledBack`（回滚成功）/`Failed`（回滚失败） | 原异常上抛；非事务模式：已落库写入保持 |
  | 数据库 commit 派发失败/响应丢失 | **`Unknown`**（回滚尝试成功也不改写） | `Failed` | 原异常上抛 + 「提交结果未知」可检查标记；不得谎称已回滚 |
  | 数据库 commit 已确认 | **`Committed` 立即记录** | `Completed` | 后续任何失败不改变该事实 |
  | 提交后通知失败 | `Committed` 保持 | `Completed` | 通知结果 `Failed`；可抛/记录「提交后通知失败」（携带已提交事实）；**不回滚、不谎称、不自动重试业务** |
  | 回滚/清理/Dispose 失败 | 保留现状位 | 视情形 `Failed` | 原业务异常保留；失败作为可检查次级结果（专门异常，含阶段）；无原始异常时同样可检查 |
  | 域事件发布失败 | 同「提交后通知失败」 | 同上 | 不再静默吞掉（移除 Console 吞异常）；事件队列清空条件 = 发布成功 |

- **CAP 最小交接契约**（本项设计 + 计划切片 5 实现，SDK 适配留 #125）：

  ```
  IUnitOfWorkTransactionLeaseProvider {          // Provider 所有；绑定内声明
      bool TryAcquireCurrentLease(out UnitOfWorkTransactionLease lease);
  }
  UnitOfWorkTransactionLease {                   // 受限「借用」视图，不是万能扩展点
      OrmProvider Provider;
      PhysicalResourceIdentity Resource;         // 物理实例身份：Context/Connection/Transaction 实例（§4.3；CAP 不得用逻辑键替代）
      IUnitOfWorkTransactionIdentity Transaction; // 只读身份/状态视图：TransactionId/IsCompleted 等
      bool IsValid;                              // live view：仅在活跃 scope 窗口内为 true
  }
  ```

  - **同一 Connection/Transaction 关联必须可确认**：lease 由 Provider 在受管链当前窗口内签发；资源键与事务身份一致才可参与；错资源 → 拒绝。
  - **生命周期操作仍由内核唯一掌握**：公开参与契约**不暴露** Commit/Rollback/Dispose；若原生 SDK 必须取底层事务对象，限定在 Provider 适配边界内借用，明确「不得 Dispose/Commit」，且该借用不经由公共契约。
  - **live view 语义**：lease 是实时视图（非快照），仅在所属 scope `Active` 且未释放时有效；scope 终态/释放后 `IsValid=false`，继续参与 → 确定性拒绝；不允许持有到 scope 之外。
  - 规则（交接文档约束）：CAP 消息记录必须使用**同一资源/事务**在 commit 前落库；谁负责开始/提交/回滚 = UoW 内核；提交后通知**不证明**消息持久化原子性；不得从生命周期 hook 推导 exactly-once/2PC/durable outbox；不新增万能公开扩展点、不引入 CAP 依赖。
  - `RegisterWithDistributedTransaction` 死扩展的处置记录在交接文档中交 #125。

### 4.6 不做的事（与 Issue「不在本项范围」一致）

CAP storage/transport 配置与 SDK 原子发布验证（#125）；ORM SDK 全量支持矩阵（#130）；通用分布式事务引擎、2PC、Saga、跨库自动协调、自动业务重试、MongoDB UoW、新 ORM Provider；#87/#88 已裁定的业务部署/激活事务不重开。

---

## 五、测试推导（由表驱动，不以当前实现自证）

原则：每条验收判据对应契约表行；新增回归测试须**判别力验证**（临时回退修复 → 测试转红 → 恢复）。

| 契约（§三/§四） | 测试计划 | 判别力验证 |
| --- | --- | --- |
| join 参与者模型（3.2/3.3 #4/#8） | 外层 → 两个成功 join 各 Complete+Dispose → 外层成功提交；join 未 Complete 即 Dispose → 外层拒绝并回滚；join 失败被捕获 → 外层拒绝并回滚（DB 无部分写入） | 去除参与者成功记录/rollback-only 标记 → 对应用例红 |
| 事务结果四维（3.2/4.5） | commit 成功但响应失败（注入）→ 结果 `Unknown` 且回滚尝试不改写；通知失败 → `Committed+Completed` 保持且可检查；提交后 Dispose 失败 → 可检查次级结果；**释放后仍可读取最终事务结果** | 将 Committed 记录放回通知之后 → 通知失败用例红 |
| 非事务语义（3.3 #2/#8/#9） | 正式仓储非事务写 A → 后续失败/取消：检查 A 的真实数据库可见性与结果声明（不声称丢弃）；Complete 前 rollback-only → 不触发 flush（以写入探针断言） | 把非事务描述回「回滚」→ 用例红 |
| 状态机（3.2） | 重复 Complete 幂等；终态后 Rollback/Complete 确定性拒绝；乱序释放拒绝；Active+Dispose（owner/join 分化行为） | 去除状态守卫 → 对应用例红 |
| 异步激活协议（4.1/R2-S137-01） | 强制 Provider Begin 真正异步挂起（Task.Yield/Delay）后：调用方 `await` 返回后 Current、预注入仓储、事务物理身份一致；两层 RequiresNew；异步 Dispose 后父环境恢复；begin 失败恢复；**AOP 返回后的业务体实测**（不只 callback 场景）；遗漏 Dispose → 终态帧确定性拒绝 | 回退为「async 方法内写 AsyncLocal」→ 直接开始用例红 |
| 受管链隔离（4.3） | 同执行流 host A / host B：A.RequiresNew 内新 DI scope 的 B.Manager/Repository（Current 与仓储资源一致、无串用）；执行上下文继承任务（Task.Run）；受管子 UoW 仍服务预注入父仓储 | 去掉 descendant 校验 → 串用用例红 |
| 逻辑/物理资源身份（4.3/4.4/R2-S137-02） | **同一校验机制四向**：受管子 UoW 中父仓储使用新物理实例 B 成功；同链同租户不同 Context 声明 C 拒绝；不同连接配置拒绝；独立 DI 根不可见帧 | 逻辑键含物理实例 → B 跟随用例红 |
| 选项继承（4.1） | 外层显式非默认 Provider/隔离 + 内层未指定参数 → 继承（不误判冲突）；外层 tenant A 内切 tenant B 后首次仓储访问 → 拒绝 | 继承规则改「重新解析默认」→ 误判用例红 |
| 取消/超时通道（4.1/3.3 #9/#10/R2-S137-03） | AOP 方法：调用方 CT 不取消、UoW deadline 到期、方法把 CT 参数传给等待取消的数据库动作 → 收到取消 + 清理完成；无 CT 参数的正式仓储同样受组合 token 覆盖；RequestAborted 与 deadline 并存；join 更短 deadline 生效、更长被诊断；commit 前/派发中/成功后取消（成功后取消不得报告失败回滚） | 移除操作层 token 组合 → AOP 用例红 |
| Options 生效（4.1） | 非事务模式不提交只 flush；显式隔离级别按实测矩阵生效（真实 DB）；能力不符执行前失败（动作未执行断言） | 跳过校验 → 对应用例红 |
| 入口统一（4.1） | `[UnitOfWorkMo]` 成功提交/失败回滚集成用例（真实 DB）；生成 DynamicAPI 端点事务用例；两入口复用同一内核（AOP/生成运行时不出现自己的 begin/commit 实现——源码守卫测试） | 让 AOP 绕开内核 → 守卫测试红 |
| 故障分层（4.5） | 提交前失败/commit 失败/提交后通知失败分别断言：原异常保留、四维结果正确、提交后通知失败**不得**声称回滚、无自动业务重试；生命周期顺序（校验→flush→commit→notify→cleanup）观测用例 | 各故障注入分支判别力验证 |
| Provider 能力/身份（4.4） | FreeSql/SqlSugar 声明矩阵用例；执行前拒绝；EF 隔离级别逐项实测 | 跳过校验 → 对应红 |
| PostgreSQL 真实验证 | Testcontainers PG（独立 schema）：外层事务开始后的嵌套隔离、提交后可见性、隔离级别行为（CI） | — |
| CAP lease（4.5） | 同一 Connection+Transaction 身份断言（**不止 TransactionId+bool**）；错资源拒绝；完成后/释放后失效拒绝；消费者不能经普通参与契约完成外层事务 | 去掉有效性校验 → 失效用例红 |
| 原生门禁 | 更新 `CrestCreates.Data.Abstractions.AotFixture`：传播/状态/资源身份/诊断场景；publish→native link→执行原生产物；证据含 SHA/RID/命令/日志 | 门禁失败传播保留 |
| 依赖边界/文档 | Boundary 测试保留；AGENTS.md/memory.md/Provider 矩阵/迁移说明同步 | — |

基线既有测试（28 项 OrmProviders + AOT fixture 6 场景 + Web.Tests 1 项）全部保留并按新契约迁移断言对象（状态机/内核 API）；不删除既有用例。

---

## 六、验收矩阵映射（Issue → 本设计 → 交付切片）

| Issue 验收判据 | 对应设计 | 切片 |
| --- | --- | --- |
| 默认 EF 装配 + 自定义 Adapter 由预注入正式 Repository 验证提交/回滚 | §4.3；既有 2 用例保留升级 | 2 |
| Required 复用/rollback-only/join 参与者完成/冲突参数 | §3.2/§3.3 #4/#5/#8 | 1 |
| 多层 RequiresNew | §3.3 #7（既有用例 + 续接变体） | 2 |
| 连续顶层 UoW 无残留 | §3.2、§4.3（含 discard 能力） | 1（状态）+ 2（discard） |
| 释放一次、事务及时结束 | §3.1 | 1 + 2 |
| 受管链隔离（跨 scope 不串用） | §4.3 | 2 |
| 非事务/隔离/取消/超时/故障结果 | §4.1、§4.5 | 1（内核）+ 3（Provider）+ 4（故障注入） |
| 多 Context / 换租户错误复用拒绝、Provider 能力执行前拒绝 | §4.3/§4.4 | 3 |
| 真实 PostgreSQL 嵌套隔离与提交后可见性 | §五 | 3 |
| `[UnitOfWorkMo]` + 生成 CRUD 成功/失败集成 | §4.1 | 1（机制）+ 4（集成） |
| commit 前后故障通知顺序与结果 | §4.5 | 4 |
| UoW NativeAOT fixture 更新（SHA/RID/命令/日志） | §五 | 4 |
| 依赖边界/文档/支持矩阵/99_RecycleBin 一致 | §二 | 各切片同步，4 终审 |
| 向 #125 交付交接契约与故障语义 | §4.5 | 5 |

---

## 七、风险与开放项

1. **FreeSql/SqlSugar 及时终止、丢弃与 lease 能力**：计划切片 3/5 落位「实现或明确拒绝」；若拒绝，矩阵与迁移说明必须显式（不得默认残留事务）。
2. **CAP lease 具体类型命名与形态**（`UnitOfWorkTransactionLease` 等）在计划切片 5 由评审确认后定型；本设计固定职责、所有权、live view 与失效语义。
3. **通知失败/清理失败的具体异常类型命名**在计划切片 4 定型；本设计固定「不回滚、不谎称、不自动重试、可检查」四原则。
4. **受管链节点的承载方式**（scoped 身份服务 vs 帧内嵌节点对象）在计划切片 2 定型；本设计固定 descendant-or-self 可见性与一致性校验语义。**激活协议的具体 API 形态**（`BeginScope` 返回未启动 scope + `StartAsync` vs 其他等价形态）在计划切片 1 定型；本设计固定「调用方帧同步激活/恢复」语义。
5. 旧测试/AOT fixture 的迁移面（`Begin()`、双入口实现、同步 BeginScope）在切片 1/4 内完成，迁移说明入 `99_RecycleBin` 记录。

---

## 八、审计响应记录（2026-10-10）

### 8.1 第一轮（rev.2）

对照 [第一轮 Spec 审计](../../review/2026-10-10-issue-137-uow-spec-review.md) 的逐项修订：

| 审计项 | 修订位置 |
| --- | --- |
| S137-01 [P1] join 完成必须记录成功 | §3.2（join = 参与者成功记录，与物理提交分开）+ §3.3 #4/#8 + §4.2 + §五（双成功 join / 未完成退出 / 失败被捕获三向用例） |
| S137-02 [P1] commit 结果、通知结果、释放状态混用 | §3.2 四维模型（参与状态/事务结果/通知结果/释放正交）；`Committed` 在 commit 确认时立即记录；`Unknown` 不被回滚尝试改写；释放后结果可读；§3.1 结果记录行 + §4.5 故障表 + §五 对应用例 |
| S137-03 [P1] 非事务路径不能承诺回滚已落库写入 | §3.3 #2/#8/#9 + §4.5「非事务模式语义」（保留逐写自动 flush、已持久化写入不可撤销、rollback-only 校验先于完成期 flush）+ §五 用例（A 的真实可见性、不触发 flush） |
| S137-04 [P1] DI scope 隔离缺少机制 | §4.3 受管链身份（链节点 + descendant-or-self 可见性 + Manager/访问器一致权威 + 独立 scope 为根）+ §五 用例（host A/B、Task.Run 继承、预注入父仓储跟随） |
| S137-05 [P2] 资源身份校验独立于 ambient 路由 | §4.3 资源键 + 重定向前校验时点 + 读方先校验后路由；§4.1 null/显式参数继承与校验表；§4.4 资源身份段落 + §五 用例 |
| S137-06 [P2] 异步开始、取消传递与 Provider 选项通道 | §4.1 `BeginScopeAsync` 唯一异步开始阶段（rev.3 已按 R2-S137-01 修订为「同步激活 + StartAsync」协议）、`Func<CancellationToken, Task<T>>` 通道、强类型 effective begin options、合作式语义声明；§3.3 #10 join 只能收紧 + §五 用例 |
| S137-07 [P2] CAP handle 不可信 | §4.5 lease 契约重写（Provider 所有、同一连接/事务关联、不暴露生命周期操作、live view 失效、适配边界借用）+ §3.1 lease 行 + §五 用例 |
| 文稿整理 | 移除「第 9 节」悬空引用（改为准确链接配套计划文件）；EF Core 隔离级别改为按实际数据库/驱动逐项实测声明；实施计划切片 1 纳入 Provider flush/commit/notify 职责拆分（每个可合并切片保持完整正确主链） |

### 8.2 第二轮（rev.3）

对照 [第二轮 Spec 审计](../../review/2026-10-10-issue-137-uow-spec-review-round2.md) 的逐项修订：

| 审计项 | 修订位置 |
| --- | --- |
| R2-S137-01 [P1] BeginScopeAsync 调用方可见性 | §4.1 环境激活/恢复协议：**调用方帧同步激活 + 异步 StartAsync 组合**（Task 返回须非 async 实现；Dispose/DisposeAsync 恢复段同步完成于调用方帧）；AOP 改为**包裹式拦截**（方法体运行在内核帧内）；失败恢复与终态帧确定性拒绝；§二 Manager/Scope/Proxy 行 + §4.2 释放协议 + §五 激活协议用例（强制异步挂起、await 后身份一致、两层、异步 Dispose、begin 失败、AOP 业务体、遗漏 Dispose） |
| R2-S137-02 [P2] 逻辑/物理资源身份 | §4.3 逻辑资源键（绑定/声明/租户）与物理实例身份（Context/Connection/Transaction）分离；同一准入机制（descendant 链 + 逻辑键匹配；受管 RequiresNew = 同一逻辑资源的新物理实例）；§4.4 身份段 + §4.5 lease 使用物理身份 + §五 四向同机制用例 |
| R2-S137-03 [P2] AOP 联动 token | §4.1 联动 token 通道：生成端点传联动 token 入方法参数；**操作层组合为主机制**（正式仓储/Provider 组合「传入 CT ⊕ 受管执行 token」）；支持边界文档化（非仓储等待仅观察入参 CT）；§五 AOP 用例（调用方 CT 不取消、deadline 到期、等待取消的数据库动作收到取消） |
| 文稿整理 | 契约清点 Begin/Proxy 归档行改为 `BeginScope + StartAsync`；`Disposed` 不再列为 State 行（释放标志 `IsReleased` 与 State/事务结果独立存储、互不覆盖）；计划切片 1/2 同步落位激活协议、资源键与 token 组合 |

### 8.3 第三轮（rev.3.1）

对照 [第三轮 Spec 审计](../../review/2026-10-10-issue-137-uow-spec-review-round3.md)：

- **Spec 设计通过**；R2-S137-01…03 关闭，无新增 P1 设计阻塞。
- R3-S137-01 [P2]（实施计划切片依赖）：支撑 AOP deadline 的**最小操作层 token 组合与安全读取校验前移至切片 1**——切片 1 独立验收「实际 Attribute 方法 → 正式仓储/Provider → 等待取消的数据库动作」（调用方 CT 未取消、仅 UoW deadline 到期、收到取消且完成清理；不依赖切片 2 代码、不向测试动作注入 ExecutionToken）；切片 2 将读取升级为链校验访问器并完善资源准入与仓储覆盖。
- 文稿统一（非阻塞）：`[UnitOfWorkMo]` 属性**声明方式**不变；生成器/生成调用胶水按联动 token 协议迁移并回归（§4.1 与计划切片 1/3 同步修正）。
