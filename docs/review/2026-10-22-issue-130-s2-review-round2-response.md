# Issue #130 S2 审查 R2 回复（剩余两项 P2 修复）

审查对象：精确 head `b4664572`；报告 [2026-10-22-issue-130-s2-review-round2.md](2026-10-22-issue-130-s2-review-round2.md)。
修复提交：`9071e99a`；R1 修复保持有效（`d593d312`、`666816c2`）。

## 1. 逐项映射

| Finding | 修复 | 证据 |
| --- | --- | --- |
| **R2-1 映射列名未引用**（`order` 保留字） | FreeSql 不再拼接任何物理列名：live-row 与 ConcurrencyStamp 条件改为**强类型接口表达式**（`Expression.Convert` 到 `ISoftDelete`/`IHasConcurrencyStamp` 后访问属性），由 FreeSql 翻译为映射列并自动引用；Id 继续用 `e.Id` 强类型谓词。SqlSugar 通过 `InstanceFactory.GetSqlBuilderWithContext(client).GetTranslationColumnName(mappedName)` 对 Id/stamp/live 全部标识符加 provider 引用（SQLite 为反引号），stamped `UpdateAsync` 同样覆盖 | 审查探针 `FreeSql_mapped_soft_delete_must_quote_identifiers(false/true)`、`SqlSugar_mapped_soft_delete_must_quote_identifiers(false/true)`：修复前 2/4，修复后 **4/4**。新增保留字回归 4 条：两 Provider 的 stamped update/delete（`select`/`where`/`order`）与 predicate 批量删除均通过；普通映射回归 6 条保持通过 |
| **R2-2 生成名称仍碰撞**（`A.B.Row` vs `A_B.Row`） | `MethodSuffix` 对完整类型身份做无歧义编码：ASCII 字母/数字保留，其余每个字符（含 `.`、`_`、`+`）编码为 `_XXXX`。`A.B.Row` → `A_002EB_002ERow`，`A_B.Row` → `A_005FB_002ERow`；定义、调用、索引名同步 | 审查探针 `Full_type_identity_encoding_must_distinguish_underscore_from_namespace_separator(false/true)`：修复前 1/2，修复后 **2/2**。新增显式名称测试断言两个方法同时存在且不再出现 `ConfigureA_B_RowSoftDeleteFilter` |

## 2. 行为控制

| 控制 | 修复前 | 修复后 |
| --- | --- | --- |
| R2 映射保留字探针 | 2 passed / **2 failed** / 4 | **4 passed / 0 failed** |
| R2 生成名称探针 | 1 passed / **1 failed** / 2 | **2 passed / 0 failed** |
| R1 ORM 探针 | 5/5 | 5/5（未回归） |
| R1 生成器探针 | 2/2 | 2/2（未回归） |
| 映射列回归（普通 + 保留字） | 6 | **10/10**（新增 4 条保留字 stamped/batch 控制） |
| OrmProviders 全量 | 195 / 185 / 10 S3 | **203 total / 193 passed / 10 S3 red / 0 skipped** |
| CodeGenerator 全量 | 455 / 452 / 3 G red | 见 §3（名称编码修复后复跑） |

10 条失败仍与既有 S3 T/U 清单逐一相同；未新增 S2 红例。

## 3. 复跑

```bash
dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests
dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests --filter "FullyQualifiedName~ReviewS2R2MappingProbes|FullyQualifiedName~ReviewS2Probes|FullyQualifiedName~Issue130MappedColumnTests"
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter "FullyQualifiedName~ReviewS2R2GeneratorProbes|FullyQualifiedName~TenantFilterSourceGeneratorTests"
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests
```

结构化结果：[R2 修复证据](2026-10-22-issue-130-s2-r2-fix-evidence.json)。

未声明：PostgreSQL/其他数据库、trim/NativeAOT、in-flight 取消；S3 仍未启动。
