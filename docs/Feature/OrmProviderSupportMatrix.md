# ORM Provider 支持矩阵（Issue #130 最终）

日期：2026-10-23；实现 SHA：`e57abee5`（分支 `feat/issue-130-provider-matrix-soft-delete-20261022-104210`，PR #154；**尚未合并**）。
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
| 创建/修改/删除审计字段 | **supported（SQLite）** | **supported（SQLite）** | **supported（SQLite）** |
| 两仓储共享 UoW 事务 + abandon/Complete | **supported（SQLite）**；PostgreSQL **supported**（既有 4/4） | **supported（SQLite）**；PostgreSQL **unverified** | **supported（SQLite）**；PostgreSQL **unverified** |
| RequiresNew | **supported**（既有 EF/UoW 门禁） | **unsupported**（执行前拒绝） | **unsupported**（执行前拒绝） |
| 显式 isolation | SQLite Serializable **supported**；PostgreSQL 三档 **supported**（既有 #137 证据） | **unsupported**（执行前拒绝） | **unsupported**（执行前拒绝） |
| 逐操作取消（命令启动前） | **supported** | **supported（SQLite）** | **supported（SQLite）** |
| in-flight SQL 取消 | **unverified** | **unverified** | **unverified** |
| 正式 generated CRUD + typed query + UoW | **supported（SQLite + PostgreSQL 16，JIT）**；SQLServer/MySQL **unverified** | **unsupported**：编译期 `CCCREPO001` | **unsupported**：编译期 `CCCREPO001` |
| 未知 Provider 生成请求 | **unsupported**：`CCCREPO002` | **unsupported** | **unsupported** |
| SDK `GetQueryable` 伪 IQueryable | EF 原生 IQueryable **supported** | **unsupported**：明确 `NotSupportedException` | **unsupported**：明确 `NotSupportedException` |
| SqlSugar client 生命周期 | — | — | **supported（SQLite）**：scoped client + `ISqlSugarClient` 同实例 |
| 旧配置 hook（配置时租户/软删除） | — | **unsupported**：确定性 `NotSupportedException` | **unsupported**：确定性 `NotSupportedException` |
| SQLServer / MySQL | **unverified** | **unverified** | **unverified**（模块 SqlServer 硬编码不构成证据） |
| trim / NativeAOT | **unverified** | **unverified** | **unverified** |

## 2. 证据索引

### 2.1 命令与结果（实现 SHA `e57abee5`）

| 集合 | 命令 | 结果 |
| --- | --- | --- |
| ORM 全量 | `dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests` | **257 / 257 passed，0 skipped** |
| 生成器全量 | `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests` | **461 / 461 passed**（31m11s） |
| GeneratedCrud JIT | `dotnet test tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Tests` | **28 / 28 passed** |
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

- SQLServer/MySQL 及其他数据库组合；PostgreSQL 的软删除/租户/审计/取消细节（仅 UoW 与 generated CRUD Q01 有 PG 证据）。
- ORM 包 trim/NativeAOT；CRUD NativeAOT 门禁是内存 fixture，不是 EF/ORM 原生证据。
- in-flight SQL 取消、跨进程并发、per-handler checkpoint。
- EF 专用 optional marker 的 host 收紧。
- 本矩阵不因 SQLite 通过推导其他数据库或其他执行模式。
