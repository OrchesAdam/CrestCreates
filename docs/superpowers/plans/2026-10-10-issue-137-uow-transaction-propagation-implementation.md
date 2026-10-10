# Issue #137 实施计划 — 统一执行内核、资源归属与 CAP 交接

日期：2026-10-10
设计依据：`docs/superpowers/specs/2026-10-10-issue-137-uow-transaction-propagation-design.md`（本仓库同 PR 交付）
基线：master `988bfd2f`；本计划从设计合并后的 master 起执行。
纪律：每个切片一个 PR、基于前项合并后的 master；本地测试全绿后再推送跑 CI；不自动合并；移除文件进 `99_RecycleBin/`（force-add 单文件，正文逐字节一致，附 README 迁移说明）。

---

## 切片 0（本次 PR）：设计与现状清单

- [x] 设计记录：`docs/superpowers/specs/2026-10-10-issue-137-uow-transaction-propagation-design.md`
- [x] 实施计划：`docs/superpowers/plans/2026-10-10-issue-137-uow-transaction-propagation-implementation.md`
- 本 PR 为纯文档交付，不触碰主链代码；后续切片按序实施。

---

## 切片 1：契约/状态 — 统一 Options、状态化 scope、唯一内核（PR-2）

目标：消除三入口六份实现与双 Options；`BeginScope`/`Execute(Async)`/AOP/生成运行时全部消费同一内核与状态机。

改动文件：
- `src/Persistence/CrestCreates.Data.Abstractions/`：
  - `UnitOfWorkOptions.cs`（新）：IsTransactional / Propagation / IsolationLevel? / Timeout? / Provider?（替换旧死定义，文件从 `IUnitOfWorkEnhanced.cs` 拆出）
  - `UnitOfWorkPropagation.cs`（新）：Required / RequiresNew
  - `UnitOfWorkState.cs`（新）：Active / Committed / RolledBack / Failed / Disposed
  - `IUnitOfWorkManager.cs`：`BeginScope(UnitOfWorkOptions?)`；`Execute/ExecuteAsync<T>(Func<…>, UnitOfWorkOptions?, CancellationToken)`；删除旧签名
  - `UnitOfWorkBase/UnitOfWorkManager.cs`：内核重构（验证→获取→打开→执行→完成/失败路径→释放；rollback-only 标记；冲突诊断；同步 Execute 包装异步内核）；归档 `Begin()` + `ScopedUnitOfWorkProxy`
  - `IUnitOfWorkManager.cs` 中的 `IUnitOfWorkScope`：新增 `State`、`CompleteAsync`、`RollbackAsync`
- `src/Framework/Infrastructure/CrestCreates.Aop/Interceptors/UnitOfWorkMoAttribute.cs`：OnEntry 建 scope；OnSuccess `CompleteAsync`；OnException 标记/回滚；保留最小 scope 句柄栈（删除自实现 begin/commit/flush 顺序）
- `src/Framework/Api/CrestCreates.DynamicApi/DynamicApiGeneratedRuntime.cs`：两个 ExecuteAsync 重载改为内核调用（签名保留）
- 归档：`IUnitOfWorkEnhanced.cs`、`Aop.Abstractions/Options/UnitOfWorkOptions.cs` + `AopOptions.UnitOfWork`、`Data.Abstractions/RepositoryBase/Repository.cs` → `99_RecycleBin/issue-137-contracts-state/`
- 测试迁移：`tests/Persistence/CrestCreates.OrmProviders.Tests/*`（Begin→BeginScope；新增状态机/rollback-only/冲突参数用例）、`tests/Framework/Web/CrestCreates.Web.Tests/.../LegacyGeneratedDynamicApiRuntimeTests.cs`（TestUnitOfWorkManager 适配新契约）、AOT fixture 调用点

新增测试（判别力验证）：
- 重复 Complete 幂等 / 终态后操作拒绝 / 乱序释放拒绝
- join：不提交不释放；内层失败被捕获 → 外层确定性拒绝 + 回滚（DB 无部分写入）
- 参数冲突（provider/事务开关）执行前诊断
- 依赖边界/源码守卫：AOP 与生成运行时不含自实现 commit 顺序（`UnitOfWorkReflectionGuardTests` 同款源码扫描）

验证：`dotnet build CrestCreates.slnx`；`dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests`；`dotnet test tests/Framework/Api/CrestCreates.DynamicApi.Tests`；`dotnet test tests/Framework/Infrastructure/CrestCreates.Infrastructure.Tests`；`dotnet test tests/Framework/Web/CrestCreates.Web.Tests`；UoW native gate。

---

## 切片 2：资源与仓储 — 强类型 Ambient、未完成退出丢弃、连续顶层 UoW 干净起步（PR-3）

目标：公开面不再有 `object Current/Push`；未完成退出丢弃未提交跟踪写入；连续顶层 UoW 互不污染；await 续接语义有测试。

改动文件：
- `UnitOfWorkAmbientContext.cs`：强类型只读 `Current`；Push/Restore 内部化（帧带资源身份 + 所有者 scope）；
- `UnitOfWorkManager.cs`：隔离 push/restore 走内部写路径；
- `IUnitOfWorkTransactionAbortable.cs` → 迁移为「终结 + 丢弃」契约（EF 实现 `ChangeTracker.Clear()`；命名与形态实现时定）；
- `EfCoreUnitOfWork.cs`、`EfCoreDbContextAdapter.cs`、`CrestCreatesDbContext.cs`：读路径改强类型；
- EF 绑定：声明 discard/termination 能力（切片 3 完整能力表的前置）。

新增测试：
- 连续两个顶层 UoW：正常/异常/未完成退出 → 无残留事务、**无残留跟踪写入**（写入未提交实体后 abandon，第二个 UoW 不持久化它）、第二个独立提交
- 多层 RequiresNew + await 续接 + 成功/异常退出父环境恢复（扩展现有 2 层用例）
- 预注入正式仓储跟随（默认装配 + 自定义 Adapter 两个装配，保留升级）
- 负例：直接注入原生 DbContext/预缓存 DbSet 不跟随 requiresNew（文档化限制的断言）
- 释放计数：事务句柄恰好一次、子 scope 恰好一次（既有用例保留）

验证：同上 + `dotnet test tests/Persistence/CrestCreates.Data.Abstractions.AotFixture.Tests`（fixture 同步适配）。

---

## 切片 3：Provider/入口迁移 — 能力声明、资源身份、真实数据库验收（PR-4）

目标：Provider 能力显式声明并在执行业务前校验；资源身份阻止「同 Provider 异资源」的静默复用；非事务/隔离/取消/超时确实生效；EF PG 真实验收。

改动文件：
- `UnitOfWorkProviderBinding.cs`：能力声明（SupportedIsolationLevels / SupportsTimeout / PromptTermination / Discard / 资源身份委托）；
- `UnitOfWorkManager.cs`：执行前能力校验（显式 option × 能力 × 外层）与身份校验；
- `EfCoreUnitOfWork.cs`：Begin 透传 isolation + CT；flush/commit/rollback CT 语义（清理独立 token）；内核截止时间；
- FreeSql/SqlSugar：`supportsRequiresNew: false` 保留；隔离/终止/丢弃「实现或明确拒绝」并写入 Provider 矩阵；
- 生成器/入口：确认无生成产物需要改（`[UnitOfWorkMo]` 契约不变；生成器测试回归即可）。

新增测试：
- 非事务模式：只 flush 不 commit、通知条件；显式隔离级别生效（真实 DB 断言或 Provider 参数透传断言）；
- 取消：OCE 保留 + 已取消 token 下清理仍执行 + 不报成功；超时：到期回滚 + 不报成功；
- 能力不符 → action 未执行的执行前失败；同 Provider 异资源冲突诊断；多租户切库必须新建 UoW 的负例；
- PostgreSQL（Testcontainers、独立 schema）：外层事务开始后的嵌套隔离、提交后可见性、回滚可见性。

验证：OrmProviders（SQLite）+ PG 集成（CI）+ 切片 1/2 全部套件。

---

## 切片 4：故障与原生证据 — 生命周期顺序、故障分层、NativeAOT 门禁（PR-5）

目标：flush→commit→notify→cleanup 固定顺序可观测；commit 前后故障分别定义且可检查；原生 fixture 覆盖新语义；文档（AGENTS/memory/矩阵/迁移）一致。

改动文件：
- `EfCoreUnitOfWork.cs` / `UnitOfWorkWithEvents.cs`：移除 Console 吞异常；提交后通知失败不包装成回滚；事件队列清空条件 = 发布成功；异步释放契约；
- `UnitOfWorkManager.cs`：故障结果对象/例外型（提交结果未知、提交后通知失败）与诊断聚合；
- `tests/Persistence/CrestCreates.Data.Abstractions.AotFixture/Program.cs`：新增传播/状态/身份/诊断场景与 sentinel（SHA/RID/命令/日志证据沿用 `unitofwork-native-pipeline.json` 结构）；
- `AGENTS.md`、`memory.md`、Provider 矩阵文档：状态与证据同步；
- `99_RecycleBin/`：本项移除文件与迁移说明收口。

新增测试：
- 提交前失败（flush 抛）/ commit 失败（注入）/ commit 后通知失败（发布器抛）：三条路径分别断言异常与状态；「提交后通知失败不得声称回滚、不自动重试业务」；
- 生命周期顺序观测（flush→commit→notify→cleanup；回滚不发布成功事件）；非事务模式通知条件；
- `[UnitOfWorkMo]` 与生成 CRUD 的成功提交/失败回滚集成用例（真实数据库）；
- 原生门禁：publish → native link → 执行原生产物。

验证：全套件 + 原生门禁 + `git diff --check`；CI（ci.yml / full-validation.yml）以最终 head 为准。

---

## 切片 5：CAP 交接 — 最小事务参与/访问契约（PR-6）

目标：交付 #125 所需的强类型、Provider 所有的本地事务参与/访问契约与故障语义文档。

改动文件：
- `Data.Abstractions`：`IUnitOfWorkResourceAccessor` / `UnitOfWorkResourceHandle`（形态按设计 §4.5，命名评审后定型）；EF 绑定注册访问器；
- 交接文档：`docs/superpowers/specs/2026-10-10-issue-137-cap-handoff.md`（哪些本地事务允许接入、谁负责 begin/commit/rollback、消息记录落库时点、通知不证明持久化原子性、不得推导 exactly-once/2PC/durable outbox；`RegisterWithDistributedTransaction` 死扩展处置交 #125）。

新增测试：访问器身份/所有权/存活范围断言（Active/终态/跨 scope）；CAP 侧消费示例以单测形式固化契约（不引 CAP SDK）。

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