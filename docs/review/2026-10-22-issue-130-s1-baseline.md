# Issue #130 S1 基线与契约冻结（执行记录）

日期：2026-10-22；切片：S1；状态：S1 目标红例与冻结清单完成，S2 尚未开始。
分支：`feat/issue-130-provider-matrix-soft-delete-20261022-104210`。
设计/计划：[设计 rev.1](../superpowers/specs/2026-10-22-issue-130-provider-matrix-soft-delete-design.md)、[实施计划](../superpowers/plans/2026-10-22-issue-130-provider-matrix-soft-delete-implementation.md)。
基线 master：`8c8a71de6a47d7c0eb94e8260e9f94035b22a477`；SDK：10.0.112。

S1 出口按计划是「红例触达业务路径 + 正控成立 + 设计差异与责任明确」，**不等于功能完成**。本切片未修改 EF/FreeSql/SqlSugar 生产仓储、拦截器、模块或扩展；仅新增冻结的诊断编号常量与测试基线。

## 1. 执行摘要

| 套件 | 命令 | 结果 |
| --- | --- | --- |
| OrmProviders（历史基线） | `dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests` | 124 passed / 0 failed（改动前复跑） |
| OrmProviders（含 S1 基线） | 同上 | **173 total / 146 passed / 27 failed**；27 红全部为本文 §3 约定红例，原 124 条全部通过 |
| CodeGenerator（S1 新增） | `dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter FullyQualifiedName~Issue130ProviderDiagnosticsBaselineTests` | 4 total / 1 passed（EF 正控）/ 3 failed（§3 G01/G02） |

历史 124 条完整 FQN 见 [基线证据 JSON](2026-10-22-issue-130-baseline-evidence.json) 的 `suite.results`；本轮新增红例完整 FQN 见 [S1 证据 JSON](2026-10-22-issue-130-s1-baseline-evidence.json)。

## 2. 调用图与当前事实（S1-a）

### 2.1 仓储与执行面

| 组件 | 当前事实 | 对契约的含义 |
| --- | --- | --- |
| [EfCoreRepository](../../src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepository.cs) | `GetQueryable()` 直接返回 native query；查询/写入口直接把调用方 CT 传给 EF；`DeleteAsync(id, stamp)` 用 `ExecuteDeleteAsync` | 通用 EF 仓储绕过基类租户过滤；操作层 token 组合只在 `EfCoreRepositoryBase` 存在；stamped 删除绕过审计/软删除 |
| [EfCoreRepositoryBase](../../src/Persistence/CrestCreates.Data.EFCore/Repositories/EfCoreRepositoryBase.cs) | 查询/管理 token 组合；`GetQueryable()` 继承基类租户过滤；`DeleteAsync(id, stamp)` 同样 `ExecuteDeleteAsync` | 两个 stamped 入口都必须修；基类过滤仅在 `DataFilterState.IsEnabled<TenantFilter>()` 且当前租户非空时生效 |
| [CrestRepositoryBase](../../src/Framework/Ddd/CrestCreates.Domain/Repositories/CrestRepositoryBase.cs) | `GetQueryable()` 对 `IMustHaveTenant` 做 `TenantId == currentTenant.Id` 过滤；无软删除过滤 | 唯一的仓储层租户过滤，且被 EF 通用仓储/SDK 仓储 override 绕过 |
| [FreeSqlRepository](../../src/Persistence/CrestCreates.Data.FreeSql/Repositories/FreeSqlRepository.cs) | 每个删除重载直接 `Delete<T>()`；`GetQueryable()` 为 `ToList().AsQueryable()`；构造时缓存 `uowManager.Orm` | 五删除重载均为物理删除；查询在 Where/分页前已物化；无 tenant/live-row 条件 |
| [SqlSugarRepository](../../src/Persistence/CrestCreates.Data.SqlSugar/Repositories/SqlSugarRepository.cs) | 每个删除重载直接 `Deleteable<T>()`；`GetQueryable()` 为 `ToList().AsQueryable()`；所有异步查询/写入都忽略 `cancellationToken` | 同左；取消来源被丢弃 |
| [FreeSql 拦截器](../../src/Persistence/CrestCreates.Data.FreeSql/Interceptors/FreeSqlAuditInterceptor.cs) | `ConfigureAuditInterceptor` 会同时注册软删除全局过滤器；`ConfigureSoftDelete` 的 CurdBefore 仍是 TODO；租户过滤器在配置时捕获 `tenantId` 字符串 | 过滤器可用但装配与生命周期未收口；删除仍不会被改写 |
| [SqlSugar 拦截器](../../src/Persistence/CrestCreates.Data.SqlSugar/Interceptors/SqlSugarAuditInterceptor.cs) | `ConfigureAuditInterceptor` 只注册 `DataExecuting`（含 DeleteByObject 字段设置），**不注册**软删除过滤器；租户过滤器同样捕获配置时 tenant | 现有成功软删除测试接受物理删除；默认读不会隐藏已删除行 |
| [EF AuditInterceptor](../../src/Persistence/CrestCreates.Data.EFCore/Interceptors/AuditInterceptor.cs) | tracked `Deleted → Modified`，写删除字段 | 仅证明 tracked 路径；`ExecuteDelete` 与 SQL 直改不经过它 |
| [EF MultiTenancyInterceptor](../../src/Persistence/CrestCreates.Data.EFCore/Interceptors/MultiTenancyInterceptor.cs) | Added 自动填 TenantId；Modified/Deleted 仅校验**传入实体**的 TenantId | 不校验持久行归属，伪造 detached TenantId 可越租户写 |
| [TenantFilterSourceGenerator](../../src/Tooling/CrestCreates.CodeGenerator/TenantFilterGenerator/TenantFilterSourceGenerator.cs) | 对 `IMustHaveTenant`、DataFilter `IMultiTenant`、EF `IMultiTenant` 生成同一过滤器：`currentTenant.Id == null \|\| e.TenantId == currentTenant.Id` | host-null 可读全部租户；三种 marker 被混为一谈；过滤器组合/覆盖需 S3 处理 |

### 2.2 装配与生命周期

| 入口 | 当前事实 |
| --- | --- |
| [EF 注册](../../src/Persistence/CrestCreates.Data.EFCore/Configuration/EfCoreDbContextServiceCollectionExtensions.cs) | `AddCrestCreatesEfCoreDbContext` 注册 scoped `AuditInterceptor`/`MultiTenancyInterceptor` 并 `AddInterceptors`；模型缓存键使用 `TenantAwareModelCacheKeyFactory` |
| [EF 模块](../../src/Persistence/CrestCreates.Data.EFCore/Modules/EfCoreOrmModule.cs) | 注册上述拦截器 scoped；provider 能力走 `AddUnitOfWorkProvider` |
| [FreeSql 模块](../../src/Persistence/CrestCreates.Data.FreeSql/Modules/FreeSqlOrmModule.cs) | 硬编码 `DataType.SqlServer`；单例 `IFreeSql`；scoped manager/UoW；**未调用**审计/软删除/租户过滤器配置 |
| [SqlSugar 模块](../../src/Persistence/CrestCreates.Data.SqlSugar/Modules/SqlSugarOrmModule.cs) | 硬编码 `DbType.SqlServer`；**单例** `SqlSugarClient`；scoped UoW；**未调用**审计/软删除/租户过滤器配置 |
| [FreeSql 扩展](../../src/Persistence/CrestCreates.Data.FreeSql/Extensions/FreeSqlServiceCollectionExtensions.cs) | 两个 `AddFreeSqlWithUow` 都使用 `AddFreeRepository(null, repositoryAssembly)` 扫描注册；同样未统一过滤器/审计 hook |
| 样例/平台消费者 | `samples`、`src/Platform`、`src/Framework/Modules` 中未发现 FreeSql/SqlSugar 正式运行装配；SaaSHelpdesk 仅 FreeSql binding 测试占位 |

### 2.3 编译期入口

| 生成器 | 当前事实 |
| --- | --- |
| [EntitySourceGenerator](../../src/Tooling/CrestCreates.CodeGenerator/EntityGenerator/EntitySourceGenerator.cs) | `[Entity(OrmProvider="FreeSql")]` 直接生成 `FreeSql{Entity}Repository` + FreeSql mapping；`"SqlSugar"` 同理；未知 provider 落到 EF 分支（默认 `"EfCore"`） |
| [RepositorySourceGenerator](../../src/Tooling/CrestCreates.CodeGenerator/RepositoryGenerator/RepositorySourceGenerator.cs) | `GenerateRepositoryImplementation` 未被调用；`GenerateSqlSugarRepository`/`GenerateFreeSqlRepository` 为空；未知 provider 默认 EF（死分支）；仅生成接口与 DI 注册 |
| 诊断编号 | S1 冻结 `CCCREPO001`（非 EF 生成请求）/`CCCREPO002`（未知 Provider），见 [RepositoryGeneratorDiagnosticCodes](../../src/Tooling/CrestCreates.CodeGenerator/RepositoryGenerator/RepositoryGeneratorDiagnosticCodes.cs) |

## 3. 目标红例台账（27 ORM + 3 G）

所有红例先触达真实仓储/生成器入口；数据库断言均通过独立 `Microsoft.Data.Sqlite` 连接读取原表，排除 EF ChangeTracker 与 SDK cache 影响。正控见 §4。

### 3.1 删除（D）

| 红例（FQN 简写） | 目标契约 | 当前事实 | 责任切片 |
| --- | --- | --- | --- |
| `D01_FreeSql_DeleteAsync_entity_must_preserve_the_row` | 软实体删除 → 留行 + IsDeleted/DeletionTime/DeleterId | 物理 DELETE，行不存在 | S2 |
| `D01_FreeSql_DeleteAsync_id_must_preserve_the_row` | 同上 | 物理 DELETE | S2 |
| `D01_FreeSql_DeleteRangeAsync_entities_must_preserve_rows` | 批量逐行逻辑删除 | 物理 DELETE，2 行归零 | S2 |
| `D01_FreeSql_DeleteRangeAsync_predicate_must_preserve_rows` | 条件批量逻辑删除 | 物理 DELETE，2 行归零 | S2 |
| `D01_SqlSugar_DeleteAsync_entity_must_preserve_the_row` | 同 FreeSql 五重载 | 物理 DELETE | S2 |
| `D01_SqlSugar_DeleteAsync_id_must_preserve_the_row` | 同上 | 物理 DELETE | S2 |
| `D01_SqlSugar_DeleteRangeAsync_entities_must_preserve_rows` | 同上 | 物理 DELETE | S2 |
| `D01_SqlSugar_DeleteRangeAsync_predicate_must_preserve_rows` | 同上 | 物理 DELETE | S2 |
| `D02_EfCore_default_reads_hide_soft_deleted_rows` | Get/List/Count/Any/Exists/分页统一排除已删除 | 无软删除 query filter，已删除行与 live 行同时返回 | S2 |
| `D02_SqlSugar_filtering_must_be_applied_by_production_read_paths` | 生产审计装配后默认读隐藏已删除行 | `ConfigureAuditInterceptor` 不注册软删除过滤器 | S2/S3 |
| `D03_EfCore_generic_repository_stamped_id_delete_must_preserve_the_row` | stamp 删除留行并推进 stamp | `ExecuteDeleteAsync` 物理删除 | S2 |
| `D03_EfCore_platform_repository_base_stamped_id_delete_must_preserve_the_row` | 同上（平台基类入口） | `ExecuteDeleteAsync` 物理删除 | S2 |
| `D03_FreeSql_stamped_id_delete_must_preserve_the_row_and_advance_the_stamp` | 条件 UPDATE + 留行 + 推进 stamp | 条件 DELETE，行不存在 | S2 |
| `D03_SqlSugar_stamped_id_delete_must_preserve_the_row_and_advance_the_stamp` | 同上 | 条件 DELETE，行不存在 | S2 |
| `D08_EfCore_batch_delete_of_stamped_entities_must_reject_before_any_write` | stamped 批次无每行 expectedStamp → 先拒绝 | 直接软删除全部 | S2 |
| `D08_EfCore_predicate_delete_of_stamped_entities_must_reject_before_any_write` | predicate 版本按实体类型拒绝 | 直接软删除全部 | S2 |
| `D08_FreeSql_batch_delete_of_stamped_entities_must_reject_before_any_write` | 先拒绝、零写 | 物理 DELETE 全部 | S2 |
| `D08_SqlSugar_batch_delete_of_stamped_entities_must_reject_before_any_write` | 先拒绝、零写 | 物理 DELETE 全部 | S2 |

冻结的拒绝形态：stamped 批次/谓词删除抛 `NotSupportedException`，消息含 `stamp`，且原始行 `IsDeleted = 0`。若 S2 认为应改用其他异常类型，必须在设计/计划中显式改冻结，而不是放宽测试。

### 3.2 租户（T）

| 红例 | 目标契约 | 当前事实 | 责任切片 |
| --- | --- | --- | --- |
| `T_EfCore_forged_detached_update_must_not_change_another_tenants_row` | 校验持久行租户归属；伪造 TenantId 不得越租户写 | EF 拦截器只比较传入 TenantId 与当前租户，更新成功；目标行改名/改租户 | S3 |
| `T_FreeSql_forged_detached_update_must_not_change_another_tenants_row` | 同上 | `Update<T>().SetSource` 仅按 Id+stamp 更新，成功越租户写 | S3 |
| `T_SqlSugar_forged_detached_update_must_not_change_another_tenants_row` | 同上 | 同上 | S3 |
| `T_FreeSql_cross_tenant_id_delete_must_not_remove_the_row` | id 删除必须带持久租户条件 | 无租户条件的 DELETE，A 租户行被 B 上下文删除 | S3 |
| `T_SqlSugar_cross_tenant_id_delete_must_not_remove_the_row` | 同上 | 同上 | S3 |
| `T_FreeSql_filter_must_resolve_the_current_tenant_per_operation` | 过滤器按操作解析当前租户，不捕获配置时值 | 配置时捕获 `"tenant-a"`；切到 B 后仍返回 A 行 | S3 |
| `T_SqlSugar_filter_must_resolve_the_current_tenant_per_operation` | 同上 | 同上 | S3 |

精确拒绝语义：S1 只冻结「必须失败且目标行零变化」，异常类型留给 S3 在设计/计划中冻结（当前 EF 写路径已有 `InvalidOperationException` 形态，但伪造场景尚未覆盖）。

### 3.3 审计/生命周期/取消（A/U）

| 红例 | 目标契约 | 当前事实 | 责任切片 |
| --- | --- | --- | --- |
| `U03_SqlSugar_module_must_not_register_a_process_wide_client` | SqlSugar client/事务状态按 scope 隔离 | 模块注册单例 `SqlSugarClient`，两个 scope 解析到同一实例 | S3 |
| `U07_FreeSql_repository_honors_a_pre_cancelled_token` | 取消保持取消语义、不吞来源 | 预取消 token 抛 `System.Exception` 包装 `TaskCanceledException`，取消来源丢失 | S3 |
| `U07_SqlSugar_repository_honors_a_pre_cancelled_token` | 同上 | 仓储完全不传 CT，预取消 token 被忽略并正常返回 | S3 |

### 3.4 生成期（G）

| 红例 | 目标契约 | 当前事实 | 责任切片 |
| --- | --- | --- | --- |
| `G01_EntitySourceGenerator_FreeSql_repository_request_reports_non_ef_provider_error` | `CCCREPO001` Error，且不生成 `FreeSqlProductRepository.g.cs` | 无诊断；实际生成 `FreeSqlProductRepository.g.cs`（直连 SDK CRUD） | S4 |
| `G02_EntitySourceGenerator_unknown_provider_reports_unknown_provider_error_instead_of_ef_default` | `CCCREPO002` Error，不默认 EF | 无诊断，静默走 EF 分支 | S4 |
| `G01_RepositorySourceGenerator_FreeSql_request_reports_non_ef_provider_error` | `CCCREPO001` Error | 无诊断；只生成接口，实现分支为空/未调用 | S4 |

## 4. 真实正控（22 ORM + 1 G，全部通过）

| 控制 | 证据 |
| --- | --- |
| EF tracked 删除四重载软删除 + 删除审计字段 | `D01_EfCore_tracked_delete_overloads_preserve_rows_and_write_delete_audit` |
| 非软实体确实物理删除（EF/FreeSql/SqlSugar） | `D05_*` 三条 |
| 陈旧 stamp 抛 `CrestConcurrencyException` 且行不变（EF/FreeSql/SqlSugar） | `D06_*` 三条 |
| FreeSql 生产审计装配后软删除过滤器隐藏已删除行 | `D02_FreeSql_production_audit_wiring_filters_deleted_rows_from_reads` |
| 稳定租户下 FreeSql/SqlSugar 过滤器隔离 A/B | `T_FreeSql_stable_tenant_filter_isolates_rows`、`T_SqlSugar_stable_tenant_filter_isolates_rows` |
| EF 跨租户 id 删除被拦截器拒绝、行保留 | `T_EfCore_cross_tenant_id_delete_is_rejected_by_the_save_interceptor` |
| 三 Provider 插入审计真实回读 | `A01_EfCore/FreeSql/SqlSugar_insert_persists_creation_audit` |
| 三 Provider 更新切换用户、保留创建字段 | `A02_*` 三条 |
| FreeSql/SqlSugar 两个仓储共享 UoW 事务：abandon 后新连接不可见 | `U01_*_abandon_hides_both_writes` 两条 |
| FreeSql/SqlSugar 两个仓储共享 UoW 事务：Complete 后新连接可见 | `U01_*_complete_persists_both_repository_writes` 两条 |
| EF `[Entity(OrmProvider="EfCore")]` 正常生成、无 provider 诊断 | `G03_EntitySourceGenerator_EfCore_positive_control_generates_without_provider_diagnostics` |

## 5. 租户标记冻结与迁移清单（S1-c）

### 5.1 标记事实

| 标记 | 定义 | 当前默认语义 | 真实消费者 |
| --- | --- | --- | --- |
| `Domain.Shared` 的 `IMustHaveTenant` | 必填 `string TenantId` | EF 生成过滤器：host(null) 看全部、租户看自身；`CrestRepositoryBase` 在数据过滤器启用且租户非空时再看自身；EF/SDK 写拦截器：新增为空自动填当前租户、修改/删除比较传入 TenantId | `MustHaveTenantOrganizationEntity<TKey>` → `User`、`Role`、`Organization`（`src/Framework/Ddd/CrestCreates.Domain/Permission/`） |
| `DataFilter` 的可空 `IMultiTenant` | `string? TenantId` | EF 生成过滤器同样 host=全部；FreeSql/SqlSugar `ConfigureMultiTenantFilter` 在无租户时**直接 return（不注册过滤器）**，故 host 可见全部；有租户时条件为 `TenantId == null \|\| TenantId == currentTenant` | 仅 `MultiTenantEntity<TId>`/`MultiTenantSoftDeleteEntity<TId>` 抽象基类；`src`/`samples` 未发现具体生产实体；MongoDB 测试有测试实体 |
| EF 专用 `Data.EFCore.MultiTenancy.IMultiTenant` | 非空 `string TenantId` | 与上同（生成器不区分 marker） | 仅 EF `MultiTenantEntity` 抽象基类；未发现具体生产实体或样例消费者 |

结论：三种 marker **当前并不同义**，生成器与 SDK 过滤器把它们合并成同一 host-null 语义。S1 不收紧生产行为，只冻结迁移目标。

### 5.2 目标语义与迁移动作（S3 实施）

| 场景 | 目标 | 迁移/拒绝说明 |
| --- | --- | --- |
| `IMustHaveTenant`，租户 A 普通操作 | 仅 A；插入空 TenantId 自动填 A；显式 B 写入拒绝 | 需要新负例覆盖「持久行归属」而非仅传入字段 |
| `IMustHaveTenant`，host/null 普通读 | 不自动获得全租户视图 | 当前 EF 生成过滤器 host=全部是兼容差异；收紧前必须列出真实消费方并给出迁移说明/真实负例，不能静默改变 |
| 可空 `IMultiTenant`，租户 A | 查询 A + 显式 null 共享行；普通新增空值填 A；禁止写 B | host 自身 null 行；跨租户管理走既有显式治理边界 |
| 可空 `IMultiTenant`，host/null | 仅 null 共享行 | 当前 FreeSql/SqlSugar 在 host 不注册过滤器 → 可见全部；EF 同样 host=全部。S3 必须补真实负例后收紧 |
| EF 专用 marker | 不强迫业务同时实现两套接口；S3 列出现有消费者后生成静态适配 | 当前无具体生产消费者；保留兼容差异说明 |
| 无租户标记 | 不附加任何租户语义 | 不新增隐式过滤 |

跨租户管理入口：S1 检索 `src`/`samples`/`src/Platform` 未发现以普通 host scope 作为自动 bypass 的正式消费者；S3 若需要，必须显式标出内部治理路径，不能复制权限逻辑或引入 `IgnoreQueryFilters` 式通用绕过。

## 6. SDK 探针（S1-d）

环境：SQLite 独立文件；FreeSql 3.5.308、SqlSugarCore 5.1.4.214；真实连接。

| 探针 | 结果 |
| --- | --- |
| FreeSql 仓储是否跟随平台 UoW 事务 | **是**：`U01_FreeSql_*` 两条；abandon 后新连接不可见 2 条写入，Complete 后可见 2 条 |
| SqlSugar 仓储是否跟随平台 UoW 事务 | **是（显式 scoped client 装配下）**：`U01_SqlSugar_*` 两条；但生产模块当前注册单例，不能据此推导模块行为 |
| SqlSugar 模块 client 生命周期 | **缺陷**：两个 scope 解析到同一 `SqlSugarClient`（`U03`） |
| FreeSql 执行中取消能力 | **未证明有界终止**：预取消 token 被包装为 `System.Exception`（内层 `TaskCanceledException`），取消来源丢失；未做 in-flight SQL 取消探针 |
| SqlSugar 执行中取消能力 | **未支持**：仓储入口不传 CT，预取消 token 被忽略；未做 in-flight 探针 |
| 模块对 SQLite 的可测试性 | FreeSql/SqlSugar 模块都硬编码 SqlServer，无法通过模块直接运行 SQLite；S1 使用扩展/原生 scoped 装配做行为探针，S3 统一装配核心后补模块级负例 |

`RequiresNew`/显式 isolation 的既有拒绝由 `ProviderCapabilityTests`、`UnitOfWorkDiRegistrationTests` 覆盖，S1 未放宽也未新增能力声明。

## 7. 诊断编号冻结（S1-e）

- `CCCREPO001` — 非 EF Provider 的生成仓储/CRUD 请求；指向受验证的显式异步仓储入口。
- `CCCREPO002` — 未知 Provider；禁止默认 EF。

S4 接线点：
- `EntitySourceGenerator` 的 `OrmProvider` 分支（FreeSql/SqlSugar 直连 SDK CRUD 必须改为 Error，不再生成绕过文件）；
- `RepositorySourceGenerator` 的 `GenerateRepositoryImplementation`/provider dispatch（空实现/未知默认 EF 必须改为 Error）；
- 诊断编号与 EF 正控在本切片已冻结；S4 只接线，不改编号语义。

## 8. 未覆盖/延后

| 项 | 说明 |
| --- | --- |
| Q01 EF 正式 generated CRUD 关系型查询 | S1 不新造生成 CRUD + UoW 的证据夹具；它依赖 S4 的正式生成组合，Q01 在 S4 出口验收，不能用 EF InMemory 代替 |
| R 全量注册矩阵 | 既有 `UnitOfWorkDiRegistrationTests`（15）、`AmbientChainAndOwnershipTests`（6）、`ProviderCapabilityTests`（5）覆盖注册顺序/重复/缺失与 missing scope/context；S1 仅新增 SqlSugar 生命周期红例，其余 R 项在 S3 回归 |
| EF 生成租户过滤器组合/宿主收紧 | S3 需要有真实消费者与迁移说明后再改 host=全部语义 |
| PostgreSQL/其他数据库 | 未运行；本切片只使用独立文件 SQLite |
| trim/NativeAOT | 未运行；ORM SDK 仍为「未验证」，不因本切片改变 |
| 执行中取消 | 仅预取消 token；不宣称有界终止 |
| 完整 CodeGenerator 套件 | S1 只跑新增 G 过滤集；完整套件包含耗时 MSBuild 消费项目，S4 出口必须复跑 |

## 9. 复跑命令

```bash
dotnet test tests/Persistence/CrestCreates.OrmProviders.Tests
dotnet test tests/Tooling/CrestCreates.CodeGenerator.Tests --filter FullyQualifiedName~Issue130ProviderDiagnosticsBaselineTests
```

预期：OrmProviders `173 total / 146 passed / 27 failed`（红例见 §3）；G 过滤集 `4 total / 1 passed / 3 failed`。任何额外失败都应视为回归，而不是红基线。

## 10. 下一步（S2）

1. EF：两个 stamped delete 改为 tracked 软删除 + 唯一 AuditInterceptor/concurrency 协议；补静态软删除过滤并与现有租户模型配置组合。
2. FreeSql/SqlSugar：五个删除重载统一条件 UPDATE + 审计/stamp；无软删除实体保持 DELETE；stamped 批次/谓词删除先拒绝。
3. 修复伪造 detached 写与跨租户 id/predicate 删除的持久租户校验（可与 S3 合并顺序，但不得留双轨）。
4. S2 前先确认 `NotSupportedException` 作为 batch stamp 拒绝形态是否符合后续设计；如需变更，在同一轮显式更新冻结记录。
