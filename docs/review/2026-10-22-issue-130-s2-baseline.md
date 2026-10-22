# Issue #130 S2 删除语义与查询闭环（执行记录）

日期：2026-10-22；切片：S2；状态：S2 删除/查询闭环完成，S3 尚未开始。
分支：`feat/issue-130-provider-matrix-soft-delete-20261022-104210`。
前置：[S1 基线](2026-10-22-issue-130-s1-baseline.md)；设计 §3.1/§4.1/§4.2；计划 S2。

S2 的出口是**三个 Provider 的 D/A 真实 SQLite 矩阵通过**、既有适用测试继续通过；T/U 红例按计划留给 S3，未在本切片提前实现。

## 1. 执行结果

| 套件 | 结果 |
| --- | --- |
| OrmProviders（全量，含 R1 修复） | **193 total / 183 passed / 10 failed** |
| 其中 S1/S2 的 D/A 用例 | 全部通过（D01–D09、A01–A02） |
| 其中 S3 范围 T/U 红例 | 10 条，逐条列于 §5；失败原因均为 S3 设计项，不是回归 |
| 历史 124 条 | 全部通过 |
| Tenant/Soft filter generator 过滤集 | 8 / 8 passed |
| CodeGenerator 全量 | **453 total / 450 passed / 3 failed**（32m06s；3 红全部为 S1 约定 G 红例） |
| CodeGenerator 全量 | 见 §7（本切片修改了生成器，必须复跑） |

S2 期间新增 7 条正式用例（含两条 SQL 级 live-row 证据）：FreeSql/SqlSugar stamped predicate 删除先拒绝、两个 SDK `GetQueryable` 明确拒绝、EF UoW 延迟 flush 冲突在 `CompleteAsync` 抛 `CrestConcurrencyException`。

## 2. 实现变更

### 2.1 EF Core

- [EfCoreRepository](../../src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepository.cs) / [EfCoreRepositoryBase](../../src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepositoryBase.cs)
  - 两个 stamped id 删除入口改为「加载当前行 → 设置 original stamp → 推进新 stamp → tracked Deleted」；唯一 `AuditInterceptor` 完成 `IsDeleted/DeletionTime/DeleterId` 转换，不再调用 `ExecuteDeleteAsync`。
  - 加载时应用 EF 全局过滤（基类另有数据过滤器租户条件）；零行（不存在或被过滤隐藏）统一抛 `CrestConcurrencyException`，不泄漏其他租户/已删除行存在性。
  - stamped 类型的 `DeleteRangeAsync(entities/predicate)` 在任何读取/写入前抛 `NotSupportedException`；消息含 `stamp` 并指向单行 `DeleteAsync(id, expectedStamp)`。
  - 无 stamp 软实体重复删除：EF 实体入口跳过已 `IsDeleted` 实体；id 入口经软删除过滤器隐藏，保证不刷新删除审计。
- [EfCoreUnitOfWork](../../src/Persistence/CrestCreates.Data.EFCore/UnitOfWork/EfCoreUnitOfWork.cs)：flush 阶段 `DbUpdateConcurrencyException` 翻译为 `CrestConcurrencyException`，UoW 内延迟 flush 的冲突在 `CompleteAsync` 以平台异常报告。
- [TenantFilterSourceGenerator](../../src/Tooling/CrestCreates.CodeGenerator/TenantFilterGenerator/TenantFilterSourceGenerator.cs)：同一生成链新增强类型软删除过滤器：
  - `ISoftDelete` 实体生成 `HasQueryFilter(e => !e.IsDeleted)`；
  - 同时具备租户标记与软删除的实体在租户过滤器中组合为 `(tenant condition) && !e.IsDeleted`，避免第二次 `HasQueryFilter` 覆盖；
  - 每个过滤方法先 `modelBuilder.Model.FindEntityType(...) is null` 守卫，支持同进程多个 DbContext；
  - 租户与软删除分别注册到独立 registry。
- [SoftDeleteFilterRegistryStore](../../src/Persistence/CrestCreates.Data.EFCore/MultiTenancy/SoftDeleteFilterRegistryStore.cs) / [SoftDeleteFilterConfiguration](../../src/Persistence/CrestCreates.Data.EFCore/MultiTenancy/SoftDeleteFilterConfiguration.cs) / [CrestCreatesDbContext](../../src/Persistence/CrestCreates.Data.EFCore/DbContexts/CrestCreatesDbContext.cs)：生产 DbContext 先应用软删除过滤，再应用租户过滤器（组合实体由后者覆盖为含 `!IsDeleted` 的组合表达式）。host（无当前租户）也应用软删除过滤；host 的租户可见性仍是 S1 冻结的兼容差异，S3 收紧。模型构建完成后对「软删除实体仍无 query filter」做 fail-closed 检查，避免生成器/装配缺失时静默返回已删除行。

### 2.2 FreeSql / SqlSugar

- 五删除重载统一走 Provider 内部动作 [FreeSqlSoftDeleteOperation](../../src/Persistence/CrestCreates.Data.FreeSql/Repositories/FreeSqlSoftDeleteOperation.cs) / [SqlSugarSoftDeleteOperation](../../src/Persistence/CrestCreates.Data.SqlSugar/Repositories/SqlSugarSoftDeleteOperation.cs)：
  - 软实体：条件 UPDATE，设置 `IsDeleted/DeletionTime/DeleterId`，stamp 实体推进 `ConcurrencyStamp`；
  - 无软删除标记：保持物理 DELETE；
  - 一次操作一个 UTC/用户快照；不依赖 `CurdBefore`/`DataExecuting` 改写删除；
  - stamped 批次/谓词删除先抛 `NotSupportedException`（消息含 `stamp`），零写入。
- 查询默认 live-row：
  - FreeSql 仓储构造时注册 `ISoftDelete` 全局过滤；
  - SqlSugar 仓储查询统一经私有 `Queryable()` 加 `IsDeleted = 0`；`ConfigureAuditInterceptor` 也注册软删除过滤器。
- `GetQueryable()` 两个 SDK 均改为确定性 `NotSupportedException`，移除 `ToList().AsQueryable()` 假翻译；异步仓储方法保留。迁移说明：需要真实 SDK 查询时直接使用 SDK 查询 API 或异步仓储方法，不能依赖伪 IQueryable。
- 仓储构造函数新增可选 `ICurrentUserProvider`（审计快照来源）；S3 统一装配 typed adapter 并解决模块生命周期。

### 2.3 SqlSugar 细节

- `Where("Id = @Id ...")` 与 SqlSugar 自动主键参数冲突，软删除条件参数改名为 `@entityId/@entityStamp`。
- 未新增第二套查询过滤器实现：仓储本地 live-row 条件与全局过滤器语义一致，重复条件为幂等 AND。

## 3. D/A 红转绿

S1 的 21 条 D 红例与 3 条 D04 新增红例（S1 提交后补充）在 S2 全部转绿；A01/A02 保持绿。对应 S1 FQN：

| 组 | S1 红例 | S2 结果 |
| --- | --- | --- |
| D01 SDK 五重载 | `D01_FreeSql_*` ×4、`D01_SqlSugar_*` ×4 | 全部软删除/留行/删除审计通过 |
| D01 EF tracked | `D01_EfCore_tracked_delete_overloads_preserve_rows_and_write_delete_audit` | 通过 |
| D02 默认过滤 | `D02_EfCore_default_reads_hide_soft_deleted_rows`、`D02_SqlSugar_filtering_must_be_applied_by_production_read_paths` | 通过 |
| D03 stamped 删除 | EF 两个入口 + FreeSql + SqlSugar | 留行、推进 stamp、删除审计通过 |
| D04 幂等 | EF/FreeSql/SqlSugar 三条 | 第二次删除零刷新、行保留 |
| D08 批次预检 | EF entities/predicate、FreeSql/SqlSugar entities（+ 新增 SDK predicate） | 先拒绝、零写 |
| D09 IQueryable | 新增两个 SDK `GetQueryable` 拒绝 | 显式 `NotSupportedException` |

## 4. 故障控制与敏感性

- **物理删除故障控制**：S1 基线（提交 `2f3d64e4`）在旧实现上运行，D01/D03/D04/D08 对物理 DELETE 全部红；S2 改为条件 UPDATE 后转绿。这不是事后文本断言，而是版本间真实对照。
- **batch stamp 零写**：D08 断言抛 `NotSupportedException` 且原表 `IsDeleted = 0` 行数不变。
- **stale stamp 零写**：D06 EF/FreeSql/SqlSugar 均抛 `CrestConcurrencyException`，原行 `IsDeleted = 0`、stamp 不变。
- **UoW 延迟冲突**：`U01_EfCore_stale_stamp_delete_reports_conflict_at_complete` 在 `CompleteAsync` 观察到 `CrestConcurrencyException`，新连接回读行未变。
- **幂等删除**：D04 切换当前用户后再次删除，`DeleterId` 保持第一次值；SDK 行保留，EF 行保留。
- **租户/事务绑定故障控制**：对应 T/U 红例仍在，S3 修复后再做；本切片不宣称 T/U。

## 5. 仍未通过（S3 范围，10 条）

FQN 见 [S2 证据 JSON](2026-10-22-issue-130-s2-baseline-evidence.json)：
- T：EF forged detached update；FreeSql/SqlSugar forged update、跨租户 id 删除、过滤器捕获配置时租户（共 7 条）。
- U：SqlSugar 模块单例 client、`ISqlSugarClient` 缺失注册；FreeSql 预取消异常包装；SqlSugar 忽略预取消 token（共 4 条）。
- 这些不是 S2 出口项；S2 未提前修复，避免与 S3 的装配核心改造产生双轨。

## 6. 查询契约与兼容变化

| 变化 | 迁移 |
| --- | --- |
| SDK `GetQueryable/GetQueryableUnfiltered` 不再返回伪 IQueryable | 使用异步仓储方法或 SDK 原生查询；不新增通用 IQueryable 适配器 |
| SDK 软实体默认查询附加 live-row 条件 | 需要原始对照时在 fixture 使用独立 SDK/SQL 连接，不在业务查询调用 `IgnoreQueryFilters` 式绕过 |
| stamped 批次/谓词删除拒绝 | 逐行 `DeleteAsync(id, expectedStamp)`；无 stamp 实体批次继续支持 |
| EF 软删除过滤器由生成器提供，生产 `CrestCreatesDbContext` 应用 | 自定义 DbContext 按生成注册调用 `SoftDeleteFilterConfiguration.ApplyAll`；租户过滤器仍由 `ConfigureTenantDiscriminator` 应用 |
| EF UoW flush 冲突异常类型统一 | 调用方捕获 `CrestConcurrencyException`；不再依赖 `DbUpdateConcurrencyException` |
| FreeSql/SqlSugar 仓储 ctor 新增可选 `ICurrentUserProvider` | 现有子类源兼容；S3 装配 core 注册同一身份适配器 |

## 7. 复跑命令与结果

```bash
dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests
# 193 total / 183 passed / 10 failed (S3 T/U red baselines)

dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter FullyQualifiedName~TenantFilterSourceGeneratorTests
# 8 total / 8 passed
```

CodeGenerator 全量套件因本切片修改了生成器，已完成复跑：**453 total / 450 passed / 3 failed（32m06s）**；3 条失败全部是 S1 约定的 `CCCREPO001/002` G 红例，无无关回归。

## 7.1 R1 复审修复（2026-10-22）

审查精确 head `b7f1d349` 提出 3 项 P2：host 组合表达式括号、SDK 映射列、同名软实体方法冲突。修复提交 `d593d312`，逐项回复见 [R1 response](2026-10-22-issue-130-s2-review-round1-response.md)。

- 生成器：租户 OR 先加括号再 AND live-row；方法/索引标识改为全类型身份。
- SDK：FreeSql/SqlSugar 的 live-row、Id、stamp 条件改由 provider 映射元数据或强类型表达式生成；SqlSugar 查询过滤器按 client 去重注册。
- 回归：审查 ORM 探针 5/5、生成器探针 2/2；`Issue130MappedColumnTests` 4/4；全量 OrmProviders 193/183/10（10 仍为 S3 T/U）。

## 8. 下一步（S3）

1. 统一 FreeSql/SqlSugar 模块/扩展 typed 装配核心；SqlSugar scoped client + `ISqlSugarClient` 注册。
2. 持久租户归属校验（detached update / id delete / predicate delete）与 per-operation 租户过滤。
3. 取消来源：SDK 预取消 token 保持 `OperationCanceledException`（FreeSql 去包装、SqlSugar 传 CT），并验证连接可复用。
4. 复跑 T/U 矩阵与正式 runtime 原生门禁；本切片不改变 ORM trim/NativeAOT 的「未验证」声明。
