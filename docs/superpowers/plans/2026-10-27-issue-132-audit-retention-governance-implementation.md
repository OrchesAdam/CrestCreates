# #132 retention 有界实施计划（设计裁定后执行）

日期：2026-10-27。依据：[功能设计](../specs/2026-10-27-issue-132-audit-retention-governance-design.md)。本文件是交付拆分与执行记录；实现已在该分支完成，远程 Issue 勾选与合并由维护者执行。

## S0：确认裁定与基线

1. 维护者裁定 PostgreSQL 固定数据库、Global 期限、最小身份/治理元数据暂不 GC 的 V1 范围。
2. 以届时当前 master 重新记录 SHA，复跑 gap probe；不把 `348c4d3c` 旧工作目录作为实施基线，保留未提交审查材料。
3. 枚举旧 cleanup 的所有注册、直接调用与自动生成暴露；先确定关闭/退出方式，禁止双链同时可执行。记录实际服务身份/租户/权限契约，不为 scheduler 缺身份创建旁路。

## S1：契约、Setting 与 admission

1. 在现有 Accountability.Abstractions/Accountability 增加 scope、policy、run/batch/hold/result 与 typed store 协议；公开 DTO 与 runtime protocol 分界清晰，不泄漏 Npgsql、SQL 或服务定位器。
2. 在现有 Domain Setting definition 增加 Global enabled/budget 设置，复用 90 天 key，验证写入和最终读取；无任意请求期限覆盖。由既有 Settings/Permission 主链承担管理权限。
3. Application 构建统一授权入口，复用 IPermissionChecker、当前身份/租户/目录；当前租户限定、host 精确 scope、后台可信身份都必须有真实失败测试。
4. 显式装配按 StoreId 绑定，缺 capability/重复/内存存储拒绝；先保持执行 disabled。无程序集扫描和反射 fallback。

验收：P02/P08 的拒绝均零 Provider 删除；Settings 负数/非法值/冲突声明 fail closed；Runtime 不引用 Framework/具体 ORM。普通 AppService/Job 不重复治理判断。

## S2：PostgreSQL 原子持久协议

1. additive migration：ledger、typed projections/quarantine、hold、scope lock、run/batch receipts、索引；升级回填维护窗口，原始 JSON/hash 保持不变；版本 readiness 拒绝旧 writer/半迁移。
2. 同步改 PostgreSqlAuditSink 唯一去重权威为 ledger；正文与首次接收同事务，Purged 后 Duplicate/Conflict 且不复活正文。
3. 批次事务：scope→run→record 固定锁序；有界双时间选取/hold 检查/DELETE RETURNING/ledger 状态/receipt/run 预算同事务。预览只读、有界、无全表 Count。
4. hold place/release 同锁协议与 receipt，expected version + 多 hold；禁止从 tags 赋予保护或用错误租户 ID 操作记录。

验收：P01/P03/P04/P05/P07/P09；真实 PG 每测试独立 schema；迁移两次、异常隔离、EXPLAIN 与晚接收分布；两连接回读提交/回滚事实。不得用 Moq/InMemory 代替 SQL 原子性证据。

## S3：执行内核、作业与旧入口退出

1. shared executor 固定 policy/cutoff，持久批预算，处理取消/commit Unknown/receipt reconciliation；ReadRun 由 batch receipts 派生可靠累计值，保留 MirrorOutcome。
2. 提交后通过现有 IAuditRecorder 输出受保护 typed fact，提供 receipt 显式再导出；不宣称后台可靠投递或用镜像失败回滚提交。
3. AppService、生成 HTTP 与 typed job 调用同一 admission；复用 ISchedulerService，不新增 timer/hosted 清理循环。按显式 StoreId/scope 注册，默认关闭。
4. 替换所有旧 cleanup 调用与注册，归档裸删除接口/日期 DTO/专用旧清理测试到 `99_RecycleBin/issue-132-audit-retention/`；保留旧查询但明确其兼容身份。不能把旧表清理偷偷包装成正式 Provider。

验收：P06/P08、真实 Application Settings/Permission/Tenant + generated HTTP 的拒绝/成功、进程崩溃与同 key 恢复；取消后先前批仍存在 receipt，Unknown 不报零，无旧裸删 fallback。

## S4：原生证据与交接

1. 扩展现有 PG AotHost/harness，显式 aot publish/link/run，按 spec §9 执行场景/负控/总 sentinel，保留二进制、源 SHA、发布命令、exit code 与 per-case evidence。
2. JsonContracts roots 静态闭包；测试保持真实生成路径，不禁用约束来过门禁。
3. 只对实际执行组合声明 NativeAOT-verified；单列真实认证/Setting/Quartz 是否覆盖。其他 Provider/RID 写未验证，CI 手动禁用状态不改。
4. 同步 memory、AGENTS、Accountability feature 文档、CapabilityMap 的已有项目记录与支持声明，不复制 ORM 支持矩阵。

最终交接：实现 PR、差异/迁移说明、P01–P09 到具体 test 的映射、native 产物证据、遗留延期项。#132 关闭仅表示设计裁定；完整 retention 功能另按实施结果验收，不自动合并。

## 执行证据与 P01–P09 映射（2026-10-27，本分支）

| 组 | 具体测试 | 结果 |
| --- | --- | --- |
| P01 时间 | `PostgreSqlAuditRetentionTests.Batch_purges_only_dual_time_expired_bodies_and_keeps_cutoff_equal`、`Backfill_projects_valid_rows_quarantines_invalid_rows_and_is_repeatable`；原生 `CRESTCREATES_AUDIT_RETENTION_NATIVE_TIME_OK` | 通过 |
| P02 scope/权限 | `Tenant_scope_and_host_scope_never_cross_partitions`；`AuditRetentionAppServiceTests` 的匿名/权限/租户/host 名称解析用例；`AuditRetentionHttpAdmissionTests` 匿名、403、跨租户拒绝；原生 `SCOPE_OK` | 通过 |
| P03 保护 | `Active_hold_blocks_purge_until_released_and_quarantine_is_never_purged`、`Hold_version_conflict_and_already_purged_are_rejected`；原生 `HOLD_OK`、`PROTECTION_OK` | 通过 |
| P04 预算/预览 | `Batch_budget_is_persisted_and_remaining_is_reported`、`Preview_is_read_only_bounded_and_does_not_create_a_run`；`AuditRetentionExecutorTests` 批/时间预算；原生 `BATCH_OK` | 通过 |
| P05 事务 | `Receipt_failure_rolls_back_the_whole_batch`、`Second_batch_failure_keeps_the_first_committed_batch` | 通过 |
| P06 并发/恢复 | `Repeated_batch_key_returns_the_committed_receipt_without_deleting_again`、`Concurrent_same_batch_serializes_on_the_scope_lock`、`Finalize_is_first_terminal_winner_and_retry_resumes_the_same_budget`；executor commit-Unknown 三个用例；原生 `RECOVERY_OK`（同 key、新 provider 回放） | 通过 |
| P07 sink | `Sink_dedup_survives_purge_and_old_writer_is_rejected`、`Sink_dedup_does_not_allow_cross_tenant_duplicate_identity`；原生 `SINK_OK`（含 Old writer FK） | 通过 |
| P08 composition | `AuditRetentionCompositionValidatorTests`（缺 store/重复/缺能力/共享 sink/启动拒绝）、`AuditRetentionAppServiceTests`（真实 admission 语义，collaborator 以接口替身隔离）、`SaaSHelpdesk.Tests.AuditRetentionHttpAdmissionTests`（真实身份/权限/Setting/租户 + generated HTTP，拒绝零 Provider 调用，授权且开启后成功）；原生 `CAPABILITY_OK` | 通过 |
| P09 升级 | `Backfill_projects_valid_rows_quarantines_invalid_rows_and_is_repeatable`、`PostgreSqlRuntimeIntegrationTests` 的迁移重放/校验和/schema manifest 断言 | 通过 |

原生门禁：`tests/Persistence/CrestCreates.Runtime.Persistence.PostgreSql.AotFixture.Tests`
执行 `-p:CrestCreatesPublishMode=aot -r linux-x64` publish/link/run，断言八个 per-case
sentinel 与总 sentinel `CRESTCREATES_AUDIT_RETENTION_NATIVE_OK`；同一原生二进制以
`retention-negative wrong-count|hold-filter|receipt-fault` 运行时均非零退出且不含总 sentinel，
输出包含 `CRESTCREATES_AUDIT_RETENTION_NEGATIVE_CONTROL_CAUGHT`。

边界说明：

- 进程崩溃窗口（commit 前/后、响应丢失）的 SQL 原子性证据来自 PG 两连接/触发器用例；
  原生场景覆盖的是同 key、跨 provider 重建的 receipt 回放，不冒充真实进程 kill。
- 原生 host 不建立真实认证/Setting/Quartz 身份链；这些由 JIT P08 与 Application 测试覆盖。
- 未执行：EF/FreeSql/SqlSugar/MongoDB retention、其他 RID、按租户策略、批量/法务 hold、
  archive/WORM、元数据 GC、自动调度注册。
- 计划中“receipt 显式再导出”未实现：设计未要求独立导出入口；批量/保留事实已通过
  `IAuditRecorder` 落库，运行记录可由 `GetRunAsync` 与 ledger 查询读取。
