# Issue #130 S3 租户、身份与 UoW 装配（执行记录）

日期：2026-10-22；切片：S3；状态：S3 实现完成，等待审查；S4 未开始。
分支：`feat/issue-130-provider-matrix-soft-delete-20261022-104210`。
前置：[S2 记录](2026-10-22-issue-130-s2-baseline.md)（R1/R2 已闭合）、设计与计划 S3。

S3 出口：T/A/U/R 真实 SQLite 矩阵通过；租户/事务故障控制归因正确；既有适用测试继续通过。未验证范围明确保持「未验证」。

## 1. 执行结果

| 套件 | 结果 |
| --- | --- |
| OrmProviders（全量） | **216 total / 216 passed / 0 failed / 0 skipped** |
| 原 S1/S2 的 10 条 T/U 红例 | 全部转绿 |
| 新增 S3 租户矩阵 | 9 / 9 passed |
| 新增 S3 装配/事务/取消矩阵 | 4 / 4 passed |
| R1/R2 审查探针 | ORM 9/9、生成器 4/4（未回归） |
| 映射列回归 | 10 / 10（未回归） |
| CodeGenerator 全量 | 后台复跑中，结果同步到 JSON 与 PR |
| Dependency Boundaries | 176 / 178；2 条为既有 ControlPlane evidence-ledger 守卫（#69），与本切片无关 |

## 2. 实现变更

### 2.1 SDK 逐操作租户隔离

[FreeSqlRepository](../../src/Persistence/CrestCreates.Data.FreeSql/Repositories/FreeSqlRepository.cs) / [SqlSugarRepository](../../src/Persistence/CrestCreates.Data.SqlSugar/Repositories/SqlSugarRepository.cs) 新增可选 `ICurrentTenant`：

- 所有普通查询（List/Get/Count/Any/Exists/Paged）在每次操作时解析当前租户，不再依赖配置时捕获的全局过滤器。
- `IMustHaveTenant`：租户上下文只读自身；host/null 上下文 **fail closed**（抛 `InvalidOperationException`），不自动获得全租户视图。
- 可选 `DataFilter.IMultiTenant`：租户 A 读 A + null 共享行；host 只读 null 共享行；A→B 切换即时生效。
- 写路径：
  - 插入：必填租户实体自动填充当前租户并拒绝显式其他租户；可选租户空值填当前租户，host 保留 null 共享行。
  - 更新/删除：先通过 `DisableGlobalFilter` / `ClearFilter` 读取**持久行租户**，与当前租户比对；不一致抛确定性异常且业务表零变化。EF 两个仓储同样在 `UpdateAsync`/`DeleteAsync(entity)` 前做持久归属校验。
  - stamped 批次/谓词删除仍先拒绝；SDK 谓词/批次删除追加租户条件，不触及其他租户行。
  - `UpdateRangeAsync` 对租户标记实体明确拒绝（`NotSupportedException`），避免批量绕过持久归属校验。
- SqlSugar 条件继续使用映射感知列名 + provider 引用；FreeSql 条件继续使用强类型表达式，保持 R1/R2 的映射/保留字修复。

### 2.2 EF 生成过滤器语义分层

[TenantFilterSourceGenerator](../../src/Tooling/CrestCreates.CodeGenerator/TenantFilterGenerator/TenantFilterSourceGenerator.cs) 区分三种租户标记：

| 标记 | 生成过滤器 | host/null |
| --- | --- | --- |
| `IMustHaveTenant` | `currentTenant.Id != null && e.TenantId == currentTenant.Id` | 无行（fail closed） |
| `DataFilter.IMultiTenant`（可空） | `(currentTenant.Id != null && (e.TenantId == currentTenant.Id || e.TenantId == null)) \|\| (currentTenant.Id == null && e.TenantId == null)` | 仅 null 共享行 |
| EF 专用 `Data.EFCore.MultiTenancy.IMultiTenant` | 保留 `currentTenant.Id == null \|\| e.TenantId == currentTenant.Id` | 兼容差异：host 仍可见全部租户；收紧需消费者清单与迁移说明，未在本切片悄改 |

软删除条件继续与租户表达式组合（括号优先），并由 `EnsureSoftDeleteFiltersAreApplied` fail closed。

### 2.3 装配核心、身份与生命周期

- 新增 [SqlSugarServiceCollectionExtensions](../../src/Persistence/CrestCreates.Data.SqlSugar/Extensions/SqlSugarServiceCollectionExtensions.cs)：唯一装配核心注册 **scoped** `SqlSugarClient`、同一实例的 `ISqlSugarClient`、scoped UoW、Provider binding 与身份适配器；模块与扩展都委托它。
- [FreeSqlUnitOfWorkRegistration](../../src/Persistence/CrestCreates.Data.FreeSql/Extensions/FreeSqlUnitOfWorkRegistration.cs)：统一 FreeSql 模块与两个扩展重载的 UoW/binding 装配。
- 新增 [CurrentUserProviderAdapter](../../src/Persistence/CrestCreates.Data.Core/CurrentUserProviderAdapter.cs)：`ICurrentUser → ICurrentUserProvider` 的唯一 typed adapter；装配时若存在 `ICurrentUser` 使用它，否则回退到 `DefaultCurrentUserProvider`，不建立第二用户目录。
- FreeSql 扩展不再调用 `AddFreeRepository` 扫描；`repositoryAssembly` 参数保留源兼容但不再扫描，仓储应显式闭合泛型注册。
- SqlSugar 模块从 singleton client 改为 scoped client + `ISqlSugarClient` 注册；仓储与 UoW 注入同一 client。
- 取消：两个 SDK 仓储的每个入口先检查调用方 token，预取消保持 `OperationCanceledException`；SqlSugar 追加连接可复用控制。

## 3. T/A/U/R 矩阵与故障控制

| 组 | 证据 |
| --- | --- |
| T 租户读 | `T_FreeSql/SqlSugar_optional_tenant_reads_shared_rows_and_switches_per_operation`；`T_FreeSql/SqlSugar_required_tenant_host_reads_fail_closed`；`T_EfCore_generated_filters_enforce_required_and_optional_host_semantics` |
| T 写归属 | `T_*_forged_detached_update...`、`T_*_cross_tenant_id_delete...`（原红例转绿）；`T_*_insert_fills_required_tenant_and_rejects_foreign_tenant`；`T_*_range_delete_only_touches_the_current_tenant` |
| A 审计 | S2 的 A01/A02 6 条保持通过；身份适配器不改变审计来源 |
| U 事务/取消 | `U01_*_two_repositories_share_uow_and_abandon/complete`；`U01_SqlSugar_extension_*`；`U03_SqlSugar_extension_registers_scoped_client_and_interface_alias`；`U03_SqlSugar_module_*`；`U07_*_honors_a_pre_cancelled_token`；`U07_SqlSugar_cancellation_does_not_poison_the_connection` |
| R 装配 | 既有 `UnitOfWorkDiRegistrationTests`/`AmbientChainAndOwnershipTests`/`ProviderCapabilityTests` 全绿；新增 extension/module 一致性控制 |

故障归因：
- 去掉租户条件 → T 读/写矩阵失败（reds 已在 S1/S2 版本证明并对 S3 版本转绿）。
- 解除仓储事务绑定 → U01 abandon/complete 控制失败（S1 物理删除版本与既有 UoW 内核测试提供对照）。
- host 必填租户读取 → 明确 `InvalidOperationException`，不是返回空或全租户。
- 跨租户写 → 持久归属校验先于 SQL 写入，原行零变化。

## 4. 兼容变化与迁移

| 变化 | 迁移 |
| --- | --- |
| SDK 必填租户 host 读取抛异常 | host 管理需显式治理上下文；不要用普通 host scope 作为 bypass |
| SDK 可选租户 host 只读 null 共享行 | 共享行由 host 创建/维护；租户可见自身 + 共享 |
| EF 必填租户 host 查询无行 | 迁移说明 + 负例见上；需要跨租户查看时使用显式治理入口 |
| EF 专用 optional marker 仍 host=all | 兼容差异保留；收紧需消费者清单、迁移说明和真实负例（未在本切片改变） |
| `UpdateRangeAsync` 租户实体拒绝 | 逐条 `UpdateAsync` 校验持久归属 |
| FreeSql 扩展不再扫描仓储程序集 | 显式注册闭合泛型仓储 |
| SqlSugar client 从 singleton 改为 scoped | 每请求/作用域一个 client；仓储与 UoW 共享 |
| 取消预检查 | 适配器不吞取消；in-flight SQL 取消仍未验证 |

## 5. 未验证/未覆盖

- EF 专用 optional marker 的 host 收紧、PostgreSQL/其他数据库、ORM trim/NativeAOT、in-flight 取消、跨进程并发；不因本切片改变「未验证」声明。
- S3 未修改 AOT-verified 的正式 runtime（Workflow/Eventing/RabbitMQ/LocalEvent 等）；ORM 本就未声明 NativeAOT-verified，本轮不新增发布门禁声明。
- S4 未开始：`CCCREPO001/002` 接线、EF 正式 generated CRUD Q01、完整生成器/CRUD/HTTP 回归。

## 6. 复跑

```bash
dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock dotnet test tests/Boundary/CrestCreates.DependencyBoundaries.Tests
```

结构化证据：[S3 JSON](2026-10-22-issue-130-s3-evidence.json)。
