# issue-137-contracts-state 归档说明（切片 1：契约/状态）

来源提交：`feat/issue-137-slice1-kernel-state-20261010-155153`（#137 切片 1）。
归档时间：2026-10-10。

本目录文件为 #137 切片 1 从正式主链移除的死代码，正文与被移除时逐字节一致：

| 文件 | 原路径 | 归档理由 |
| --- | --- | --- |
| `Data.Abstractions.IUnitOfWorkEnhanced.cs` | `src/Persistence/CrestCreates.Data.Abstractions/IUnitOfWorkEnhanced.cs` | `IUnitOfWorkEnhanced` 无实现、无消费者；其旧 `UnitOfWorkOptions`（IsolationLevel/Timeout/过滤器开关）为「声明未生效」选项，已由生效的 `UnitOfWorkOptions`（IsolationLevel/Timeout/Provider/传播）替换。仓储获取走 DI 注入；TenantId 走 `ICurrentTenant`；过滤开关走现有过滤注册/`IgnoreQueryFilters`。 |
| `Aop.Abstractions.Options.UnitOfWorkOptions.cs` | `src/Framework/Infrastructure/CrestCreates.Aop.Abstractions/Options/UnitOfWorkOptions.cs` | 旧 AOP 侧 `UnitOfWorkOptions`（IsTransactional/DefaultTimeout/AutoCommit/AutoRollbackOnException）零读取；`AopOptions.UnitOfWork` 属性同步移除。AOP 不再持有独立 UoW 配置，事务语义由唯一内核承担。 |
| `Data.Abstractions.RepositoryBase.Repository.cs` | `src/Persistence/CrestCreates.Data.Abstractions/RepositoryBase/Repository.cs` | 构造期捕获 DbSet/QueryableBuilder 的抽象仓储基类，零消费者；跨 UoW 缓存资源对象的反面样本。正式仓储走 `CrestRepositoryBase` + 逐次解析。 |

迁移指引：统一使用 `CrestCreates.Data.Abstractions` 的 `UnitOfWorkOptions` 与 `BeginScope + StartAsync` 内核入口（见 `docs/superpowers/specs/2026-10-10-issue-137-uow-transaction-propagation-design.md`）。
