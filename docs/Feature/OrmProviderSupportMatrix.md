# ORM Provider 支持矩阵（Issue #130 最终）

日期：2026-10-23；实现 SHA：`162bdbee`（分支 `feat/issue-130-provider-matrix-soft-delete-20261022-104210`），经 PR #154 于 2026-10-23 合入 master，merge commit `348c4d3c35b8682e6b47b6c2dd6058b046c1113a`；Issue #130 已按验收关闭。本矩阵是 ORM Provider 支持的唯一来源，其他文档（含[能力地图](CapabilityMap.md)）只链接、不复制。
设计：[Issue #130 设计](../superpowers/specs/2026-10-22-issue-130-provider-matrix-soft-delete-design.md)。S1–S5 证据见 [S1](../review/2026-10-22-issue-130-s1-baseline.md)、[S2](../review/2026-10-22-issue-130-s2-baseline.md)、[S3](../review/2026-10-22-issue-130-s3-tenant-assembly.md)、[S4](../review/2026-10-23-issue-130-s4-compile-entry-crud.md)。

状态术语：

- **supported（SQLite/JIT）**：该组合/能力在本仓库有真实执行证据；不等于其他数据库或其他执行模式。
- **experimental**：入口保留，整体证据未闭合；不得用于推导 supported。
- **unsupported**：执行前确定性拒绝，不静默降级。
- **unverified**：未验证；不等于已证明不支持。

版本：EF Core 10.0.7；Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1；FreeSql 3.5.308；SqlSugarCore 5.1.4.214；SQLite 独立文件；PostgreSQL 16（Testcontainers，podman）。

## 1. 能力矩阵

| 能力 / 组合 | EF Core | FreeSql | SqlSugar |
| --- | --- | --- | --- |
| 基础仓储 CRUD（SQLite，JIT） | **supported** | **supported（SQLite；整体 experimental）** | **supported（SQLite；整体 experimental）** |
| 默认 live-row 过滤（软删除隐藏） | **supported**：生成过滤器 + fail-closed 检查 | **supported（SQLite）**：仓储全局过滤 + 映射感知 | **supported（SQLite）**：每 client 查询过滤器 |
| 软删除五重载/留行/审计/stamp | **supported（SQLite）** | **supported（SQLite）** | **supported（SQLite）** |
| 重复无 stamp 删除幂等 | **supported（SQLite）** | **supported（SQLite）** | **supported（SQLite）** |
| stamped 批次/谓词删除 | **unsupported**（`NotSupportedException`，零写） | **unsupported**（同左） | **unsupported**（同左） |
| 租户读隔离（required/optional/host） | **supported（SQLite）**；PostgreSQL **unverified** | **supported（SQLite）**；PostgreSQL **unverified** | **supported（SQLite）**；PostgreSQL **unverified** |
| 持久租户写归属（detached/forged/跨租户） | **supported（SQLite）** | **supported（SQLite）** | **supported（SQLite）** |
| 共享 null 行 host 独占写 | **supported（SQLite）** | **supported（SQLite）** | **supported（SQLite）** |
| 租户 `UpdateRange` | **unsupported**（拒绝） | **unsupported**（拒绝） | **unsupported**（拒绝） |
| 租户批次 `DeleteRange`（entities/predicate） | **unsupported**（跟踪/写入前拒绝，零写） | **supported（SQLite）**：谓词/实体批次只作用于当前租户（stamped 批次仍拒绝） | **supported（SQLite）**：同左 |
| 创建/修改/删除审计字段 | **supported（SQLite）** | **supported（SQLite）** | **supported（SQLite）** |
| 两仓储共享 UoW 事务 + abandon/Complete（EF 仓储组合） | **supported（SQLite）**；PostgreSQL **unverified** | **supported（SQLite）**；PostgreSQL **unverified** | **supported（SQLite）**；PostgreSQL **unverified** |
| PostgreSQL 原始 Context UoW 门禁（既有 4/4：requiresNew 外层回滚 + 三档 isolation） | **supported（仅 raw Context）** | **unverified** | **unverified** |
| RequiresNew | **supported**（既有 EF/UoW 门禁） | **unsupported**（执行前拒绝） | **unsupported**（执行前拒绝） |
| 显式 isolation | SQLite Serializable **supported**；PostgreSQL 三档 **supported**（既有 #137 raw Context 证据） | **unsupported**（执行前拒绝） | **unsupported**（执行前拒绝） |
| 逐操作取消（命令启动前） | **supported** | **supported（SQLite）** | **supported（SQLite）** |
| in-flight SQL 取消 | **unverified** | **unverified** | **unverified** |
| generated CRUD typed query/filter/sort/page | **supported（SQLite + PostgreSQL 16，JIT）**；SQLServer/MySQL **unverified** | **unsupported**：编译期 `CCCREPO001` | **unsupported**：编译期 `CCCREPO001` |
| generated CRUD 写入 + UoW 参与/事务内失败回滚 | **supported（SQLite）**；PostgreSQL **unverified** | **unsupported** | **unsupported** |
| 未知 Provider 生成请求 | **unsupported**：`CCCREPO002` | **unsupported** | **unsupported** |
| SDK `GetQueryable` 伪 IQueryable | EF 原生 IQueryable **supported** | **unsupported**：明确 `NotSupportedException` | **unsupported**：明确 `NotSupportedException` |
| SqlSugar client 生命周期 | — | — | **supported（SQLite）**：scoped client + `ISqlSugarClient` 同实例 |
| 旧配置 hook（配置时租户/软删除） | — | **unsupported**：确定性 `NotSupportedException` | **unsupported**：确定性 `NotSupportedException` |
| SQLServer / MySQL | **unverified** | **unverified** | **unverified**（模块 SqlServer 硬编码不构成证据） |
| trim / NativeAOT | **unverified** | **unverified** | **unverified** |

## 1.0 R3-31 定向补充（2026-11-11）

- FreeSql builder 回调在 Build 前配置；原 `Action<IFreeSql>` 重载改为 `Action<FreeSqlBuilder>`，已构建实例重载保持不变。
- SqlSugar 接口工厂只注册 `ISqlSugarClient`；`ConnectionConfig` 重载继续提供同 scope 的具体 `SqlSugarClient`。
- FreeSql/SqlSugar 四种分页重载缺省按 Id 升序，显式排序后追加 Id 升序；旧 stamp 仍作不透明字符串精确匹配，新 stamp 统一 N 格式。
- `SqlSugarPostgresIntegrationTests` 18/18：PostgreSQL 16/JIT 下正式仓储软删入口，host/A/B、bool live-row 参数、stamp、留行/幂等及 stamped 批次拒绝。此为定向执行证据，不提升整个 SqlSugar PostgreSQL 组合的支持声明，不推导 UoW、取消或 NativeAOT。

## 1.0.1 R3-08 MongoDB 定向边界（2026-11-11）

此补充独立于上表的 EF/FreeSql/SqlSugar 组合，不提升该表任何支持等级。MongoDB 保留为 **experimental，standalone JIT**；执行面是 `MongoRepositoryBase<TEntity,TKey>`，驱动 MongoDB.Driver 3.4.0，真实服务器为 Podman/Testcontainers `mongo:7`，每例独立数据库。

- 所有普通读（同步 `IQueryable`、Get/List/Count/Any/Exists 与四种分页）组合 live-row 与租户条件，由 BSON serializer 解析具体成员映射。required 租户只读当前租户，host 无结果；optional 租户读自身与 null 共享行，host 只读 null；缺少 `ICurrentTenant` 按 host 处理。
- 普通写不允许跨租户或租户写共享 null 行。最终 update/delete 条件保留持久 ownership、live-row、适用的 expected stamp；不能靠 detached entity 提交的 TenantId 改换持久归属。软删除只 patch `IsDeleted`/`DeletionTime` 与适用的 stamp，不 replacement 写入调用方其他字段；未接入当前用户的删除人审计，不新增平台 Audit Logging 背书。
- 分页改为 **0-based**，负 index、非正 size、offset 溢出在 count/query 前拒绝。缺省按 Id 升序；显式排序追加 Id 升序作为 tie-breaker（直接按 Id 排序保留调用方方向）。这是固定数据集上的确定性排序，不承诺并发插入/删除时 offset 分页快照一致。
- 陈旧 stamp 的更新/单行删除抛 `CrestConcurrencyException`；新生成 stamp 为 GUID N 格式，旧 token 仍按不透明字符串精确比较。带 expected stamp 的删除在缺失/隐藏/已删除行时也按并发冲突拒绝；普通 id 删除隐藏行保持无操作。stamped 两种批量删除与 tenant `UpdateRange` 为 **unsupported**，数据库命令前拒绝；其他 standalone 批次不承诺原子性。
- **所有正式 managed UoW 窗口内的 Mongo 仓储操作均 unsupported**，包括未 Start、非事务型和事务型窗口。使用既有 `UnitOfWorkExecutionContext.ResolveParticipation` 在 DB 执行前拒绝；`GetQueryable` 获取及延迟执行、标量执行、枚举推进也受守卫约束。同步 IQueryable 保持单源服务端 LINQ 组合；返回值不再暴露 Mongo 专用 query provider，异步查询使用仓储 async 方法。租户上下文变化后旧 query 拒绝执行。
- R3-08 审查修复：guarded 根保留在表达式树中，执行 visitor 收集并校验每个 guarded source 后再解包为原生表达式。多个 guarded source 的组合（包括同租户 `Concat`/`Union` 与标准 `Join`）当前 **unsupported**，在 Mongo command 前确定性拒绝；任一源租户上下文变化同样拒绝。单源投影、标量和 untyped 重建不受该多源限制。未新增公开 API 或 runtime reflection fallback。
- 未新增 Mongo session/transaction binding，不承诺 replica-set 事务、Mongo/EF 跨资源事务、领域事件/UoW 组合或 generated CRUD 组合；trim/NativeAOT **unverified**，其他服务器/驱动/RID 未验证。

验证入口：`tests/Persistence/CrestCreates.MongoDB.Tests/MongoSemanticsIntegrationTests.cs`。包含 host/A/B 全读重载、BSON 重命名、分页/非法参数零命令、真实留行/stamp/幂等回读、伪造归属、共享写拒绝、最终写前归属/stamp 竞态、managed 窗口所有公开重载零 Mongo command 观察，以及多源双租户切换、同租户多源拒绝、标准 Join 拒绝、managed 多源零命令和单源兼容回归。managed 测试使用正式 `UnitOfWorkManager` 内核与测试工厂（EfCore provider 身份），不等同于真实 EF/Mongo 事务组合验证。

本轮执行：SDK `/home/orches/.dotnet/dotnet` 10.0.114，`-m:1 -p:UseSharedCompilation=false`，所有测试进程串行；Mongo 项目 **76/76**（58 条真实 Mongo 集成 + 18 条单元），exit 0、零跳过（`/tmp/r308-mongo-multisource-final.log`）；TenantIdentity/CapabilityMap 守卫复跑 **50/50**，exit 0、零跳过（`/tmp/r308-multisource-boundary.log`）。多源修复前定向真实 Mongo 回归 **8 failed / 0 passed**（`/tmp/r308-multisource-red.log`，其中单源用例当时因不兼容的 `Cast` 断言失败，后续改为直接枚举 untyped query）；修复后定向 **9/9**（`/tmp/r308-multisource-green.log`）。此前 R3-08 主修复的精确基线 `765516b8` 三条负例实跑 **3 failed / 0 passed**（删除泄漏、0 页、并发异常类型）。命令、环境、日志与镜像身份见 `memory.md` 的 R3-08 条目。

## 1.1 最终审查 R1 修复（2026-10-23）

- EF 两个入口的无 stamp 租户批次写（UpdateRange、实体/谓词 DeleteRange）在跟踪/写入前确定性拒绝，失败零写；租户单行仍走持久归属校验。
- 平台 `EfCoreRepositoryBase.GetQueryable()` 改为从受生成过滤器约束的原生查询起步（required 租户条件仅叠加在已过滤查询上），普通读保留 tenant/live-row 组合；`GetQueryableUnfiltered` 仅保留给内部持久快照。
- 支持矩阵拆分 PostgreSQL 范围：PG generated **查询** supported；PG generated **写入/UoW 回滚** 与 EF 两仓储 abandon/Complete **unverified**；既有 PG 4/4 仅覆盖 raw Context requiresNew/isolation。
- 最终 ORM **267/267**（含 10 条最终审查探针，10/10）；GeneratedCrud 项目总 28 = 25 非原生 + 3 NativeAOT。最终复审 R2 已独立确认该范围并按本矩阵放行合并。

## 2. 证据索引

### 2.1 命令与结果（最终 head `2ea73e0d`；实现 SHA `162bdbee`）

| 集合 | 命令 | 结果 |
| --- | --- | --- |
| ORM 全量 | `dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests` | **267 / 267 passed，0 skipped** |
| 生成器全量 | `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests` | **461 / 461 passed**（31m11s） |
| GeneratedCrud 非原生 | `dotnet test tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Tests --filter "Category!=NativeAotGate"` | **25 / 25 passed**（项目总 28 = 25 非原生 + 3 NativeAOT，不把 28 当作独立 JIT 计数） |
| GeneratedCrud NativeAOT | `dotnet test ... --filter "Category=NativeAotGate"` | **3 / 3 passed**（1m44s） |
| PostgreSQL UoW | `dotnet test tests/Framework/Web/CrestCreates.IntegrationTests --filter FullyQualifiedName~UnitOfWorkPostgresIntegrationTests` | **4 / 4 passed** |
| Dependency Boundaries | `dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests` | 176 / 178（2 条既有 #69 evidence-ledger，无关） |

全部测试串行执行。

### 2.2 代码与测试 FQN

- EF 仓储/基类：[EfCoreRepository](../../src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepository.cs)、[EfCoreRepositoryBase](../../src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepositoryBase.cs)；测试 `CrestCreates.OrmProviders.Tests.Issue130.Issue130DeleteContractBaselineTests.D01_EfCore_*`、`...D03_EfCore_*`、`...T_*`、`...A03_EfCore_*`。
- EF 生成过滤器：[TenantFilterSourceGenerator](../../src/Tooling/CrestCreates.CodeGenerator/TenantFilterGenerator/TenantFilterSourceGenerator.cs)、[SoftDeleteFilterConfiguration](../../src/Persistence/CrestCreates.Data.EFCore/MultiTenancy/SoftDeleteFilterConfiguration.cs)；测试 `CrestCreates.CodeGenerator.Tests.TenantFilterGenerator.TenantFilterSourceGeneratorTests.*`、`...ReviewS2GeneratorProbes.*`、`...ReviewS2R2GeneratorProbes.*`。
- FreeSql：[FreeSqlRepository](../../src/Persistence/CrestCreates.Data.FreeSql/Repositories/FreeSqlRepository.cs)、[FreeSqlSoftDeleteOperation](../../src/Persistence/CrestCreates.Data.FreeSql/Repositories/FreeSqlSoftDeleteOperation.cs)、[FreeSqlUnitOfWorkRegistration](../../src/Persistence/CrestCreates.Data.FreeSql/Extensions/FreeSqlUnitOfWorkRegistration.cs)；测试 `...Issue130DeleteContractBaselineTests.D01_FreeSql_*`、`...Issue130TenantIsolationTests.T_FreeSql_*`、`...Issue130S3AssemblyTests.U01_FreeSql_*`、`...Issue130MappedColumnTests.D01_FreeSql_*`。
- SqlSugar：[SqlSugarRepository](../../src/Persistence/CrestCreates.Data.SqlSugar/Repositories/SqlSugarRepository.cs)、[SqlSugarSoftDeleteOperation](../../src/Persistence/CrestCreates.Data.SqlSugar/Repositories/SqlSugarSoftDeleteOperation.cs)、[SqlSugarServiceCollectionExtensions](../../src/Persistence/CrestCreates.Data.SqlSugar/Extensions/SqlSugarServiceCollectionExtensions.cs)；测试 `...D01_SqlSugar_*`、`...T_SqlSugar_*`、`...Issue130S3AssemblyTests.U01_SqlSugar_*`、`...U03_SqlSugar_*`、`...U07_SqlSugar_*`。
- 租户/审计/取消/映射回归：`CrestCreates.OrmProviders.Tests.Issue130.Issue130TenantIsolationTests.*`、`...Issue130S3AssemblyTests.A03_*`、`...ReviewS3Probes.*`、`...ReviewS3R2Probes.*`、`...ReviewS3R3Probes.*`、`...Issue130MappedColumnTests.*`。
- 生成期诊断：`CrestCreates.CodeGenerator.Tests.RepositoryGenerator.Issue130ProviderDiagnosticsBaselineTests.*`、`...Issue130ProviderDiagnosticsMsBuildTests.*`；EF 生成 CRUD Q01：`CrestCreates.GeneratedCrud.AotFixture.Tests.GeneratedCrudEfCoreRelationalTests.Q01_*`。
- PostgreSQL UoW：`CrestCreates.IntegrationTests.UnitOfWorkPostgresIntegrationTests.*`。

### 2.3 数据库与驱动

- SQLite：各 Provider 独立临时文件；原始行读取使用独立 `Microsoft.Data.Sqlite` 连接。
- PostgreSQL：Testcontainers `postgres:16-alpine`；EF Core 10.0.7 + Npgsql 10.0.1。
- FreeSql 3.5.308（`FreeSql.Provider.Sqlite`）；SqlSugarCore 5.1.4.214。

## 3. 迁移说明（兼容变化）

1. **软删除成为真实逻辑删除**：`ISoftDelete` 实体的五个 Delete 重载保留行并写删除字段/stamp；无软删除标记的实体仍物理删除。重复无 stamp 删除不刷新审计。
2. **stamped 批次/谓词删除被拒绝**：`DeleteRangeAsync` 对 `IHasConcurrencyStamp` 实体抛 `NotSupportedException`（零写）；逐行 `DeleteAsync(id, expectedStamp)`。
3. **SDK `GetQueryable` 不再返回伪 IQueryable**：抛 `NotSupportedException`；使用异步仓储方法或 SDK 原生查询。
4. **SDK 租户语义**：required 租户 host 读取 fail closed；optional 租户 host 仅 shared null 行；普通租户不能写 shared 行；`UpdateRange` 对租户实体拒绝。
5. **EF 租户过滤器**：required host=0 行；DataFilter optional host=shared only；EF 专用 optional marker 保持 host=all 兼容差异（尚未收紧；需消费者清单和迁移说明）。
6. **生成器**：非 EF/未知 Provider 的生成仓储/CRUD 请求报 `CCCREPO001/002` Error；不再生成直连 SDK CRUD，也不默认 EF。使用受验证的显式异步仓储入口。
7. **装配**：SqlSugar client 由 singleton 改为 scoped，并注册同一实例 `ISqlSugarClient`；FreeSql 扩展停止仓储程序集扫描，显式注册闭合泛型；仓储审计由 Provider 内部动作完成，业务无需手动 `ConfigureAuditInterceptor`。
8. **旧弱 hook**：FreeSql `ConfigureMultiTenantFilter`/`ConfigureSoftDelete`、SqlSugar `ConfigureMultiTenantFilter` 已改为确定性 `NotSupportedException`，避免配置时捕获租户或空软删除 hook；替代为仓储构造时传入 `ICurrentTenant` 与仓储内部逻辑。
9. **审计**：创建人来自唯一 `ICurrentUserProvider`；调用方预填创建字段被覆盖，普通更新不写回创建字段。

## 4. 未验证 / 不承诺

- SQLServer/MySQL 及其他数据库组合；PostgreSQL 的软删除/租户/审计/取消细节（仅 raw Context UoW 4/4 与 generated CRUD **查询** Q01 有 PG 证据；PG 写入/UoW 回滚与 EF 两仓储组合未验证）。
- ORM 包 trim/NativeAOT；CRUD NativeAOT 门禁是内存 fixture，不是 EF/ORM 原生证据。
- in-flight SQL 取消、跨进程并发、per-handler checkpoint。
- EF 专用 optional marker 的 host 收紧。
- 本矩阵不因 SQLite 通过推导其他数据库或其他执行模式。

## 5. 仓储实现演进策略（2026-11-05 决定）

第二轮整体架构审查（P1-5）要求对 FreeSql/SqlSugar 仓储的同构复制做二选一治理（抽取共享实现，或显式声明独立演进）。本矩阵选择**显式声明**：

- **决定**：`FreeSqlRepository` 与 `SqlSugarRepository`（当前 976 / 1068 行）保留为**各自独立实现**，不抽取共享基类；这是有意决策，不是待办。
- **理由**：两者的差异不是模板重复，而是 SDK 语义差异——查询构造（FreeSql `ISelect`/表达式解析 vs SqlSugar `ISugarQueryable`）、软删除条件 UPDATE、审计字段/stamp 写入、租户过滤与 `GetQueryable` 拒绝策略都由各 SDK 的 API 和限制决定。抽取共享层需要新增一层 provider 抽象，反而会把唯一有执行证据的路径移出矩阵已覆盖范围，且没有第二个消费者证明这层抽象稳定。
- **维护约束**：行为契约（删除重载语义、无 stamp 幂等、租户读写归属、审计字段、取消、批次删除拒绝）变更时，两仓储必须在同一变更中同步修改，并由既有 `Issue130*` 套件（本矩阵 §2）作为行为基线；不得只改一侧。
- **退出条件（何时重新评估）**：任一仓储超过约 1500 行、新增第三个 SDK Provider、或同一缺陷在两份实现中重复出现两次，则改为抽取共享执行内核；届时必须先补齐与现有证据等价的回归套件。
- **边界**：本决策不改变任何 supported/unsupported 判定，也不推导 trim/NativeAOT 或非 SQLite 证据。
