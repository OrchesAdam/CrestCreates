# Issue #130 S4 编译期入口与 EF 正式组合（执行记录）

日期：2026-10-23；切片：S4；状态：S4 实现与门禁完成，等待审查；S5 未开始。
分支：`feat/issue-130-provider-matrix-soft-delete-20261022-104210`。
前置：S3 已放行；设计与计划 S4。

## 1. 执行结果

| 套件 | 结果 |
| --- | --- |
| CodeGenerator 全量 | **461 total / 461 passed / 0 failed / 0 skipped**（31m11s；原 3 条 G 红例已转绿） |
| 真实 MSBuild G 控制 | **3 / 3 passed**（非 EF FreeSql、未知 Dapper 均失败并带 CCCREPO001/002；EF 正控成功生成） |
| EF 正式生成 CRUD Q01（SQLite） | **4 / 4 passed**（查询、筛选/排序敏感性、写入 UoW、缺 UoW 负控） |
| EF 正式生成 CRUD Q01（PostgreSQL） | **1 / 1 passed**（Testcontainers PostgreSQL 16） |
| GeneratedCrud 全量（JIT） | **28 / 28 passed** |
| GeneratedCrud NativeAOT 门禁 | **3 / 3 passed**（1m44s；publish/link/run + sentinels） |
| PostgreSQL UoW 门禁 | **4 / 4 passed** |
| OrmProviders 全量 | **255 / 255 passed**（S3 证据保持） |
| Dependency Boundaries | 176 / 178；2 条为既有 ControlPlane evidence-ledger 守卫（#69），无关 |

所有测试串行执行，未并发跑套件。

## 2. 编译期入口（G）

[RepositoryGeneratorDiagnostics](../../src/Tooling/CrestCreates.CodeGenerator/RepositoryGenerator/RepositoryGeneratorDiagnostics.cs) 冻结并接线两个生成器：

- `EntitySourceGenerator`：非 EF（FreeSql/SqlSugar）报 **CCCREPO001**；未知 Provider 报 **CCCREPO002**；报告后直接返回，不再生成 `FreeSql*Repository`/`SqlSugar*Repository` 直连 SDK CRUD，也不默认 EF。
- `RepositorySourceGenerator`：对 `[GenerateRepository]/[GenerateEntity]` 的 Provider 同样报 Error 并跳过接口/注册；enum Provider 值先映射为名称（`2` → `FreeSql`），避免诊断误判。
- EF 正控继续生成接口、EF 仓储与映射。
- 真实 MSBuild 消费项目测试使用真实 `EntityAttribute`/分析器：非 EF/未知构建必须失败并含诊断，且不产出 SDK 仓储；EF 构建成功并产出 `EfCoreOrderRepository.g.cs`。
- 两个测试夹具实体（原 SqlSugar/FreeSql）改为 EF，避免测试项目自身携带已被拒绝的声明。

## 3. EF 正式生成 CRUD 关系型 Q01

在 [GeneratedCrudEfCoreRelationalTests](../../tests/Framework/Api/CrestCreates.GeneratedCrud.AotFixture.Tests/GeneratedCrudEfCoreRelationalTests.cs) 中，使用真实生成 `WidgetAppService`/DTO/typed query adapter，替换为 EF Core 仓储与 UoW：

- SQLite 与 PostgreSQL 各自独立数据库执行同一查询：decimal(Price) >、DateTime(CreationTime) >=、nullable string(Description) Contains、enum(Status) Equals，按 Name 排序并分页；断言 total/items 与真实 SQL（含 SELECT/LIMIT）。
- 写入通过生成 `CreateAsync` 经 `[UnitOfWorkMo]` 与 EF UoW 持久化，fresh context 回读。
- Widget 测试实体新增枚举字段以覆盖 enum 过滤；未修改生产运行时。

## 3.1 S4 R1 复审修复（2026-10-23）

审查 head `824da858` 指出 Q01 两项验收缺口，修复提交 `1f8e592e`：

- 查询数据加入只违反单一筛选的诱饵行，匹配行命名使显式排序与默认排序在非零页不同；主断言与 SQL 断言（Price/CreationTime/ORDER BY）更新，并新增逐项敏感性控制。
- 写入侧用测试包装 `IDataBaseContext` 观测受管事务，EF 拦截器在事务内 flush 后抛错并断言零残留；缺 UoW 的负控证明仓储自动提交会残留，主门禁会失败。
- 更新 Q01 5/5、CRUD JIT 28/28、NativeAOT 3/3（1m44s）、PG UoW 4/4、ORM 255/255、CodeGenerator 461/461。

逐项回复见 [S4 R1 response](2026-10-23-issue-130-s4-review-round1-response.md)。

## 4. 回归与证据

- Generator 全量 461/461、CRUD JIT 26/26、NativeAOT 3/3、PG UoW 4/4、ORM 255/255。
- 历史 S1/S2/S3 证据与审查探针保持通过；本切片未改变其结论。
- 未验证：SQLServer/MySQL、其他 ORM 数据库、ORM 包 trim/NativeAOT、EF CRUD 在 PostgreSQL 之外的数据库、in-flight SQL 取消、跨进程并发。
- S5 未开始：最终支持矩阵、迁移说明、memory.md 与证据索引。

结构化证据：[S4 JSON](2026-10-23-issue-130-s4-evidence.json)。
