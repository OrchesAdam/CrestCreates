# issue-125-cap-consolidation — 归档记录

日期：2026-10-12
来源：Issue #125（入口收口；设计 `docs/superpowers/specs/2026-10-12-issue-125-cap-entry-consolidation-design.md` rev.4）

## 归档内容与原因

| 归档文件（原路径） | 原因 | 替代路径 |
| --- | --- | --- |
| `src/Runtime/DistributedTransaction.CAP/Modules/DistributedTransactionCapModule.cs` | 自动发现模块入口注册**内存实现**，与扩展入口形成静默回落内存的双轨；模块路径无法完成配置绑定的 CAP 装配（OnConfigureServices 无配置通道） | 唯一入口 `AddCrestCapDistributedTransaction` |
| `.../Implementations/TransactionLogger.cs` | 内存字典 + Console 的假日志 | `PersistentTransactionLogger`（持久化） |
| `.../Implementations/DefaultTransactionCompensator.cs` | 假补偿：不调用执行器即标记完成、`CanCompensateAsync` 恒 true | `PersistentTransactionCompensator`（真实执行器 + 重试 + Failed） |
| `.../Implementations/DistributedTransaction.cs`、`DistributedTransactionManager.cs` | 内存参与者列表伪 2PC（Begin 空操作、Prepare/Commit 内存态），与唯一 UoW 内核冲突、零业务消费者 | 原子发布消费 #137 lease/borrower 契约（CAP 集成） |
| `src/Runtime/DistributedTransaction/Extensions/UnitOfWorkIntegrationExtensions.cs`、`ITransactionParticipantRegistry.cs` | 死扩展（#137 交接已判；把 Domain UoW 包成参与者的旧桥） | #137 交接契约 |
| `src/Runtime/DistributedTransaction/Abstractions/IDistributedTransaction.cs`、`IDistributedTransactionManager.cs`、`ITransactionParticipant.cs` | 上述内存引擎的零消费者抽象 | 同上 |
| `tests/Runtime/DistributedTransaction/CrestCreates.DistributedTransaction.Tests/DistributedTransactionManagerTests.cs` | 仅覆盖内存 manager，与归档对象同进退 | 新测试面向持久化/原子发布真实路径 |

归档为 #125 S1 交付；如需恢复，请先对照设计文档 rev.4 与审查记录（PR #140）。
