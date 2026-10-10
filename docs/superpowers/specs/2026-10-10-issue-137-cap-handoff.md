# Issue #137 → #125 CAP 事务参与交接文档

日期：2026-10-10
来源：`docs/superpowers/specs/2026-10-10-issue-137-uow-transaction-propagation-design.md`（§4.5 CAP 最小交接契约，rev.3.1）
适用基线：#137 实施分支（切片 1-5）；#125 必须在本项验收合并后基于新 master 开始。

---

## 一、允许 CAP 接入的本地事务范围

1. **单数据库资源的本地事务**（EF Core 当前唯一 Provider 实现）：CAP 只能参与「当前受管 UoW 窗口」的同一 Connection/Transaction；内核负责开始/提交/回滚。
2. **明确拒绝**：
   - 跨库 / 跨 ORM 原子提交（含「依次 Commit 即原子」的聚合器）；
   - 多 DbContext 共享事务（如未来纳入支持，必须附真实数据库证明后另行声明）；
   - scope 终态/释放后的继续参与（lease 失效，确定性拒绝）；
   - 第二资源（第二 Context 声明/连接配置/租户库）与中切租户：必须先新建 UoW，不能在活动 UoW 内参与。

## 二、谁负责开始 / 提交 / 回滚

- **唯一内核**（`UnitOfWorkManager` + 状态化 scope）负责：`StartAsync` 打开事务、完成顺序（rollback-only 校验 → flush → commit【确认即记录 Committed】→ 通知 → 清理）、失败路径回滚与「终结 + 丢弃」。
- **CAP 不获得生命周期操作**：公开参与契约（`UnitOfWorkTransactionLease` / `IUnitOfWorkTransactionIdentity`）不暴露 Commit/Rollback/Dispose；CAP 不得尝试完成或释放外层事务。
- **适配边界借用**（仅 Provider 适配层）：`IUnitOfWorkTransactionIdentity.NativeTransaction` 是原生事务对象（如 `IDbContextTransaction`），供 CAP SDK 适配（如 `ICapPublisher.Transaction = native`）使用；借用规则：**不得 Dispose / Commit / Rollback**，仅可读取身份并在同一事务内附加命令（消息记录写入）。

## 三、获取与失效

- 获取：`IUnitOfWorkTransactionLeaseProvider.TryAcquireCurrentLease(out var lease)`（Provider 所有、绑定/注册声明；EF 实现为 scoped）。
- 关联校验：lease 解析的是「当前有效资源上下文」（受管链路由后）的 `Database.CurrentTransaction`，因此天然满足同一 Connection/Transaction；`TransactionId` 可用于与数据库侧核对。
- **live view**：`IsValid` 为实时计算（事务句柄已释放/已完结即 false）；每个参与动作前应重新 `TryAcquireCurrentLease`，**不得跨 scope 缓存 lease**。
- 无事务（未开始/非事务模式/已终结）→ `TryAcquireCurrentLease` 返回 false；继续使用已失效 lease → 确定性拒绝。

## 四、消息记录落库时点与语义边界

1. CAP 消息记录（业务消息 + 事务表）必须使用**同一资源/事务**、在**本 UoW commit 之前**落库（即在 `CompleteAsync` 的 flush/commit 窗口内完成）。
2. **提交后通知不证明消息持久化原子性**：域事件/通知在数据库 commit 确认之后发布；通知失败保留已提交事实（不回滚、不自动重试业务），CAP 侧不得把它当作「消息与业务数据同库同事务」的证据。
3. **不得从生命周期 hook 推导**：exactly-once、2PC、durable outbox 保证均不由本机制承诺；CAP 的存储/传输配置与 SDK 原子发布验证属于 #125。
4. 不得新增万能公开扩展点、不得为 CAP 引入对 CAP SDK 的框架依赖；SDK 适配代码留在 #125 / 应用或集成层。

## 五、交 #125 的处置记录

| 既有物 | 状态 | 处置 |
| --- | --- | --- |
| `CrestCreates.DistributedTransaction.Extensions.UnitOfWorkIntegrationExtensions`（`RegisterWithDistributedTransaction`） | 死扩展（零消费者） | 由本契约替代；#125 评估删除或归档（99_RecycleBin），不得作为正式参与路径保留 |
| `ITransactionParticipant` / `UnitOfWorkTransactionParticipant`（Prepare/Commit/Rollback/Compensate） | 内存式参与者抽象 | #125 决定复用或替换；不得与本 lease 契约形成双轨 |
| `IUnitOfWorkTransactionIdentity.IsolationLevel` | 关系型 Provider 可用 | CAP 如需要隔离级别，从 lease 读取；非关系型为 null |

## 六、故障语义（CAP 必须消费的统一裁定）

- 提交前失败：回滚；原异常保留。
- 提交派发期失败/响应丢失：`TransactionOutcome=Unknown`（回滚尝试不改写）；CAP 不得据此宣布「已回滚」。
- 提交后通知失败：`Committed` 保持、`NotificationOutcome=Failed`；不回滚、不自动重试业务。
- 未完成退出：内核「终结事务 + 丢弃未 flush 跟踪写入」；CAP 侧不得依赖未完成窗口内的写入。

---

**验收要求（#125 开工前）**：基于本项合并后的 master 实施；CAP SDK 原子发布须以真实数据库验证；不得跳过本交接文档中的拒绝项而自行扩展跨库/2PC 语义。
