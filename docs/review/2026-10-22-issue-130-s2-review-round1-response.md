# Issue #130 S2 审查 R1 回复（三项 P2 修复）

审查对象：精确 head `b7f1d349`；审查报告 [2026-10-22-issue-130-s2-review-round1.md](2026-10-22-issue-130-s2-review-round1.md)。
修复提交：`d593d312`（生成器/映射修复、探针入库与测试隔离）、`666816c2`（stamped update 的同类映射条件补强）。

## 1. 逐项映射

| Finding | 修复 | 证据 |
| --- | --- | --- |
| **R1-1 host 绕过软删除过滤** | `AppendTenantFilter` 在拼接 live-row 前把租户 OR 整体加括号，生成 `(currentTenant.Id == null \|\| e.TenantId == currentTenant.Id) && !e.IsDeleted`；host 与 tenant 都先应用软删除过滤器，组合实体再由租户组合表达式覆盖 | 审查探针 `Combined_filter_keeps_soft_deleted_rows_hidden_even_for_host(null/"tenant-a")` 2/2 通过；生成器字符串控制改为断言完整括号表达式，不再剥括号 |
| **R1-2 SDK 忽略列映射** | FreeSql 通过 `CodeFirst.GetTableByEntity(typeof(TEntity)).ColumnsByCs[...].Attribute.Name` 解析 live-row/stamp/Id 物理列；SqlSugar 通过 `EntityMaintenance.GetDbColumnName(property, type)` 解析，并新增 per-client `TableFilterItem<ISoftDelete>` 映射感知查询过滤器；所有删除重载与查询统一走映射列/强类型谓词 | 审查探针 `FreeSql_soft_delete_respects_mapped_soft_flag_column`、`SqlSugar_query_and_delete_respect_mapped_soft_flag_column(false/true)` 3/3 通过；新增 `Issue130MappedColumnTests` **6/6**，覆盖两 Provider 的 Get/Count/Any/Exists、entity/id/stamp/range/predicate 删除、stamped update 及 `deleted_flag/row_id/stamp_col` 映射 |
| **R1-3 同名实体阻断编译** | 生成方法名、调用名与索引名统一使用 `FullName` 转义出的确定性标识（`.`/`+` → `_`），不再只用 `Name`；保留 `global::{FullName}` 类型身份 | 审查探针 `Soft_entities_in_different_namespaces_must_compile(false/true)` 2/2 通过；原 `TenantFilterSourceGeneratorTests` 8/8 通过 |

## 2. 行为控制结果

| 控制 | 修复前 | 修复后 |
| --- | --- | --- |
| ORM 审查探针 | 1 passed / **4 failed** / 5 | **5 passed** / 0 failed |
| 生成器审查探针 | 1 passed / **1 failed** / 2 | **2 passed** / 0 failed |
| Tenant/Soft filter 生成器过滤集 | 8/8（原字符串测试无法识别优先级） | **10/10**（含新增括号断言与审查探针） |
| OrmProviders 全量 | 174 passed / 10 S3 red / 184 | **185 passed / 10 S3 red / 195** |
| 新增映射列回归 | — | **6 passed**，覆盖全部删除重载、查询与 stamped update |
| CodeGenerator 全量 | 453 / 450 / 3 G red | **455 / 452 / 3 G red（30m51s）** |

10 条失败仍为 S3 延期项（T/U），未新增 S2 红例。

## 3. 测试隔离说明

审查探针 `ReviewS2Probes` 入库时仅增加 `[Collection("ModelFilterRegistry")]` 属性，逻辑逐字保留；原因是 `EfCoreDbContextRegistrationTests` 会临时 `TenantFilterRegistryStore.Clear()`，进程级 registry 在并行测试下会产生顺序依赖（全量运行曾使 tenant 正向控制失败）。同时把 `EfCoreDbContextRegistrationTests` 改为快照/恢复注册，而非清空后不恢复。生成器探针未改动。

## 4. 复跑与证据

```bash
dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests
dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests --filter FullyQualifiedName~ReviewS2Probes
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~TenantFilterSourceGeneratorTests|FullyQualifiedName~ReviewS2GeneratorProbes"
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests
```

结构化结果：[S2 R1 evidence](2026-10-22-issue-130-s2-r1-fix-evidence.json)。

未声明：PostgreSQL/其他数据库、trim/NativeAOT、in-flight 取消；S3 仍未启动。
