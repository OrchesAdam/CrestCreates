# 归档说明：Infrastructure.UnitOfWork.UnitOfWorkFactory.cs（Issue #124）

归档日期：2026-10-10
来源：`src/Framework/Infrastructure/CrestCreates.Infrastructure/UnitOfWork/UnitOfWorkFactory.cs`

## 归档原因

该文件定义了第二套 `IUnitOfWorkFactory` / `IUnitOfWorkManager` / `OrmOptions` /
`ConfigureOrm` / `AddUnitOfWork` / `UnitOfWorkFactoryDelegate`（命名空间
`CrestCreates.Infrastructure.UnitOfWork`）。经全仓核对：

- 无任何消费者：`CrestCreatesWebApplicationExtensions` 的 `AddUnitOfWork(OrmProvider.EfCore)`
  因重载解析（identity conversion 优先于 `OrmProvider?`）实际绑定到
  `CrestCreates.Data.Abstractions` 的扩展；该 using 已随迁移移除。
- 其实现依赖 `Type.GetType`、`AppDomain.GetAssemblies` 反射回退与吞异常 `try/catch`，
  与 #124「唯一装配、typed binding、无反射后门」的目标冲突。
- 其 `UnitOfWorkManager` 使用实例级 `_current`（非 AsyncLocal），与 Data 侧
  scope/嵌套语义重复且更弱。

## 迁移去向

- 唯一契约与实现：`CrestCreates.Data.Abstractions`（`IUnitOfWorkFactory`、
  `IUnitOfWorkManager`、`UnitOfWorkManager`、`AddUnitOfWork`、`AddUnitOfWorkProvider`）。
- 默认 Provider 声明：`AddUnitOfWork(defaultProvider)` 与装配规则（见
  `docs/review/2026-10-10-issue-124-unitofwork-unified-registration.md`）。
- 配置来源（`OrmOptions` / "Orm" 配置节）：旧路径不再读取；如需恢复必须按新证据另行设计。

## 交付说明

- 本目录随 PR 提交（对 `99_RecycleBin/` 的忽略规则做单文件 force-add），下游 checkout 可取到本说明与归档正文。
- 归档文件与被删除原文件**逐字节一致**（用 `git show <base>:<原路径> | diff -` 验证），
  因此保留了原文件既有的行尾空白；`git diff --check` 对新增行的空白提示属预期，不应修改正文。

如需恢复该文件，请先提供新的可复现消费方与失败用例。
