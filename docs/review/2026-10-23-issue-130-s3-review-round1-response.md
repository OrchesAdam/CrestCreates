# Issue #130 S3 审查 R1 回复（2 P1 + 2 P2）

审查对象：精确 head `8c8b24fe`；报告 [2026-10-23-issue-130-s3-review-round1.md](2026-10-23-issue-130-s3-review-round1.md)。
修复提交：`1025368e`。测试按用户要求**串行执行**，未并发跑套件。

## 1. 逐项映射

| Finding | 修复 | 证据 |
| --- | --- | --- |
| **S3-R1-1 P1 EF 归属预读被 tracked 实体污染** | 两个 EF 仓储的 `EnsurePersistedTenantOwnershipAsync` 改为 `AsNoTracking()` 读取持久快照，不进入 identity map、不附加跟踪。tracked 伪造场景读到数据库 tenant-a 并拒绝；合法 detached 同租户更新不再产生 same-key tracked 冲突 | 审查探针 `EF_ownership_preflight_must_use_persistent_facts`：修复前 2/6，修复后 **6/6**（两个入口 × tracked control/detached/forged） |
| **S3-R1-2 P1 SDK 共享行写边界失守** | 分离读/写租户谓词：读仍为 optional A + null；写无条件按所有权——tenant 只能写 `TenantId == current`，host 只能写 `TenantId IS NULL`。持久归属与提交实体都要求精确匹配，禁止普通租户更新/删除/批量删除共享行，也禁止改 TenantId 私有化 | 审查探针 `Optional_shared_rows_are_host_owned_for_writes`：修复前 6/12，修复后 **12/12**（两 Provider × host/tenant × update/id/predicate） |
| **S3-R1-3 P2 正式 DI 未接通审计** | 审计值进入 Provider 内部仓储动作：Insert/InsertRange 填 `CreationTime/CreatorId`，Update/UpdateRange 填 `LastModificationTime/LastModifierId`，软删除动作填 `DeletionTime/DeleterId`；用户来自 scoped `ICurrentUserProvider`，不捕获进 singleton hook，业务 handler 无需手动配置 SDK | 审查探针 `Formal_provider_registration_must_enable_creation_audit`：修复前 0/2，修复后 **2/2**；新增 `A03_Formal_*_registration_persists_create_update_delete_audit_across_scopes` **2/2**（三 scope 切换用户，Creator/LastModifier/Deleter 分别落库） |
| **S3-R1-4 P2 忽略受管执行 token** | 两个 SDK 仓储的每个公开异步入口使用 `UnitOfWorkExecutionContext.CombineWithCurrent(cancellationToken, out effective)`，以组合 token 预先检查并传给 SDK 命令；不自建 ambient 取消协议 | 审查探针 `Managed_cancellation_before_repository_entry_must_stop_sql`：修复前 0/2，修复后 **2/2**（非事务 UoW 取消后零写入，抛 `OperationCanceledException`） |

## 2. 回归结果

| 集合 | 结果 |
| --- | --- |
| ReviewS3Probes（新增审查探针） | **22 / 22 passed**（修复前 8 passed / 14 failed） |
| OrmProviders 全量 | **240 total / 240 passed / 0 skipped**（原 216 + 22 探针 + 2 正式审计回归） |
| R1/R2 历史探针 | ORM 9/9、生成器 4/4（未回归） |
| 映射列回归（普通 + 保留字） | 10 / 10 |
| CodeGenerator 全量 | 458 / 455 / 3（仅约定 G 红；本 R1 未改生成器） |
| Dependency Boundaries | 176 / 178（2 条既有 #69 evidence-ledger，无关） |

## 3. 行为说明

- EF 预读使用无跟踪快照后，合法 detached 更新与 tracked 同租户更新都保留；伪造 tracked 实体在写入前被拒绝，原行业务字段与 TenantId 零变化。
- SDK 读范围与写所有权已分离：租户可读共享行，但只有 host 能维护共享行；predicate 删除使用写所有权条件，不会误删共享行。
- 审计快照一次操作内使用同一时刻/用户；创建字段在修改时保持不变。
- 受管取消只处理"入库前已取消"；in-flight SQL 取消仍按原声明未验证。

## 4. 未验证不变

EF 专用 optional marker 的 host 收紧、PostgreSQL/其他数据库、ORM trim/NativeAOT、in-flight 取消、跨进程并发。S4 未启动。
