# Issue #137 实施计划 — 统一执行内核、资源归属与 CAP 交接

日期：2026-10-10
设计依据：`docs/superpowers/specs/2026-10-10-issue-137-uow-transaction-propagation-design.md`（rev.3.1，已吸收三轮 Spec 审计 S137-01…07 / R2-S137-01…03 / R3-S137-01 与文稿整理；本仓库同 PR 交付）
基线：master `988bfd2f`；本计划从设计合并后的 master 起执行。
纪律：每个切片一个 PR、基于前项合并后的 master；本地测试全绿后再推送跑 CI；不自动合并；移除文件进 `99_RecycleBin/`（force-add 单文件，正文逐字节一致，附 README 迁移说明）。

---

## 切片 0（本次 PR）：设计与现状清单

- [x] 设计记录：`docs/superpowers/specs/2026-10-10-issue-137-uow-transaction-propagation-design.md`
- [x] 实施计划：`docs/superpowers/plans/2026-10-10-issue-137-uow-transaction-propagation-implementation.md`
- 本 PR 为纯文档交付，不触碰主链代码；后续切片按序实施。

---

## 切片 1：契约/状态 — 统一 Options、唯一内核与激活协议、四维状态、Provider 职责拆分（PR-2）

目标：消除三入口六份实现与双 Options；`BeginScope + StartAsync`/`Execute(Async)`/AOP/生成运行时全部消费同一内核与四维状态模型（§3.2：参与状态 × 事务结果 × 通知结果 × 释放）；**落地调用方帧激活协议**（§4.1：同步激活 + 异步开始；AsyncLocal 不得依赖 async 方法体内写入）。**本切片同时划清 Provider 侧 flush / commit / 通知职责边界**（每个可合并切片必须保持完整正确主链，不能用后续切片补救已暴露的错误语义）。

改动文件：
- `src/Persistence/CrestCreates.Data.Abstractions/`：
  - `UnitOfWorkOptions.cs`（新）：IsTransactional / Propagation / IsolationLevel? / Timeout? / Provider?（替换旧死定义，含 null 继承规则 §4.1；文件从 `IUnitOfWorkEnhanced.cs` 拆出）
  - `UnitOfWorkPropagation.cs`、`UnitOfWorkState.cs`、`UnitOfWorkTransactionOutcome.cs`、`UnitOfWorkNotificationOutcome.cs`（新）
  - `IUnitOfWorkManager.cs`：`BeginScope(UnitOfWorkOptions?)`（同步激活，调用方帧）+ `scope.StartAsync(ct)`（异步开始）；`Execute/ExecuteAsync<T>(Func<CancellationToken, Task<T>>, UnitOfWorkOptions?, CancellationToken)`；删除 `BeginScopeAsync` 单步异步返回、旧 `Begin()`；`IUnitOfWorkScope` 增加 `State`/`TransactionOutcome`/`NotificationOutcome`/`IsReleased`/`ExecutionToken`/`StartAsync`/`CompleteAsync`/`RollbackAsync`
  - `UnitOfWorkBase/UnitOfWorkManager.cs`：内核重构（校验→获取→打开→执行→完成/失败路径→释放；激活/恢复只在调用方帧同步完成；join 参与者成功记录；rollback-only 标记；null 继承与冲突诊断；token/截止时间联动；同步 Execute 包装异步内核）
  - Domain `IUnitOfWork`：Begin 改为接收强类型有效选项（隔离级别/截止时间）+ CT；`Domain.UnitOfWork` 签名与 `UnitOfWorkWithEvents` 同步
- `src/Persistence/CrestCreates.Data.EFCore/UnitOfWork/EfCoreUnitOfWork.cs`：拆分 flush / commit / 通知职责——**commit 确认后立即记录 `Committed`**，通知在其后独立执行；移除 Console 吞异常路径；`Unknown` 判定入口（提交派发期失败不回写为 RolledBack）
- `src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepository.cs`（及基类）与 `DbContexts/EfCoreDbContextAdapter.cs`：**最小操作层 token 组合**（传入 CT ⊕ 受管执行 token，`CreateLinkedTokenSource`）与安全读取校验（经受管载体读取，不得全局裸读）——支撑本切片 AOP deadline 验收独立成立
- `src/Framework/Infrastructure/CrestCreates.Aop/Interceptors/UnitOfWorkMoAttribute.cs`：改为**包裹式拦截**（被拦截方法体运行在内核异步帧内；Rougamo RawMo 或等价机制），删除「OnEntry 写 ambient 后依赖继承」路径；回调栈与 `ExecutionToken` 同步收口
- `src/Framework/Api/CrestCreates.DynamicApi/DynamicApiGeneratedRuntime.cs`：两个重载改为内核调用（签名保留）；生成器把**内核联动 token** 传入服务方法 CT 参数（`RequestAborted` 作为调用方 CT 参与联动）
- 归档：`IUnitOfWorkEnhanced.cs`、`Aop.Abstractions/Options/UnitOfWorkOptions.cs` + `AopOptions.UnitOfWork`、`Data.Abstractions/RepositoryBase/Repository.cs`、`ScopedUnitOfWorkProxy` → `99_RecycleBin/issue-137-contracts-state/`
- 测试迁移：`tests/Persistence/CrestCreates.OrmProviders.Tests/*`（旧 API → `BeginScope + StartAsync`；既有 28 用例断言对象迁移）、`tests/Framework/Web/CrestCreates.Web.Tests/.../LegacyGeneratedDynamicApiRuntimeTests.cs`、AOT fixture 调用点

新增测试（判别力验证）：
- 激活协议：**强制 Provider Begin 真正异步挂起**（Task.Yield/Delay）后，调用方 `await` 返回后 Current、预注入仓储、事务物理身份一致；两层 RequiresNew；异步 Dispose 后父环境恢复；begin 失败恢复；遗漏 Dispose → 终态帧确定性拒绝
- AOP（**本切片独立验收，不依赖切片 2、不向测试动作注入 ExecutionToken**）：被拦截方法体（包裹式）内 ambient/Current 实测可见（不只 callback 场景）；实际 Attribute 方法 → 正式仓储/Provider → 等待取消的数据库动作：调用方 CT 保持未取消、仅 UoW deadline 到期 → 操作收到取消且完成清理
- join 参与者模型：两个成功 join 各 Complete+Dispose → 外层提交；join 未 Complete 即 Dispose → 外层拒绝回滚；join 失败被捕获 → 外层拒绝回滚（DB 无部分写入）
- 四维状态：重复 Complete 幂等；终态后操作/乱序释放确定性拒绝；`Committed` 在通知之前记录（通知失败不改变）；`Unknown` 不被回滚尝试改写
- 冲突参数（事务开关/显式 provider/显式隔离/超时延长）执行前诊断
- 源码守卫：AOP 与生成运行时不含自实现 begin/commit 顺序（`UnitOfWorkReflectionGuardTests` 同款源码扫描）

验证：`dotnet build CrestCreates.slnx`；`dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests`；`dotnet test tests/Framework/Api/CrestCreates.DynamicApi.Tests`；`dotnet test tests/Framework/Infrastructure/CrestCreates.Infrastructure.Tests`；`dotnet test tests/Framework/Web/CrestCreates.Web.Tests`；UoW native gate。

---

## 切片 2：资源与仓储 — 受管链身份、逻辑/物理资源身份、强类型 Ambient、未完成退出丢弃（PR-3）

目标：公开面不再有 `object Current/Push`；**受管链身份机制落地**（跨 DI scope 不串用，预注入父仓储仍跟随）；**逻辑资源键准入**（同一校验机制区分「合法 RequiresNew 跟随」与「第二资源」）；**操作层 token 组合**（正式仓储/Provider 组合传入 CT ⊕ 受管执行 token）；未完成退出丢弃未 flush 跟踪写入；连续顶层 UoW 互不污染。

改动文件：
- `UnitOfWorkAmbientContext.cs`：强类型只读 `Current`；Push/Restore 内部化（帧带逻辑资源键 + 物理实例身份 + 链节点）；
- `UnitOfWorkManager.cs`：受管链节点创建/挂接（隔离子 scope 节点挂当前节点；非内核 scope 为独立根）；push/restore 一致性校验；读方 descendant-or-self + 逻辑键匹配的**同一准入校验**；隔离 push/restore 走内部写路径；
- `IUnitOfWorkTransactionAbortable.cs` → 迁移为「终结 + 丢弃」契约（EF 实现 `ChangeTracker.Clear()`；命名与形态实现时定）；
- `EfCoreUnitOfWork.cs`、`EfCoreDbContextAdapter.cs`、`CrestCreatesDbContext.cs`：读路径改强类型 + 链节点/逻辑键解析 + 先校验后路由；**token 组合读取升级为链校验访问器**（切片 1 已落最小实现与校验）；
- EF 绑定：声明 discard/termination 能力（切片 3 完整能力表的前置）。

新增测试：
- 激活/链隔离：A.RequiresNew 内新 DI scope 的 B.Manager/Repository（Current 与仓储资源一致、无串用）；`Task.Run` 执行上下文继承；受管子 UoW 仍服务预注入父仓储
- 逻辑/物理资源身份（同一校验机制四向）：父仓储使用新物理实例 B 成功；同链同租户不同 Context 声明 C 拒绝；不同连接配置拒绝；独立 DI 根不可见帧
- 连续两个顶层 UoW：正常/异常/未完成退出 → 无残留事务、**无残留跟踪写入**（写入未提交实体后 abandon，第二个 UoW 不持久化它）、第二个独立提交
- 多层 RequiresNew + await 续接 + 成功/异常退出父环境恢复（扩展现有 2 层用例）；换租户 → 确定性拒绝并指引新建 UoW
- 预注入正式仓储跟随（默认装配 + 自定义 Adapter 两个装配，保留升级）
- 负例：直接注入原生 DbContext/预缓存 DbSet 不跟随 requiresNew（文档化限制的断言）
- 释放计数：事务句柄恰好一次、子 scope 恰好一次（既有用例保留）

验证：同上 + `dotnet test tests/Persistence/CrestCreates.Data.Abstractions.AotFixture.Tests`（fixture 同步适配）。

---

## 切片 3：Provider/入口迁移 — 能力声明、资源键校验、真实数据库验收（PR-4）

目标：Provider 能力显式声明并在执行业务前校验；资源键阻止「同 Provider 异资源」的静默复用（校验先于 ambient 路由）；非事务/隔离/超时/取消确实生效；EF PG 真实验收。

改动文件：
- `UnitOfWorkProviderBinding.cs`：能力声明（SupportedIsolationLevels / SupportsTimeout / PromptTermination / Discard / 资源键委托）；
- `UnitOfWorkManager.cs`：执行前能力校验（显式 option × 能力 × 外层；null 继承不误判；join 超时只收紧）与资源键校验；
- `EfCoreUnitOfWork.cs`：Begin 透传 isolation + CT；flush/commit/rollback CT 语义（清理独立 token）；内核截止时间联动；
- FreeSql/SqlSugar：`supportsRequiresNew: false` 保留；隔离/终止/丢弃「实现或明确拒绝」并写入 Provider 矩阵；
- 生成器/入口：联动 token 协议迁移已在切片 1 完成（生成调用胶水传联动 token；`[UnitOfWorkMo]` 属性声明不变）；本切片回归生成器测试与生成端点行为一致性。

新增测试：
- 非事务模式：只 flush 不 commit、通知条件；写 A 后失败/取消 → 检查 A 的真实数据库可见性与结果声明（不声称丢弃）
- 显式隔离级别按实测矩阵生效（真实 DB 断言）；join 更短 deadline 生效、更长被诊断；阻塞直到 token 取消的动作（合作式取消）
- 能力不符 → action 未执行的执行前失败；第二资源（第二 Context/租户库）确定性拒绝
- PostgreSQL（Testcontainers、独立 schema）：外层事务开始后的嵌套隔离、提交后可见性、回滚可见性；SQLite 快速回归。

验证：OrmProviders（SQLite）+ PG 集成（CI）+ 切片 1/2 全部套件。

---

## 切片 4：故障与原生证据 — 故障注入、生命周期顺序、NativeAOT 门禁（PR-5）

目标：生命周期顺序（校验→flush→commit→notify→cleanup）可观测；commit 前/派发中/成功后故障分别定义且结果可检查（四维）；释放后仍可读取最终结果；原生 fixture 覆盖新语义；文档（AGENTS/memory/矩阵/迁移）一致。

改动文件：
- `EfCoreUnitOfWork.cs` / `UnitOfWorkWithEvents.cs`：故障注入面（commit 响应丢失、通知发布器抛异常、清理失败）；事件队列清空条件 = 发布成功；
- `UnitOfWorkManager.cs`：故障结果对象/例外型定型（提交结果未知、提交后通知失败、清理失败可检查结果）与诊断聚合；
- `tests/Persistence/CrestCreates.Data.Abstractions.AotFixture/Program.cs`：新增传播/状态/资源身份/诊断场景与 sentinel（SHA/RID/命令/日志证据沿用 `unitofwork-native-pipeline.json` 结构）；
- `AGENTS.md`、`memory.md`、Provider 矩阵文档：状态与证据同步；
- `99_RecycleBin/`：本项移除文件与迁移说明收口。

新增测试：
- 提交前失败（flush 抛）/ commit 派发失败（注入）→ `Unknown` 且回滚尝试不改写 / commit 后通知失败（发布器抛）→ `Committed+Completed` 保持：三条路径分别断言四维结果与异常；「提交后通知失败不得声称回滚、不自动重试业务」；
- 提交后 Dispose 失败（无原始异常）→ 可检查次级结果；**释放后仍可读取最终事务结果**；
- 生命周期顺序观测（校验→flush→commit→notify→cleanup；回滚不发布成功事件）；非事务模式通知条件；
- `[UnitOfWorkMo]` 与生成 CRUD 的成功提交/失败回滚集成用例（真实数据库）；
- 原生门禁：publish → native link → 执行原生产物。

验证：全套件 + 原生门禁 + `git diff --check`；CI（ci.yml / full-validation.yml）以最终 head 为准。

---

## 切片 5：CAP 交接 — 最小事务参与 lease 契约（PR-6）

目标：交付 #125 所需的强类型、Provider 所有的本地事务参与 lease 契约与故障语义文档。

改动文件：
- `Data.Abstractions`：`IUnitOfWorkTransactionLeaseProvider` / `UnitOfWorkTransactionLease`（受限借用视图：Provider / ResourceKey / 只读事务身份 / live view 有效性；形态按设计 §4.5，命名评审后定型）；EF 绑定注册 lease 提供者；
- 交接文档：`docs/superpowers/specs/2026-10-10-issue-137-cap-handoff.md`（哪些本地事务允许接入、谁负责 begin/commit/rollback、消息记录落库时点、通知不证明持久化原子性、不得推导 exactly-once/2PC/durable outbox、适配边界借用规则；`RegisterWithDistributedTransaction` 死扩展处置交 #125）。

新增测试：同一 Connection+Transaction 身份断言（**不止 TransactionId+bool**）；错资源拒绝；完成后/释放后 lease 失效拒绝；消费者不能经普通参与契约完成外层事务；CAP 侧消费示例以单测形式固化契约（不引 CAP SDK）。

验证：全套件 + 切片 4 原生门禁；在 #125 实施前合并且未破坏任何既有门禁。

---

## 通用验证命令（每个切片）

```bash
dotnet build CrestCreates.slnx
dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests
dotnet test tests/Framework/Api/CrestCreates.DynamicApi.Tests
dotnet test tests/Framework/Infrastructure/CrestCreates.Infrastructure.Tests
dotnet test tests/Framework/Web/CrestCreates.Web.Tests
dotnet test tests/Persistence/CrestCreates.Data.Abstractions.AotFixture.Tests        # 原生门禁（linux-x64）
dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
git diff --check
```

- 依赖 Docker/Testcontainers 的 PG 集成套件在 CI 验证（本地不可用时按 #124 记录方式说明）。
- 每个修复/新增能力做**判别力验证**：临时回退 → 对应测试红 → 恢复（记录在 PR 正文）。
- PR 指向 master；本地全绿后推送；CI 以最终 head 为准；不自动合并。