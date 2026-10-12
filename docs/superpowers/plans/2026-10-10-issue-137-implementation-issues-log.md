# Issue #137 实施问题记录（切片化实施，统一审查）

日期：2026-10-10
用途：记录 #137 实施过程中发现的问题、遗留项、环境边界与建议后续动作，供整体完成后统一审查。
设计依据：`docs/superpowers/specs/2026-10-10-issue-137-uow-transaction-propagation-design.md`（rev.3.1）
分支：`feat/issue-137-slice1-kernel-state-20261010-155153`（切片 1 起堆叠；PR #139 统一承载）

## 状态说明

- 每个条目格式：编号 / 类别（缺陷·遗留·环境·设计待判·流程）/ 说明 / 影响 / 建议动作 / 解决切片或后续轨道。

## 记录

| # | 类别 | 说明 | 影响 | 建议动作 | 状态 |
| --- | --- | --- | --- | --- | --- |
| I-01 | 遗留（AOP 轨道） | AOP 拦截器的服务解析仍走既有反射式 `MethodContextExtensions.GetService`（读取 Target 的 `ServiceProvider`/`Services`/`HttpContextAccessor` 属性，如 `CrestAppServiceBase.ServiceProvider`）。本切片未改动该机制；AOP 验收探针通过显式暴露 `ServiceProvider` 属性复现生产模式。 | 无该属性的被织入目标会静默跳过 UoW（manager 缺失 warn+skip 的既有兼容路径）；与「优先代码生成、减少反射」原则相悖 | 建议 AOP 轨道跟进：替换为类型化 scope 访问器（编译期/明确注册），并复审 warn+skip 语义 | 待后续轨道 |
| I-02 | 环境（本地验证） | SQLite 为单写者：EF 默认 `BEGIN IMMEDIATE`；requiresNew 的父子写事务不可重叠，本地 UoW 数据库用例按序开启写事务（注释已注明），嵌套隔离的并发语义由 PostgreSQL CI 套件覆盖 | 本地无法复现重叠写事务场景 | 保持 CI 为 PG 场景证据来源；本地改动涉及事务顺序时按序断言 | 已知/接受 |
| I-03 | 环境（本地验证） | Web.Tests 中 4 个 `CapabilityEndpointBoundaryTests` 在 worktree 失败（查找 `.git` 目录，worktree 中为文件），#124 记录在案；与本项无关 | 本地绿灯信号需扣除该项 | 无需处理；CI（非 worktree）通过 | 已知/接受 |
| I-04 | 环境（本地验证） | Boundary 套件 5 个失败：3 个 Testcontainers/Docker 不可用；2 个 Control Plane reference-data evidence ledger 依赖 CI 复位后再生产（本地不产生该证据） | 本地无法全绿该套件 | 以 CI 为准；本地按受影响套件验证 | 已知/接受 |
| I-05 | 遗留（切片 3/5 决策） | FreeSql/SqlSugar 的「未完成退出及时终止 / 丢弃未 flush 状态 / CAP lease」能力未落地（当前声明 fail-closed 的仅 requiresNew；终止依赖容器释放语义） | 非 EF Provider 的未完成退出没有即时事务终结保证 | 切片 3（终止/丢弃）与切片 5（lease）落位「实现或明确拒绝」并写入 Provider 矩阵 | 已裁定：切片 3 实现终止/丢弃（`IUnitOfWorkAbandonable`）；lease 未声明（交 #125/#130 按需评估声明） |
| I-06 | 环境（本地验证） | PostgreSQL/Testcontainers 依赖套件（IntegrationTests 等）本地不可运行（Docker 不可用） | 涉及 PG 的验收项本地不可复现 | 由 CI 验证并在 PR/记录中标注范围 | 已知/接受 |
| I-07 | 流程 | `dotnet build` 管道给 `tail` 会吞退出码；本仓库 CI 等价本地构建需：solution restore → 遗留 fixture restore → BuildTasks bootstrap → `--no-restore` 构建 | 误判构建结果 | 已写入项目记忆；PR 验证记录用重定向 + `$?` | 已记录 |
| I-08 | 遗留（切片 2） | 受管执行 token 的**链校验**已贯穿适配器构造的 builder/set 与 `EfCoreRepository`（可注入节点）；平台派生仓储基类 `EfCoreRepositoryBase` 的原始 IQueryable 终结路径仍用顶层帧语义（其派生仓储 ctor 未向基类传递节点）。期限污染的窗口仅限「独立 scope 在同一异步流 + 派生仓储原始 IQueryable 路径」 | 极端场景下独立 scope 可能读到他人截止时间（不含资源/事务路由） | 后续切片或 ORM 轨道：为派生仓储 ctor 传递节点（或基类经 DI 解析节点） | 待处理 |
| I-09 | 环境/测试 | InMemory 等非关系型 Provider 没有连接配置：ambient 日志键与读方连接校验对非关系型 Provider 自动退化为「声明类型」比较（读取连接串带保护）；无环境帧时快速返回不计算逻辑键 | 无（行为已定义） | 保持该保护；新增非关系型 Provider 时复核 | 已处理 |
| I-10 | 声明裁定（切片 3） | FreeSql/SqlSugar 能力声明：`PromptTerminationOnAbandon=true`、`DiscardUncommittedOnAbandon=true`（已实现 `IUnitOfWorkAbandonable`）；**未声明隔离级别**（SDK 隔离能力未实测，显式请求由内核执行前拒绝） | 这两个 Provider 不支持显式隔离级别直到有实测证据 | 交 #130 ORM 矩阵：实测 SDK 隔离能力后再逐项声明 | 已裁定 |
| I-11 | 证据边界（切片 3） | 隔离级别矩阵按 ProviderName 探测声明：SQLite={Serializable}（本地实测接受/拒绝双向）；PG={ReadCommitted,RepeatableRead,Serializable}（CI Testcontainers 实测，本地无 Docker 不运行）；未知提供者按透传声明 | 本地无法运行 PG 用例 | 以 CI 为 PG 证据来源；文档矩阵在切片 4 同步 | 已知/接受 |
| I-12 | 交付整理（切片 4） | 生命周期顺序（begin→flush→commit→notify→dispose 顺序观测）、非事务 flush 后通知、放弃失败可检查（State=Failed）用例补齐；原生 fixture 新增 deadline 取消、join rollback-only 拒绝、通知失败保留已提交、能力门禁四场景（JIT 与原生 link/run 同步验证）；Spec §4.4 矩阵与 AGENTS.md UoW 行同步为实施结果 | 无 | 保持四场景随原生门禁回归 | 已处理 |
| I-13 | 流程 | 「生成 CRUD 集成用例」的生成服务经 [UnitOfWorkMo] 走同一内核：本地由 Web/Generator 套件与 AOP 织入验收覆盖；完整生成 CRUD 端到端在 CI 集成套件（PG）执行 | 本地无 PG 完整链 | 以 CI 集成套件为端到端证据；如审查需要可补 SQLite 端到端 harness | 已知/接受 |
| I-14 | 交付整理（切片 5） | CAP lease 契约落地：`IUnitOfWorkTransactionLeaseProvider`/`UnitOfWorkTransactionLease`/`IUnitOfWorkTransactionIdentity`（只读身份，无生命周期操作；live view 失效）；EF 实现按「有效资源的 CurrentTransaction」签发（同一 Connection/Transaction 关联）；构造经 `InternalsVisibleTo(CrestCreates.Data.EFCore)` 限定 Provider 边界，业务/CAP 不可构造 | 无 | 交接文档 `docs/superpowers/specs/2026-10-10-issue-137-cap-handoff.md` 随 PR；`RegisterWithDistributedTransaction` 死扩展处置交 #125 | 已处理 |
| I-15 | 遗留（交 #125） | `DistributedTransaction` 包内 `UnitOfWorkIntegrationExtensions`/`ITransactionParticipant` 仍为死代码/内存抽象；本项不删除（不在范围） | 双轨风险若被误用 | #125 按交接文档决定删除或归档；不得与 lease 契约形成双轨 | 交 #125 |

## PR #139 审查修复轮（2026-10-12，R139-01…11）

对照 `docs/review/2026-10-12-pr-139-uow-implementation-review.md` 的逐项修复（审查方 11 条契约回归用例已纳入本仓库测试并由 11/11 失败转为 11/11 通过）：

| 审查项 | 修复 |
| --- | --- |
| R139-01 [P1] 未完成 join 不标记 rollback-only | `DisposeCore` 增加「非 owner 且 Active 的 join 退出 → 标记实际 owner rollback-only」；成功完成的 join 仅恢复环境。回归：`Abandoned_join_must_prevent_outer_commit` |
| R139-02 [P1] 显式/异常回滚不丢弃跟踪写入 | 所有未提交退出路径统一经 `TryTerminateUncommittedAsync`（优先 `IUnitOfWorkAbandonable`：终结事务 + 丢弃未 flush 跟踪写入；否则回滚）。回归：`Explicit_rollback_must_not_leak_pending_writes_into_next_uow` |
| R139-03 [P1] 租户校验被顶层/host 绕过 | 全部受管 scope（root/join/RequiresNew）在 token 帧记录租户快照；`ValidateDataAccess` 先于一切路由与提前返回执行，两端均有租户来源时按值（含 null=host）精确比较。回归：`Root_uow_must_reject_tenant_change_before_repository_access`、`Tenant_to_host_change_must_be_rejected_inside_isolation` |
| R139-04 [P1] 提交确认后被句柄释放异常改写 Unknown | EF/FreeSql 提交成功后的清理失败抛 `UnitOfWorkCommitCleanupFailureException`（次级诊断）；内核固定 `Committed` + `Failed`（清理未完成），不尝试回滚。回归：`Confirmed_commit_must_not_become_unknown_when_handle_dispose_fails` |
| R139-05 [P1] PG/ORM 套件未接入 CI | `ci.yml` 与 `full-validation.yml` 新增「ORM providers」与「LibraryManagement PG integration」两个测试步骤；PG 隔离用例增加实际事务级别断言；Spec/memory/PR 措辞改为「CI 门禁已接入，证据以最终 head CI run 为准」。回归/证据：CI run（最终 head） |
| R139-06 [P2] 终态窗口可继续写入 | token 源携带参与状态/释放标志；`ValidateDataAccess` 对终态/已释放 owner 帧拒绝新数据动作（结果仍可读）；`BeginScope` join 拒绝终态 owner。回归：`Completed_scope_must_reject_new_repository_writes` |
| R139-07 [P2] 释放失败替换业务根因/释放标志不完成 | `DisposeCore` 按阶段收集失败（abandon/restore/token-dispose/uow-dispose/owned-scope-dispose），各步骤尽可能执行，始终置 `IsReleased`；无主异常时抛 `UnitOfWorkCleanupException`（含阶段），有主异常时入口抑制传播。回归：`Cleanup_failure_must_not_replace_business_exception` |
| R139-08 [P2] join 丢失 owner token 继承 | token 选择改为「唯一来源直接复用（caller 或 owner）+ deadline 时链接」，不再返回会被误解的 null。回归：`Joined_scope_must_inherit_owner_cancellation_without_deadline` |
| R139-09 [P2] join Complete 绕过自身 deadline | join 成功记录前执行同等 `ThrowIfExecutionInterrupted`；失败进入统一参与者失败语义。回归：`Joined_scope_completion_must_observe_its_deadline` |
| R139-10 [P2] lease 公开面暴露生命周期句柄 | `IUnitOfWorkTransactionIdentity.NativeTransaction` 改为不可操作借用标记（不可强转原生类型）；`ResourceIdentity` 改为只读 `UnitOfWorkResourceIdentity`（声明类型 + 稳定实例标识）；原生句柄借用走新增 `IEfCoreTransactionBorrower` 适配边界。回归：`Public_lease_must_not_expose_lifecycle_handle` |
| R139-11 [P2] lease 签发未绑定受管窗口 | 签发前置：读方可见链存在 Active 且未释放的 owner 窗口（`TryGetCurrentManagedState`），原生未受管事务/独立 scope/终态一律拒绝。回归：`Lease_must_reject_transaction_outside_managed_scope` |

### 修复轮补充记录

| # | 类别 | 说明 | 处置 |
| --- | --- | --- | --- |
| I-16 | 流程（CI） | 新增 CI 步骤时 `name:` 值内含「`: `」（冒号+空格）会使 YAML 纯量截断、整个 workflow 解析失败（run 显示 0 jobs）且不产生明确错误信息 | 已移除步骤名中的冒号并本地 YAML 解析校验；后续在 CI 工作流中避免步骤名含冒号 |
| I-17 | 测试健壮性（CI 首跑暴露） | ORM 套件首次在 CI 执行时，基于「固定 `Task.Delay` + 定时器 `CancelAfter`」的 deadline 用例在负载下时序抖动（取消未在窗口内触发）；本地从未暴露（该套件此前未接入 CI） | 时序敏感用例改为**有界等待条件**（轮询直到 token 取消，超时 10s 后再断言；断言不变，判别力保留）；本地 3 轮复跑全绿 |
| I-18 | 遗留（CI 接线，预存问题，非本项引入） | 既有 LibraryManagement IntegrationTests 套件首次接入 CI 后整体失败：宿主**启动期** seeding 即查询 OpenIddict（`42P01 relation "OpenIddictScopes" does not exist`，栈：`OpenIddictEntityFrameworkCoreScopeStore.FindByNameAsync`）。根因类别：`HostMigrationAndSeedRunner` 对多 DbContext 依赖 `EnsureCreatedAsync` 的「仅第一次调用生效」语义，在已存在的库上不会为后续上下文（OpenIddict）建表；fixture 的建表步骤在宿主启动之后，永远无法先执行。该套件历史上从未接入任何 workflow，在干净库 CI 环境不可运行 | UoW PG 验收已改为**自包含 Testcontainers 类**（自带容器/DbContext，不依赖 legacy fixture），CI 以过滤方式真实执行；完整 legacy 套件待修复后再整体接入 | 交 CI/集成测试轨道：修复多上下文建表（runner 或 fixture 顺序）后放开 `ci.yml`/`full-validation.yml` 的过滤 |
| I-19 | 证据（CI，R139-05 收口） | 修复轮全部推送后，CI 在代码终点 `cea23075` 全绿：run 38192439617 **success**（26m29s）。新增两个步骤均真实执行并通过：「Test — Persistence / ORM providers (UnitOfWork kernel, chain, capabilities, AOP weave, CAP lease)」与「Test — UnitOfWork PostgreSQL acceptance (Testcontainers)」（自包含类：requiresNew 独立提交 + 隔离级别实测断言）；全部原生 publish/link/run 门禁通过；审查方 11 条契约回归用例在该 head 下本地与 CI 均 11/11 通过；I-16/I-17/I-18 的稳定化修复均已生效，无残余失败 | 该 run 即 R139-05 的 CI 证据（未再改动代码）；后续如有变更以新 head 的 CI run 为准 |

（后续切片发现问题将追加到本表；证据以最新 head 的 CI run 为准。）

## PR #139 第二轮审查修复（2026-10-12，R2-139-01…05）

对照 `docs/review/2026-10-12-pr-139-uow-implementation-review-round2.md` 的逐项修复（审查方 5 条探针原样纳入 `Pr139Round2ReviewerTests.cs`：修复前 5/5 失败 → 修复后 5/5 通过）：

| 审查项 | 修复 |
| --- | --- |
| R2-139-01 [P1] Provider 吞掉回滚异常、内核仍报成功回滚 | EF/FreeSql/SqlSugar 的 `AbandonPendingWork` 改为「尽力终结 + 如实上报」：**回滚无法确认**时抛 `UnitOfWorkAbandonFailureException`（原始终结故障保留为 InnerException，后续句柄释放/丢弃失败经 `SubsequentFailures` 保留）；回滚已确认但释放/丢弃失败抛 `UnitOfWorkAbandonCleanupFailureException`（与提交后清理失败语义对称）。内核 `TryTerminateUncommittedAsync` 把终结失败交回调用方：无法确认回滚不得宣称 RolledBack（State=Failed、事务结果转 Unknown）；回滚已确认的次级失败保持 RolledBack + Failed；显式回滚经 `IUnitOfWorkScope.RollbackTerminationFailure` 保留可检查，异常路径附加到业务主异常。回归：`Ef_rollback_failure_must_not_report_successful_rollback`（探针）+ `Explicit_rollback_termination_failure_must_not_report_rolled_back`、`Rollback_termination_failure_is_attached_to_business_root_cause` |
| R2-139-02 [P1] 顶层窗口放行第二逻辑资源 | 所有 owner 窗口在 `StartAsync` 记录选定资源的逻辑键（声明种类 + 连接配置）与实际资源/事务关联（新增 `IUnitOfWorkManagedResource`，EF 实现）；`ValidateDataAccess` 改为作用于**最近的可见 owner 帧**，读方在返回自身 Context 或路由前统一比较——第二 Context 声明/连接配置在根窗口同样确定性拒绝；保留「独立 DI 根不可见」语义。回归：`Root_scope_must_reject_second_logical_context` |
| R2-139-03 [P2] 清理次级诊断被空吞 | `ExecuteAsync` finally 把 `UnitOfWorkCleanupException` 附加到业务主异常 `Data`（键 `CrestCreates.UnitOfWork.CleanupException`），根因类型/栈保留；无主异常时照旧外抛；回滚终结失败同样附加（键 `…RollbackTerminationFailure`），终结方法不再「只返回 bool 丢弃原异常」。回归：`Business_exception_must_retain_cleanup_diagnostic` |
| R2-139-04 [P2] 原生 borrower 无受管窗口校验 | borrower 与 lease 签发共用单一受管事务准入权威 `TryGetCurrentManagedTransaction`（最近可见 owner 帧：已开始、事务型、Active、未释放、事务句柄非空，且解析到的 Context/Transaction 与 owner 开始记录为**同一引用关联**）；未受管原生事务、独立根一律拒绝。回归：`Native_borrower_must_reject_unmanaged_transaction`、`Borrower_and_lease_reject_independent_root_with_native_transaction`、`Borrower_and_lease_reject_replaced_transaction` |
| R2-139-05 [P2] lease 认领未 Start 的外部事务 | 同一权威取代 `TryGetCurrentManagedState`：载体 `HasStarted` 与事务句柄关联成为准入必要条件——BeginScope 后未 Start、非事务窗口中的外部事务、同资源上被替换的事务都不再被认领。回归：`Lease_must_not_adopt_native_transaction_before_scope_start`、`Borrower_and_lease_reject_not_started_carrier_with_native_transaction`、`Borrower_and_lease_reject_non_transactional_window_with_native_transaction` |

### 修复轮补充记录（R2）

| # | 类别 | 说明 | 处置 |
| --- | --- | --- | --- |
| I-20 | 整理（审查方指出） | `git diff --check a9da4dab..43b5c8a7` 报 `UnitOfWorkExceptions.cs` 文件末尾多余空白行 | 已清理；提交前 `git diff --check` 复查干净 |
| I-21 | 测试环境（I-02 复核） | 新增 lease/borrower 负例中「独立根」「第二实例」场景若共用同一 SQLite 文件会触发单写者 `database is locked`（30s 超时假失败） | 独立根场景改用独立数据库文件；「事务被替换」改为在同资源上手动终结并替换事务（无并发写者）；本地 118/118 复跑通过 |
| I-22 | 平台界面变化（交审查知悉） | 新增 `IUnitOfWorkManagedResource`（Provider 受管资源曝光）与 `IUnitOfWorkScope.RollbackTerminationFailure`（保留终结故障）；`UnitOfWorkExecutionContext.TryGetCurrentManagedState` 被 `TryGetCurrentManagedTransaction` 取代（切片 5 期间引入、未发布） | 属本 PR 未合并契约的强化，不构成对外兼容负担；公开面均无生命周期操作 |

## PR #139 第三轮审查修复（2026-10-12，R3-139-01…02）

对照 `docs/review/2026-10-12-pr-139-uow-implementation-review-round3.md` 的逐项修复（审查方 3 条探针原样纳入 `Pr139Round3ReviewerTests.cs`：修复前 0/3 通过 → 修复后 3/3 通过）：

| 审查项 | 修复 |
| --- | --- |
| R3-139-01 [P2] 已签发 lease 未随 owner 终态失效 | 签发时绑定**原 owner 载体**（`TryGetCurrentManagedTransaction` 新增只读 owner 输出，不重新解析「当前最近的 owner」）；`EfCoreTransactionIdentity.IsCompleted` 改为「owner 终态/已释放 **OR** 事务关联失效」——提交已确认但句柄清理失败（CurrentTransaction 仍指向原句柄）时旧 lease 立即失效；正常完成/回滚/释放、清理失败均覆盖，旧 lease 不随后续 scope 复活。回归：`Issued_lease_must_expire_when_confirmed_commit_cleanup_fails`（探针）+ `Issued_lease_must_invalidate_on_rollback_and_release`、`Old_lease_must_not_revive_with_subsequent_scope` |
| R3-139-02 [P2] 已确认回滚的清理故障结果不一致 | 新增共用分类 `ClassifyTerminationResult`（所有退出路径同源）：成功回滚保持 RolledBack；`UnitOfWorkAbandonCleanupFailureException`（回滚已确认、仅释放/丢弃失败）保持 **RolledBack + Failed** 且诊断保留；无法确认才 Unknown + Failed；提交已确认的 Unknown 不被随后回滚改写。flush 失败路径与 `TryAbandonOwner`（已开始 owner 直接 Dispose）改用同一分类，后者同时填充 `RollbackTerminationFailure`。回归：`Confirmed_rollback_must_survive_cleanup_failure_on_all_exit_paths`（探针，2 cases）+ `Confirmed_rollback_cleanup_failure_keeps_rolled_back_on_explicit_rollback` |

### 修复轮补充记录（R3）

| # | 类别 | 说明 | 处置 |
| --- | --- | --- | --- |
| I-23 | 契约文档（R3） | `IUnitOfWorkTransactionIdentity.IsCompleted` 语义文档同步为「owner 终态或已释放，或事务关联终结后为 true」；lease live validity 不依赖驱动清理成功 | 已更新；公开参与契约仍无生命周期操作 |
