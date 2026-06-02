# ORM 能力矩阵

CrestCreates 框架多 ORM Provider 能力对比。基于实际源码分析，非假设。

## 能力矩阵

| 能力 | EF Core | FreeSql | SqlSugar | MongoDB |
|------|---------|---------|----------|---------|
| 软删除 | ✅ | ⚠️ | ✅ | ✅ |
| 多租户过滤 | ✅ | ✅ | ✅ | ✅ |
| 审计字段 | ✅ | ✅ | ✅ | ⚠️ |
| 并发控制 (ETag) | ✅ | ✅ | ✅ | ✅ |
| 工作单元 (UoW) | ✅ | ✅ | ✅ | ❌ |
| 批量操作 | ✅ | ✅ | ✅ | ⚠️ |
| 原生 SQL | ✅ | ⚠️ | ⚠️ | ❌ |
| 乐观锁 | ✅ | ✅ | ✅ | ✅ |

## 详细说明

### EF Core

**项目路径**: `framework/src/CrestCreates.OrmProviders.EFCore/`

**软删除** ✅
- 通过 `AuditInterceptor` 实现，拦截 `EntityState.Deleted` 状态的 `ISoftDelete` 实体
- 自动将删除操作转换为更新操作，设置 `IsDeleted = true`、`DeletionTime`、`DeleterId`
- 相关文件: `Interceptors/AuditInterceptor.cs`

**多租户过滤** ✅
- 通过 `MultiTenancyInterceptor` 在保存时自动设置租户ID并验证租户边界
- 通过 `MultiTenancyDiscriminator` 配置全局查询过滤器（编译时生成）
- 支持 `IMultiTenant`（可选租户）和 `IMustHaveTenant`（必需租户）两种模式
- 相关文件: `Interceptors/MultiTenancyInterceptor.cs`, `MultiTenancy/MultiTenancyDiscriminator.cs`

**审计字段** ✅
- 通过 `AuditInterceptor` 自动填充 `CreationTime`、`CreatorId`、`LastModificationTime`、`LastModifierId`
- 支持 `IAuditedEntity` 接口
- 相关文件: `Interceptors/AuditInterceptor.cs`

**并发控制 (ETag)** ✅
- 通过 `IHasConcurrencyStamp` 接口实现乐观并发控制
- `ModelBuilderExtensions.ConfigureConcurrencyStamp()` 自动配置并发令牌
- 更新时检查 `ConcurrencyStamp`，冲突时抛出 `CrestConcurrencyException`
- 相关文件: `Extensions/ModelBuilderExtensions.cs`, `Repositories/EfCoreRepositoryBase.cs`

**工作单元 (UoW)** ✅
- 通过 `EfCoreUnitOfWork` 实现，支持事务管理、领域事件发布
- 支持 `BeginTransactionAsync`、`CommitTransactionAsync`、`RollbackTransactionAsync`
- 相关文件: `UnitOfWork/EfCoreUnitOfWork.cs`

**批量操作** ✅
- 支持 `InsertRangeAsync`、`UpdateRangeAsync`、`DeleteRangeAsync`
- 使用 EF Core 的 `AddRangeAsync`、`UpdateRange`、`RemoveRange`
- 相关文件: `Repositories/EfCoreRepositoryBase.cs`

**原生 SQL** ✅
- 通过 `IEntityFrameworkCoreDbContext.ExecuteSqlRawAsync` 支持原生 SQL 执行
- 使用 EF Core 的 `Database.ExecuteSqlRawAsync`
- 相关文件: `DbContexts/IEntityFrameworkCoreDbContext.cs`, `DbContexts/CrestCreatesDbContext.cs`

---

### FreeSql

**项目路径**: `framework/src/CrestCreates.OrmProviders.FreeSqlProvider/`

**软删除** ⚠️ 部分支持
- `FreeSqlAuditInterceptor.ConfigureSoftDeleteFilter()` 配置全局软删除过滤器
- 但实际的删除转更新逻辑标记为 TODO，未完全实现
- 相关文件: `Interceptors/FreeSqlAuditInterceptor.cs`

**多租户过滤** ✅
- 通过 `ConfigureMultiTenantFilter` 使用 `GlobalFilter.Apply` 配置全局过滤器
- 支持 `IMultiTenant`（查询自身租户或无租户数据）和 `IMustHaveTenant`（只查询当前租户数据）
- 相关文件: `Interceptors/FreeSqlAuditInterceptor.cs`

**审计字段** ✅
- 通过 `FreeSqlAuditInterceptor` 使用 `Aop.AuditValue` 事件自动填充审计字段
- 支持 `CreationTime`、`CreatorId`、`LastModificationTime`、`LastModifierId`
- 相关文件: `Interceptors/FreeSqlAuditInterceptor.cs`

**并发控制 (ETag)** ✅
- 通过 `IHasConcurrencyStamp` 接口实现乐观并发控制
- 使用原生 SQL WHERE 子句检查 `ConcurrencyStamp`
- 更新时生成新的 `ConcurrencyStamp`，冲突时抛出 `CrestConcurrencyException`
- 相关文件: `Repositories/FreeSqlRepositoryBase.cs`

**工作单元 (UoW)** ✅
- 通过 `FreeSqlUnitOfWork` 和 `FreeSqlUnitOfWorkManager` 实现
- 支持事务传播（`Propagation.Required` 等 6 种模式）
- 支持领域事件发布和重试机制
- 相关文件: `UnitOfWork/FreeSqlUnitOfWork.cs`, `UnitOfWork/FreeSqlUnitOfWorkManager.cs`

**批量操作** ✅
- 支持 `InsertRangeAsync`、`UpdateRangeAsync`、`DeleteRangeAsync`
- 使用 FreeSql 的批量操作方法
- 相关文件: `Repositories/FreeSqlRepositoryBase.cs`

**原生 SQL** ⚠️ 未显式暴露
- FreeSql 本身支持原生 SQL，但仓储接口未显式暴露 `ExecuteSql` 方法
- 可通过 `IFreeSql` 实例直接执行原生 SQL

---

### SqlSugar

**项目路径**: `framework/src/CrestCreates.OrmProviders.SqlSugar/`

**软删除** ✅
- 通过 `SqlSugarAuditInterceptor` 实现，拦截 `DeleteByObject` 操作类型
- 自动设置 `IsDeleted = true`、`DeletionTime`、`DeleterId`
- 通过 `ConfigureSoftDeleteFilter` 配置全局软删除过滤器
- 相关文件: `Interceptors/SqlSugarAuditInterceptor.cs`

**多租户过滤** ✅
- 通过 `ConfigureMultiTenantFilter` 使用 `QueryFilter.Add` 配置全局过滤器
- 支持 `IMultiTenant`（可选租户）和 `IMustHaveTenant`（必需租户）两种模式
- 相关文件: `Interceptors/SqlSugarAuditInterceptor.cs`

**审计字段** ✅
- 通过 `SqlSugarAuditInterceptor` 使用 `Aop.DataExecuting` 事件自动填充审计字段
- 支持 `CreationTime`、`CreatorId`、`LastModificationTime`、`LastModifierId`
- 相关文件: `Interceptors/SqlSugarAuditInterceptor.cs`

**并发控制 (ETag)** ✅
- 通过 `IHasConcurrencyStamp` 接口实现乐观并发控制
- 使用原生 SQL WHERE 子句检查 `ConcurrencyStamp`
- 更新时生成新的 `ConcurrencyStamp`，冲突时抛出 `CrestConcurrencyException`
- 相关文件: `Repositories/SqlSugarRepository.cs`

**工作单元 (UoW)** ✅
- 通过 `SqlSugarUnitOfWork` 实现，使用 `ISqlSugarClient.Ado.BeginTran/CommitTran/RollbackTran`
- 支持领域事件发布和重试机制
- 相关文件: `UnitOfWork/SqlSugarUnitOfWork.cs`

**批量操作** ✅
- 支持 `InsertRangeAsync`、`UpdateRangeAsync`、`DeleteRangeAsync`
- 使用 SqlSugar 的批量操作方法
- 相关文件: `Repositories/SqlSugarRepository.cs`

**原生 SQL** ⚠️ 未显式暴露
- SqlSugar 本身支持原生 SQL，但仓储接口未显式暴露 `ExecuteSql` 方法
- 可通过 `ISqlSugarClient.Ado` 直接执行原生 SQL

---

### MongoDB

**项目路径**: `framework/src/CrestCreates.OrmProviders.MongoDB/`

**软删除** ✅
- 在 `MongoRepositoryBase.DeleteAsync` 中直接实现
- 检查 `ISoftDelete` 接口，将删除转换为更新操作
- 设置 `IsDeleted = true`、`DeletionTime`
- 相关文件: `Repositories/MongoRepositoryBase.cs`

**多租户过滤** ✅
- 通过 `BuildTenantFilterDefinition` 和 `BuildTenantFilter` 方法实现
- 所有查询自动应用租户过滤器
- 支持 `IMultiTenant`（可选租户）和 `IMustHaveTenant`（必需租户）两种模式
- 相关文件: `Repositories/MongoRepositoryBase.cs`

**审计字段** ⚠️ 部分支持
- 只支持 `CreationTime` 和 `LastModificationTime` 的自动填充
- 不支持 `CreatorId`、`LastModifierId` 的自动填充
- 相关文件: `Repositories/MongoRepositoryBase.cs`

**并发控制 (ETag)** ✅
- 通过 `IHasConcurrencyStamp` 接口实现乐观并发控制
- 使用 `ReplaceOneAsync` + `ConcurrencyStamp` 过滤器
- 冲突时抛出 `InvalidOperationException`
- 相关文件: `Repositories/MongoRepositoryBase.cs`

**工作单元 (UoW)** ❌ 不支持
- MongoDB 驱动支持多文档事务（4.0+），但框架未实现 UoW
- 无 `MongoUnitOfWork` 实现

**批量操作** ⚠️ 部分支持
- `InsertRangeAsync` 使用 `InsertManyAsync`（原生批量）
- `UpdateRangeAsync` 和 `DeleteRangeAsync` 逐个处理（非原生批量）
- 相关文件: `Repositories/MongoRepositoryBase.cs`

**原生 SQL** ❌ 不适用
- MongoDB 是文档数据库，不支持 SQL 查询
- 可通过 MongoDB 原生查询 API（`Find`、`Aggregate` 等）

## 已知限制

### EF Core
- 无已知主要限制，功能最完整

### FreeSql
- 软删除的删除转更新逻辑未完全实现（标记为 TODO）
- 仓储接口未显式暴露原生 SQL 方法

### SqlSugar
- 仓储接口未显式暴露原生 SQL 方法

### MongoDB
- 审计字段只支持时间字段，不支持用户ID字段
- 无工作单元（UoW）实现
- 批量更新和删除非原生实现，性能可能较差
- 不支持 SQL 查询

## 接口统一性

所有 ORM Provider 均实现 `ICrestRepositoryBase<TEntity, TKey>` 接口，提供统一的 CRUD 操作。但以下能力依赖 ORM 特定实现：
- 原生 SQL 执行
- 工作单元（UoW）
- 批量操作性能

## 参考文件

- 抽象层: `framework/src/CrestCreates.OrmProviders.Abstract/`
- 领域实体接口: `framework/src/CrestCreates.Domain.Shared/Entities/Auditing/`
- 数据过滤: `framework/src/CrestCreates.DataFilter/Entities/`
