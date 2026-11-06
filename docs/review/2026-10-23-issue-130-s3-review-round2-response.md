# Issue #130 S3 审查 R2 回复（剩余两个 P2）

审查对象：精确 head `7b06a5fb`；报告 [2026-10-23-issue-130-s3-review-round2.md](2026-10-23-issue-130-s3-review-round2.md)。
修复提交：`733bea45`。全部测试串行执行，未并发跑套件。

## 1. 逐项映射

| Finding | 修复 | 证据 |
| --- | --- | --- |
| **S3-R2-1 P2 审计字段可伪造** | 创建审计改为权威赋值：`CreationTime = UtcNow`、`CreatorId = ICurrentUserProvider.GetCurrentUserId()`（不再 `??=`，预填假值被覆盖），插入时清空修改字段；FreeSql/SqlSugar 更新使用 `IgnoreColumns(CreationTime, CreatorId)`，不把调用方提交的创建字段写回；EF `AuditInterceptor` 插入同样权威赋值，两个 EF 仓储更新将 CreationTime/CreatorId 标记为未修改 | 审查探针 `Audit_must_derive_creator_from_identity_and_preserve_creation_fields`：修复前 2/6，修复后 **6/6**。新增 EF 回归 `A03_EfCore_creation_audit_is_authoritative_and_updates_preserve_it` **1/1** |
| **S3-R2-2 P2 SqlSugar 未传递组合 token** | SqlSugar 所有 SDK 命令接收组合后的 `cancellationToken`：Insert/Update/Delete 的 `ExecuteCommandAsync(ct)`、查询 `ToListAsync/FirstAsync/CountAsync/AnyAsync(ct)`；`InSingleAsync` 无 CT 重载，在调用前检查；审计快照读取后、SQL 前再次检查，覆盖“入口检查后、SQL 前取消” | 审查探针 `Cancellation_during_audit_snapshot_before_SQL_must_prevent_the_write`：修复前 1/2，修复后 **2/2**；FreeSql 对照保持 0 行，SqlSugar 现在也 0 行 |

## 2. 回归结果

| 集合 | 结果 |
| --- | --- |
| ReviewS3R2Probes（新增） | **8 / 8 passed**（修复前 3 passed / 5 failed） |
| ReviewS3Probes（R1） | **22 / 22 passed**（未回归） |
| OrmProviders 全量 | **249 total / 249 passed / 0 skipped** |
| 新增 EF 审计回归 | 1 / 1 |
| CodeGenerator 全量 | 458 / 455 / 3（约定 G 红；R2 未改生成器） |
| Dependency Boundaries | 176 / 178（2 条既有 #69 evidence-ledger，无关） |

## 3. 行为说明

- 普通创建/更新/删除审计只使用现有 `ICurrentUserProvider` 权威；预填 `CreatorId/CreationTime` 不再生效，普通更新保留数据库原始创建字段。
- 组合 token 现在真正进入 SqlSugar 命令；取消点在审计快照内、SQL 之前时，两个 Provider 都零写入并抛取消异常。
- 仍不声称 in-flight SQL 取消、跨进程并发、PG/其他数据库或 ORM trim/NativeAOT。

S4 未启动。
