# Issue #137 UnitOfWork 事务传播、资源归属与业务参与主链 — 设计记录与现状清单

日期：2026-10-10
实施基线：master `988bfd2f`（PR #136 merge，即 #124 闭环后的真实主链）
关联：[Issue #137](https://github.com/OrchesAdam/CrestCreates/issues/137)、[Issue #121](https://github.com/OrchesAdam/CrestCreates/issues/121)、[#124 设计记录](../../review/2026-10-10-issue-124-unitofwork-unified-registration.md)、[#123 证据矩阵](../../review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md)
下游：#125（CAP）必须在本项验收并合并后实施；#130 复用本项的 Provider 能力边界。

本文档是 #137 的第 1 项交付（Issue §1「先交付设计与现状清单，再实施」）：真实调用链、公开契约清点与处置、资源所有权表、状态/传播表、设计裁定与测试推导。实施按第 9 节拆分为多个 PR，每个 PR 基于前项合并后的 master。

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
| G6 | 无取消/超时：`IUnitOfWork` 无 CancellationToken；Begin 不传隔离级别；ExecuteAsync 不接收 CT | `Domain.UnitOfWork.IUnitOfWork`、`EfCoreUnitOfWork` |
| G7 | 未完成退出不丢弃跟踪写入；连续顶层 UoW 可能带入上一逻辑 UoW 的残留 | `AbortPendingTransaction` 仅回滚事务 |
| G8 | Ambient 契约为 `public static object? Current` + `Push(object)`（非强类型、可被任意调用方操作） | `UnitOfWorkAmbientContext.cs` |
| G9 | 故障结果不可检查：TryRollback/TryAbort 吞清理异常；通知失败「先吞后写控制台」；发布阶段异常（如 CT 取消）会被 `catch → Rollback` 包装成「回滚」——**提交已成功后的通知失败可能被报告为失败/回滚** | `EfCoreUnitOfWork.CommitTransactionAsync`、Manager TryRollback |
| G10 | Provider 能力只有 `SupportsRequiresNew` 一个布尔；隔离级别/超时/及时终止/丢弃能力未声明，无法「执行前失败」 | `UnitOfWorkProviderBinding` |
| G11 | 无事务资源身份模型：Provider 相同即视为可复用；同一 EF Core 的不同 Context/连接/租户库无法区分 | 复用判定只看 ambient 有无 |
| G12 | CAP 交接面不存在：`RegisterWithDistributedTransaction` 扩展无消费者；无「当前 Connection/Transaction 身份、所有权、存活范围」契约 | `UnitOfWorkIntegrationExtensions.cs` |

---

## 二、公开契约清点与处置

| 契约/类型 | 当前实现/消费者 | 处置 |
| --- | --- | --- |
| `Domain.UnitOfWork.IUnitOfWork`（Begin/Commit/Rollback/SaveChanges/Dispose） | 三个入口、Provider 实现、测试 | **保留**。保持最小（Provider 级句柄操作）；生命周期/状态由平台 scope 承担，不在此接口堆语义 |
| `IUnitOfWorkManager` | AOP、生成运行时、测试、AOT fixture | **保留并收口**：执行内核唯一入口（ExecuteAsync/同步包装 + BeginScope）；签名迁移为 options 形态（见 §4.1） |
| `IUnitOfWorkScope` | AOP、生成运行时、测试 | **保留并升级为状态化 scope**：新增 `State`、`CompleteAsync`、`RollbackAsync`；完成/回滚/释放语义全部经此对象（见 §4.2） |
| `IUnitOfWorkFactory` + `UnitOfWorkProviderBinding` + `Registry` | Provider 包声明、Manager | **保留**；Binding 扩展能力声明与资源身份（见 §4.4） |
| `UnitOfWorkRegistrationState` | 注册扩展 | 保留（内部装配状态） |
| `UnitOfWorkAmbientContext`（`public static object` Current/Push） | 写方仅 Manager；读方 EF 适配器/默认 DbContext/测试 | **替换为强类型受管访问器**：`Current` 强类型只读公开；写方内部化（见 §4.3）。旧公开 Push 归档 |
| `IUnitOfWorkTransactionAbortable` | Manager 调用、EF 实现 | **迁移**为「未完成退出终结 + 丢弃未提交跟踪状态」契约（见 §4.3） |
| `UnitOfWorkMoAttribute` | 手写服务、生成 CRUD、DynamicApi 生成器读取 | **契约保留**（属性签名与语义不变）；实现迁移到内核（AOP 只保留传递 scope 句柄的最小栈） |
| `DynamicApiGeneratedRuntime.ExecuteAsync` | 生成端点（生成器契约） | **签名保留**（生成器测试依赖）；实现迁移到内核 |
| `Data.Abstractions.UnitOfWorkOptions` | **零消费者** | **替换**为唯一生效的 options 类型（同名同位置，见 §4.1）；过滤器开关/软删除等成员删除 |
| `IUnitOfWorkEnhanced`（GetRepository/EnableSoftDeleteFilter/SetTenantId 等） | **无实现、无消费者** | **归档**（`99_RecycleBin/issue-137-...`）。仓储获取走 DI 注入；TenantId 走 `ICurrentTenant` 平台能力；过滤开关走现有过滤注册/IgnoreQueryFilters |
| `Aop.Abstractions.Options.UnitOfWorkOptions` + `AopOptions.UnitOfWork` | 零读取 | **归档**（不迁移；AOP 不再持有独立 UoW 配置） |
| `Data.Abstractions.RepositoryBase.Repository<,>`（构造期捕获 DbSet/QueryableBuilder） | **零消费者**（生成仓储走 `CrestRepositoryBase`） | **归档**（跨 UoW 缓存资源对象的反面样本，防止被误用） |
| `IUnitOfWorkManager.Begin(OrmProvider?)` + `ScopedUnitOfWorkProxy` | 测试、AOT fixture（旧式单 UoW API） | **归档**；调用方迁移到 `BeginScope`（测试/fixture 同步迁移） |
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
| Ambient 帧（当前资源上下文） | 内核（隔离 scope 开始时 Push） | 内核 | 隔离 scope 退出，**先于**资源释放恢复 | 仅栈顶可恢复；乱序不允许回退他人环境 | 异常路径与正常路径同一恢复点 |
| 未提交跟踪状态（ChangeTracker/pending writes） | context | UoW/context | 提交后自然持久化；未完成退出必须**丢弃**（新增能力，见 §4.3） | 丢弃幂等 | 与 Abandon 一体 |
| 域事件队列（实体 DomainEvents） | 实体/UoW | UoW | 发布成功后清除；发布失败**不得静默吞掉**（见 §4.5） | — | 回滚路径不发布 |

### 3.2 状态表（平台 scope 状态机）

| 状态 | 含义 | 允许的后续操作 | 重复/越界行为（裁定） |
| --- | --- | --- | --- |
| `Active` | 已开始；事务（若声明）已打开或按非事务模式运行 | `CompleteAsync`、`RollbackAsync`、`Dispose` | — |
| `Committed` | flush + 数据库 commit 成功（非事务：flush 成功） | 只读 `State`；`Dispose` | 再次 `CompleteAsync` = **幂等 no-op**；`RollbackAsync` = 确定性拒绝（不得谎称可回滚） |
| `RolledBack` | 显式回滚或失败路径回滚完成 | 只读 `State`；`Dispose` | 再次 `RollbackAsync` = 幂等 no-op；`CompleteAsync` = 确定性拒绝 |
| `Failed` | 完成过程自身失败且回滚/清理未能到达确定终态（如 commit 失败且回滚也失败） | 只读 `State`；`Dispose` | 一切完成/回滚调用 = 确定性拒绝并携带诊断 |
| `Disposed` | 资源已释放（对象/子 scope/事务句柄），环境已恢复 | 只读 `State` | 任何操作 = 确定性拒绝（`ObjectDisposedException` 语义） |

- **乱序释放**：非栈顶 scope 释放 = 确定性拒绝（保留 #136 既有 `The unit of work scope was disposed out of order` 语义）。
- `Active + Dispose`（未完成退出）：owner scope → 回滚 + 丢弃未提交状态 + 释放（终态 `RolledBack`）；join scope → 标记外层 rollback-only（§3.3）。

### 3.3 传播表

| # | 环境 | 请求 | 结果 | 诊断/裁定 |
| --- | --- | --- | --- | --- |
| 1 | 无 | Required + 事务 | 新顶层 scope；Begin；Complete = flush+commit | — |
| 2 | 无 | Required + 非事务 | 新顶层 scope；无事务；Complete = flush（+通知条件 §4.5） | — |
| 3 | 无 | RequiresNew | 与 #1/#2 相同的顶层新建（requiresNew 无环境时退化为新建，确定性，不报错） | — |
| 4 | 有 | Required + 参数一致 | **join**：IsOwner=false；内层不得提交/释放外层；内层 Complete = no-op | — |
| 5 | 有 | Required + 参数冲突（provider / 隔离级别 / 事务开关不同） | **执行前确定性失败**，不静默降级 | 消息含内外参数与修正指引 |
| 6 | 有 | RequiresNew + Provider 不支持 | 执行前 `NotSupportedException`（保持 #124 fail closed） | — |
| 7 | 有 | RequiresNew + 支持 | 子 scope 隔离：独立资源/事务；内层 Complete 独立提交；外层回滚**不撤销**已提交内层 | 文档明确 |
| 8 | join | 内层 action 失败（无论外层是否捕获） | 外层标记 rollback-only；外层 Complete = **确定性拒绝 + 回滚**（不静默提交部分失败） | 终态异常携带原因 |
| 9 | 任意 | 取消（CT 触发） | action 获 `OperationCanceledException` → 回滚（清理用独立 token）→ 原 OCE 上抛；**不得报告成功** | 清理不被取消跳过 |
| 10 | 任意 | 超时（Options.Timeout 到期） | 内核级截止时间：begin/flush/commit 前到期 = 取消语义 + 回滚；commit 派发期间失败 = 「提交结果未知」诊断（§4.5） | 超时不得报告成功 |

---

## 四、设计裁定（To-Be）

### 4.1 统一 Options 与唯一执行内核

- 唯一 options 类型（替换 `Data.Abstractions.UnitOfWorkOptions` 死定义）：

  ```
  UnitOfWorkOptions {
      bool IsTransactional = true;                    // 事务开关
      UnitOfWorkPropagation Propagation = Required;   // Required | RequiresNew（其他值 fail closed）
      IsolationLevel? IsolationLevel;                 // null=默认；显式值必须与 Provider 能力/外层一致
      TimeSpan? Timeout;                              // 内核截止时间（禁止声明未生效）
      OrmProvider? Provider;                          // null=按默认解析规则
  }
  ```

- `CancellationToken` 是执行方法的参数（不是 option）。扩展 `IUnitOfWork`（Domain）为带 CT 的异步签名属本项配套（同步兼容入口只做包装）。
- 唯一内核：`IUnitOfWorkManager` 的 `ExecuteAsync<T>(Func<Task<T>>, UnitOfWorkOptions?, CancellationToken)` 与 `BeginScope(UnitOfWorkOptions?)` 返回的状态化 scope 共享同一实现；AOP、生成运行时、`Execute/ExecuteAsync`、同步包装全部经内核。**任何入口不得自实现 begin/commit/flush/rollback 顺序。**
- 同步 `Execute<T>` = 异步内核的同步等待包装（唯一实现；`ExecuteAsync(...).GetAwaiter().GetResult()`，不是第二套逻辑）。
- 迁移方式：`BeginScope(bool, bool, OrmProvider?)`、`Execute(Async)(provider)` 旧签名的调用方全部在仓库内（AOP、生成运行时、测试、AOT fixture），**直接迁移不保留双签名**（唯一主链；避免过渡 shim 被误用）。`[UnitOfWorkMo]` 属性签名不变，生成器不受影响。

### 4.2 状态机与完成语义

- 状态机由平台 scope 承载（§3.2）；Provider UoW 保留句柄级确定性拒绝（重复 Begin、终态后 Commit 等）。
- `CompleteAsync`（owner 且事务）：flush（`SaveChanges`）→ **rollback-only 校验**（join 标记透传）→ 数据库 commit → 已提交通知 → 清理（释放事务句柄）→ `Committed`。
- `CompleteAsync`（owner 非事务）：flush → 通知（条件见 §4.5）→ `Committed`。
- `CompleteAsync`（join）：no-op 返回；内层失败由内核在异常路径标记外层 rollback-only。
- `RollbackAsync`：仅 owner 生效；回滚 + 丢弃未提交状态 + 清理；join 调用 = 标记外层 rollback-only（由内核异常路径调用）。
- `SaveChanges`（flush）与事务成功完成分开定义：flush 不结束事务、不发布通知；commit 内含 flush。
- 顶层 scope 未完成退出：回滚 + 丢弃 + 释放（不等请求 DI scope 结束）；**未提交跟踪状态不得带入同一请求内的下一个顶层 UoW**（§4.3 能力）。
- 原业务异常保留：回滚/释放失败仅作诊断（附带数据/日志），不得替换根因；异步释放纳入正式契约（`DisposeAsync` 语义，同步 Dispose 保留兼容）。

### 4.3 资源归属与仓储参与

- **强类型受管 Ambient 访问器**（替换 `public static object? Current/Push`）：
  - 公开只读：`Current` 返回强类型 `IDataBaseContext?`（EF 适配器/默认 DbContext/测试读路径不变语义）。
  - 写路径（Push/Restore）内部化：仅平台内核可操作；帧携带资源身份与所有者 scope（§4.4），乱序恢复不允许回退他人环境（保留既有语义）。
  - 对外「当前资源」的消费统一经该访问器 + `IDataBaseContext`；不再暴露 object。
- **构造不变量**（保留 #136）：隔离层先 Push 再构造 UoW；新建 UoW 绝不因父环境路由被重定向到旧 Context/连接。
- **未完成退出 = 事务终结 + 跟踪状态丢弃**：迁移 `IUnitOfWorkTransactionAbortable` 语义（回滚句柄 + 丢弃未提交跟踪写入；EF 实现 `ChangeTracker.Clear()`）。不支持该能力的 Provider 必须在绑定中声明，并按 Provider 矩阵落位（实现或明确拒绝），不得静默残留。
- **仓储参与规则**：
  - 正式仓储在**操作时**解析当前上下文（现状已满足）；构造期缓存 DbSet/Queryable/原生 Context 的仓储基类已归档（§二）。
  - 直接注入原生 DbContext（非 `IDataBaseContext` 通道）与预缓存的 DbSet/IQueryable 不承诺跟随 requiresNew——限制写入文档与测试断言（负例）。
  - 一个 UoW 内不支持并行数据库操作（单 context/事务，非线程安全）；AsyncLocal 为执行流语义——不得据此声称 DbContext 并发安全。文档 + 覆盖 await 续接用例。
- **跨 manager / 子 scope / 宿主行为**（一致性裁定）：
  - `Manager.Current` 与 Ambient `Current` 的作用域不同且都必须指向同一资源：Manager 的当前 scope 是 per-manager（scoped 实例 + AsyncLocal 执行流）；Ambient 是执行流级、仅在隔离窗口内有值、由内核受管——两者在任何 owner scope 内不得互相矛盾（仓储读 Ambient，业务读 Manager，必须同源）。
  - 不同 DI scope/不同宿主（后台作业的独立 scope、测试工厂重建容器）各自持有 manager 实例与执行流：跨 scope 不共享 UoW/资源；子 scope 内解析的 manager 不继承父 scope 的 Current（隔离资源归属始终由创建者 scope 承担）。
  - 跨宿主/跨执行流不存在隐式事务共享；任何「跨 scope 复用」都必须显式经过内核并产生新资源。
- **覆盖要求**：多层 RequiresNew、await 续接、成功/异常退出后的父环境恢复（既有 2 层用例 + 新增续接与恢复矩阵）。

### 4.4 Provider 能力与资源身份

- Binding 能力声明（强类型，装配期由 Provider 包声明，内核在**执行业务前**校验）：

  | 能力 | 说明 | EF Core | FreeSql | SqlSugar |
  | --- | --- | --- | --- | --- |
  | SupportsTransactions | 本地事务 | 是 | 是（SDK） | 是（SDK Ado） |
  | SupportsRequiresNew | 隔离子 scope + 环境跟随 | 是（已验证） | 否（fail closed） | 否（fail closed） |
  | SupportedIsolationLevels | 显式隔离级别集合 | 标准级别（透传） | 按实现声明或拒绝 | 按实现声明或拒绝 |
  | SupportsTimeout | 内核截止时间之外的 Provider 级超时（如有） | 无额外声明（内核截止时间生效） | 声明或拒绝 | 声明或拒绝 |
  | PromptTerminationOnAbandon | 未完成退出及时终结事务 | 是（#136） | 实现或声明拒绝（§9 切片 3） | 实现或声明拒绝（§9 切片 3） |
  | DiscardUncommittedOnAbandon | 未完成退出丢弃未提交状态 | 是（新增） | 声明或拒绝 | 声明或拒绝 |

- **资源身份**：复用判定不使用「OrmProvider 相同」作为唯一依据；Binding 提供资源身份标记（effective Context/连接实例），join 时校验显式参数与外层资源身份一致，冲突 → 执行前确定性诊断。跨租户库/跨连接切换必须新建 UoW。
- **不支持的能力在业务执行前失败**：显式 isolation/requiresNew/timeout 与能力不符 → 确定性异常（含 Provider 与能力矩阵摘要），不静默降级。
- **范围裁定**：只承诺单数据库资源的本地事务；多 DbContext 共享事务（如纳入）需真实数据库证明；跨库/跨 ORM 原子提交**明确拒绝**——不提供「依次 Commit 即原子」的聚合器。
- EF Core 用真实 PostgreSQL（Testcontainers、独立 schema，CI 覆盖）+ SQLite 快速回归验收；FreeSql/SqlSugar「实现已声明能力或明确拒绝」；外部 SDK AOT 边界留 #130。
- 装配仍为显式强类型绑定；能力校验失败在同一次装配/执行路径确定性诊断。

### 4.5 职责收口、生命周期顺序与 CAP 交接

- `IUnitOfWorkEnhanced` 职责收口（档案化，§二）：仓储获取（DI 注入）、TenantId 切换（`ICurrentTenant`）、数据过滤开关（现有过滤注册/`IgnoreQueryFilters`）不进入 UoW；无兼容 shim（零消费者）。
- **固定生命周期顺序**：`flush/校验 → 数据库 commit → 已提交通知 → 清理`。回滚路径不发布成功事件。非事务模式：flush 成功后再通知。
- **故障分层（结果可检查）**：

  | 故障点 | 结果 | 规则 |
  | --- | --- | --- |
  | 提交前 flush/校验失败 | 回滚（owner）；原异常上抛 | 状态 `RolledBack`；清理失败仅诊断 |
  | 数据库 commit 失败 | 回滚尝试；原 commit 异常上抛（附回滚失败诊断） | commit 派发期失败 = 「提交结果未知」显式标记，不得谎称已回滚 |
  | 提交后通知失败 | 状态 `Committed`；抛出/记录**提交后通知失败**（携带「事务已提交」事实） | **不回滚**、不自动重试整个业务（防重复写入） |
  | 回滚/释放失败 | 原业务异常保留；失败作为次级诊断 | 不得替换根因 |
  | 域事件发布失败 | 通知阶段失败（同上） | 不再静默吞掉（移除 Console 吞异常）；事件队列清空条件 = 发布成功 |

- **CAP 最小交接契约**（本项设计 + §9 切片 5 实现，SDK 适配留 #125）：

  ```
  IUnitOfWorkResourceAccessor {
      bool TryGetCurrent(out UnitOfWorkResourceHandle handle);
  }
  UnitOfWorkResourceHandle {
      OrmProvider Provider;
      object ResourceIdentity;          // 同一事务资源的身份（Context/连接实例标记）
      IDataBaseTransaction? Transaction; // 本地事务句柄（TransactionId/IsCompleted）
      bool OwnsTransaction;             // 谁负责 begin/commit/rollback（内核）
      UnitOfWorkState ScopeState;       // 存活范围（Active→终态）
  }
  ```

  - Provider 所有：访问器由 Provider 绑定声明，业务/CAP 不构造、不伪造。
  - 规则（交接文档约束）：CAP 消息记录必须使用**同一资源/事务**在 commit 前落库；谁负责开始/提交/回滚 = UoW 内核；提交后通知**不证明**消息持久化原子性；不得从生命周期 hook 推导 exactly-once/2PC/durable outbox；不新增万能公开扩展点。
  - `RegisterWithDistributedTransaction` 死扩展的处置记录在交接文档中交 #125。

### 4.6 不做的事（与 Issue「不在本项范围」一致）

CAP storage/transport 配置与 SDK 原子发布验证（#125）；ORM SDK 全量支持矩阵（#130）；通用分布式事务引擎、2PC、Saga、跨库自动协调、自动业务重试、MongoDB UoW、新 ORM Provider；#87/#88 已裁定的业务部署/激活事务不重开。

---

## 五、测试推导（由表驱动，不以当前实现自证）

原则：每条验收判据对应契约表行；新增回归测试须**判别力验证**（临时回退修复 → 测试转红 → 恢复）。

| 契约（§三/§四） | 测试计划 | 判别力验证 |
| --- | --- | --- |
| 状态机（3.2） | 重复 Complete 幂等；终态后 Rollback/Complete 确定性拒绝；乱序释放拒绝；Active+Dispose（owner）终态与释放 | 去除状态守卫 → 对应用例红 |
| 传播表（3.3 #4/#5/#8） | join 不提交不释放（可查 DB 证外层未提交）；参数冲突诊断；内层失败被捕获 → 外层拒绝提交并回滚（DB 无部分写入） | 去除 rollback-only 标记 → 部分写入用例红 |
| 传播表（#6/#7） | 不支持 requiresNew 诊断（保留）；两层 RequiresNew 内层提交持久化、外层回滚不影响（保留）+ await 续接变体 | 回退 push-first/ambient 恢复 → 红 |
| 资源表（3.1） | 事务释放恰好一次（句柄计数）；子 scope 释放一次；连续两个顶层 UoW：正常/异常/未完成退出均无残留事务、**无残留跟踪写入**、第二个可独立提交 | 去除 discard → 残留写入用例红 |
| Options 生效（4.1） | 非事务模式不提交只 flush；隔离级别透传断言（真实 DB：读现象/或 Provider 调用参数检查）；取消：OCE 保留 + 清理执行（token 已取消仍回滚）+ 不报成功；超时：到期回滚 | 移除 CT 传递 → 取消用例红 |
| 入口统一（4.1） | `[UnitOfWorkMo]` 成功提交/失败回滚集成用例（真实 DB）；生成 DynamicAPI 端点事务用例；两入口复用同一内核（AOP/生成运行时不出现自己的 begin/commit 实现——源码守卫测试） | 让 AOP 绕开内核 → 守卫测试红 |
| 故障分层（4.5） | 提交前失败/commit 失败/提交后通知失败分别断言：原异常保留、状态正确、提交后通知失败**不得**声称回滚、无自动业务重试；生命周期顺序（flush→commit→notify→cleanup）观测用例 | 各故障注入分支判别力验证 |
| Provider 能力/身份（4.4） | 能力不符 → 业务执行前失败（动作未执行断言）；同 Provider 异资源冲突诊断；FreeSql/SqlSugar 声明矩阵用例 | 跳过校验 → 对应红 |
| PostgreSQL 真实验证 | Testcontainers PG（独立 schema）：外层事务开始后的嵌套隔离与提交后可见性（CI） | — |
| 原生门禁 | 更新 `CrestCreates.Data.Abstractions.AotFixture`：传播/状态/资源身份/诊断场景；publish→native link→执行原生产物；证据含 SHA/RID/命令/日志 | 门禁失败传播保留 |
| 依赖边界/文档 | Boundary 测试保留；AGENTS.md/memory.md/Provider 矩阵/迁移说明同步 | — |

基线既有测试（28 项 OrmProviders + AOT fixture 6 场景 + Web.Tests 1 项）全部保留并按新契约迁移断言对象（状态机/内核 API）；`ErrorOnLegacy` 测试替换不删除。

---

## 六、验收矩阵映射（Issue → 本设计 → 交付切片）

| Issue 验收判据 | 对应设计 | 切片 |
| --- | --- | --- |
| 默认 EF 装配 + 自定义 Adapter 由预注入正式 Repository 验证提交/回滚 | §4.3；既有 2 用例保留升级 | 2 |
| Required 复用/rollback-only/冲突参数 | §3.3 #4/#5/#8 | 1 |
| 多层 RequiresNew | §3.3 #7（既有用例 + 续接变体） | 2 |
| 连续顶层 UoW 无残留 | §3.2、§4.3（含 discard 能力） | 1（状态）+ 2（discard） |
| 释放一次、事务及时结束 | §3.1 | 1 + 2 |
| 非事务/隔离/取消/超时/故障结果 | §4.1、§4.5 | 3 + 4 |
| 多 Context 错误复用拒绝 / Provider 能力执行前拒绝 | §4.4 | 3 |
| 真实 PostgreSQL 嵌套隔离与提交后可见性 | §五 | 3 |
| `[UnitOfWorkMo]` + 生成 CRUD 成功/失败集成 | §4.1 | 1（机制）+ 4（集成） |
| commit 前后故障通知顺序与结果 | §4.5 | 4 |
| UoW NativeAOT fixture 更新（SHA/RID/命令/日志） | §五 | 4 |
| 依赖边界/文档/支持矩阵/99_RecycleBin 一致 | §二 | 各切片同步，4 终审 |
| 向 #125 交付交接契约与故障语义 | §4.5 | 5 |

---

## 七、风险与开放项

1. **FreeSql/SqlSugar 及时终止与丢弃能力**：切片 3 落位「实现或明确拒绝」；若拒绝，矩阵与迁移说明必须显式（不得默认残留事务）。
2. **CAP 契约命名与最终形态**（`IUnitOfWorkResourceAccessor` 等）在切片 5 由评审确认后定型；本设计只固定职责与规则。
3. **通知失败语义**：提供「提交后通知失败」异常/诊断类型的具体命名在切片 4 定型；本设计固定「不回滚、不谎称、不自动重试」三原则。
4. 旧测试/AOT fixture 的迁移面（`Begin()`、双入口实现）在切片 1/4 内完成，迁移说明入 `99_RecycleBin` 记录。
