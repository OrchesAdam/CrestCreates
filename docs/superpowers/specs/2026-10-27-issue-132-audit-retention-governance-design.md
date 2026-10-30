# #132 Audit retention / governance 最小平台契约设计

日期：2026-10-27。状态：功能设计草案，待维护者裁定；不表示生产 retention 已交付。  
需求：[Issue #132](https://github.com/OrchesAdam/CrestCreates/issues/132)，总控 #121，依赖 #122。  
基线：当前 master `5f4cfc8f37d6ef0e996598eb08ae0922b8b60a34`（#131 经 PR #155、状态同步 PR #156 合并）。本地工作目录仍为 `348c4d3c`；本次执行使用独立 Git archive 快照，没有切换或覆盖已有工作。

## 1. 目标与裁定建议

交付一个围绕正式 `AuditEnvelope` 存储的最小清理协议，使平台可以回答：谁授权清理哪个存储、哪个租户、什么期限之前的正文，哪些记录被保护，每批实际提交了多少，失败后如何确认和重试。

建议裁定：**实施有界 V1，先 PostgreSQL；本 Issue 交付设计与缺口复现，生产实现作为后续独立交付。** V1 包含策略解析、精确租户 scope、预览、有界正文清理、单记录 hold、原子批次收据与删除后幂等。暂缓通用审计查询平台、任意标签/条件 hold、归档导出、WORM/签名、法律合规承诺、跨数据库原子清理、收据与身份账本的自动 GC，以及 EF/FreeSql/SqlSugar/MongoDB 清理适配。

不新增业务模块或生产 csproj：抽象/执行内核进入既有 Accountability 项目，Provider 进入既有 PostgreSQL 项目，Setting/权限/调度适配进入既有 Application/平台装配。不得将 Runtime 对 Framework/Web 或具体 ORM 的引用作为捷径。

## 2. 事实校准：清理入口不等于正式 retention

| 现有代码 / 记录 | 核实结果与影响 |
| --- | --- |
| `AuditLogging/Services/AuditLogWriter.cs`、Accountability recorder | 正式写入产出 `AuditEnvelope`，通过 `IAuditSink` 接收；旧 AuditLog 不是第二个框架真相来源 |
| `PostgreSqlAuditSink.cs`、migration V003 | 正式持久表为 `runtime_audit_envelopes`，主键 `(sink_id,audit_id)`，存 integrity/envelope JSON 与 `accepted_at`；没有 retention、hold、purge receipt 协议 |
| `Application/AuditLog/AuditLogCleanupAppService.cs` | BeforeTime 优先于 RetentionDays，允许负天数和未来 cutoff；直接调用旧仓储，无预览/批次/执行收据；方法内无权限检查。直接调用缺少鉴权不证明所有宿主 HTTP 都公开可访问 |
| `EfCoreAuditLogRepository.DeleteOlderThanAsync` | 租户按 Id 过滤；null/空/`host` 上下文隐式跨租户；先 Count 再 ExecuteDelete，返回前者；无批次/hold。实际清理对象为旧 `Domain.AuditLog.AuditLog` |
| `AuditLoggingSettingDefinitionProvider` | 已有 Global `AuditLogging.RetentionDays=90`，不能再造配置表或让调用者用请求任意覆盖期限 |
| `docs/Feature/Accountability/arch-design.md` | 已明确 legacy AuditLog 为 compatibility model，正式 query/retention 不在原功能交付范围 |
| memory / AGENTS 中“写入、脱敏、查询闭环” | 查询针对旧模型的既有能力，不能据此宣称正式 PostgreSQL AuditEnvelope 的 query-retention 闭环；本设计不扩大通用查询范围 |

真实调用方仍包括 EF 模块装配、SaaSHelpdesk EF 模块与测试。旧清理 API 不能静默换成新语义继续返回看似成功的 0，也不能保留一个裸删除 fallback。§10 指定其退出条件。

复现入口、结果与限制见[证据记录](../../review/2026-10-27-issue-132-design-gap-evidence.md)和[探针](../../review/probes/issue132/README.md)。测试真实旧仓储是为了证明迁移缺口，不将其升级为正式主链验收。

## 3. 唯一责任与依赖边界

| 区域 | 唯一责任 / 拟新增契约 |
| --- | --- |
| Accountability.Abstractions | `AuditRetentionScope`、policy/preview/result、`IAuditRetentionService`、`IAuditRetentionStore`；只含强类型值与协议 |
| Accountability | `AuditRetentionExecutor`：固定策略快照、运行预算、批次循环、结果合并；不扫描程序集、不解析 claims、不查 Setting、不执行 SQL |
| Application / Contracts | retention AppService：复用当前身份、`IPermissionChecker`、当前租户/租户目录；解析 Setting 成 policy；统一预检后调用同一内核；生成 HTTP 投影只作适配 |
| 现有 Scheduling 装配 | typed `IJob`/`IJob<TArg>` 调用同一 Application 授权入口；复用 `ISchedulerService` 与 Quartz；无第二个后台清理循环 |
| Runtime.Persistence.PostgreSql | 同库 SQL 实现、锁、migration、幂等 ledger、hold、batch receipt；事务事实的唯一权威 |
| 平台装配 | 显式 typed `AddAuditRetention` + `AddPostgreSqlAuditRetention`；同一 StoreId 重复/缺失、仅内存存储、缺事务/hold/receipt 能力启动失败；禁用时不注册自动任务 |

生命周期 owner 为 Accountability；PostgreSQL 团队代码区域负责存储原子性，Application 负责授权/策略入口。这是代码责任划分，不虚构人员指派。单一执行内核服务 HTTP/运维/作业；Handler 不接 service locator 或字符串协议。

## 4. 策略、时间与租户范围

### 4.1 Setting Management

V1 只支持 Global 策略，保留现有 `AuditLogging.RetentionDays`（默认 90，允许 1–36500）。新增同组 Global 设置：`Retention.Enabled=false`、`Retention.BatchSize=500`（1–1000）、`Retention.MaxBatchesPerRun=20`（1–100）、`Retention.MaxRunSeconds=60`（1–300）。完整 key 均以 `AuditLogging.` 开头。无 User scope，无 Tenant override；租户采用同一期限，但绝不因此获得跨租户操作权。

Setting 更新链做范围校验；每次执行解析最终值再校验，无法解析/越界/溢出必须拒绝、零删除，不回退到默认 90。构建期/部署级 options 只设置硬上限与存储能力，不能成为第二个运行时策略来源。请求不能指定 RetentionDays/BeforeTime 绕过策略。V1 不在此设计扩建设置系统的多键事务；一次解析得到合法不可变快照并计算 PolicyHash，同一 run 不再逐批读设置。禁用/策略变化影响新 run；已开始 run 最长持续其预算（默认 60 秒），紧急停止走取消。

### 4.2 候选条件

取 `TimeProvider.GetUtcNow()` 一次作为 SnapshotAt，Cutoff = SnapshotAt − RetentionDays × 24 小时。只删除同时满足下列条件的正文：

1. 精确匹配 StoreId / SinkId / scope；
2. `OccurredAt < Cutoff` **且** Provider 权威 `FirstAcceptedAt < Cutoff`；等于 cutoff 保留；
3. 无 active hold，非系统受保护类别，正文仍存在。

双时间条件避免补写的旧事件一进入存储就被清除。统一 UTC，拒绝不合法投影，不从 JSON 字符串的本地时区猜时间。历史无法可靠回填身份/时间的行标记 `Quarantined`，不可清理，并暴露数量/原因；不能把坏租户值转换为 host。

### 4.3 Scope

`AuditRetentionScope` 是 `Host` 或 `Tenant(TenantId)` 的显式联合值；Host 只表示正式存储的 null TenantId 分区。没有 AllTenants/通配符，没有将空字符串或 `host` 当 host 的运行时猜测。租户名只允许目录查询，最终必须持有权威 Id。

租户调用只能使用当前真实 TenantId；host 管理员指定 tenant 必须经目录解析及权限边界。后台作业也指定单一 scope；跨租户巡检只在治理编排中枚举目录的 Id，逐 scope 授权、执行与返回，不能把缺租户上下文视为跨租户特权。V1 不承诺自动扫描所有数据库/所有租户：每个 StoreId 显式绑定一个固定 PostgreSQL database/schema，自动任务按显式 scope 注册。

## 5. 公共契约与授权入口

拟定 API（实现时定义完整类型与 JsonContracts roots，本段不是已存在的代码）：

```csharp
public interface IAuditRetentionService
{
    ValueTask<AuditRetentionPreview> PreviewAsync(AuditRetentionPreviewRequest request, CancellationToken ct = default);
    ValueTask<AuditRetentionRunResult> ExecuteAsync(AuditRetentionRunRequest request, CancellationToken ct = default);
    ValueTask<AuditRetentionRunResult> GetRunAsync(AuditRetentionRunKey key, CancellationToken ct = default);
    ValueTask<AuditHoldResult> PlaceHoldAsync(AuditHoldRequest request, CancellationToken ct = default);
    ValueTask<AuditHoldResult> ReleaseHoldAsync(AuditHoldReleaseRequest request, CancellationToken ct = default);
}
```

请求包含 StoreId/SinkId/Scope、调用方幂等 RequestId、结构化 ReasonCode；执行没有任意 SQL、表达式、动态 Provider Type。PolicySnapshot、Cutoff、Actor、CorrelationId 与授权范围由 Application 从可信上下文构造，拒绝客户端伪造 actor/权限。Runtime 接口只供可信进程内装配，不构成网络授权屏障；所有对外入口、作业重试与运维命令都经过同一 Application admission。

权限定义在现有权限目录中新增 `AuditLogging.Retention.Preview`、`.Execute`、`.ReadRuns`、`.ManageHolds`，通过既有 `IPermissionChecker` + 租户权限语义检查，缺 checker/身份确定性拒绝；配置修改沿用 Setting Management 权限。Preview 不返回正文，ReadRuns 不等于任意审计查询。HTTP generated path 的鉴权之外，Application admission 本身必须有效，直接 DI 调用和作业不得跳过。后台任务使用平台现有可信服务身份并复用 checker；若当前 scheduler 无法建立这种身份，自动执行保持关闭，不凭 JobId 伪造授权。

`IAuditRetentionStore` 提供 `CreateOrReadRunAsync`、`PreviewBatchAsync`、`ExecuteBatchAsync`、`ReadRunAsync` 与 hold 事务操作；传入不可变、已绑定存储/scope/policy 的运行记录。store 从持久化 run 重读 cutoff/预算，不能相信每批传来的新参数。Capability 是强类型能力声明，注册时校验，不能运行时失败后 fallback 到 `DeleteOlderThanAsync`。

结果分别记录 `CommittedDeletedCount`、`CommittedBatchCount`、`RemainingMayExist`、`StopReason`、`CommitKnowledge`。状态至少含 Completed/Cancelled/Failed/Unknown；达到预算返回 BudgetReached，不暗示全部清完。RemainingMayExist 不是总数量。RequestId 在 `(StoreId,SinkId,Scope)` 中唯一；相同 id + 相同请求/策略绑定返回既有 run，不同绑定拒绝 Conflict。重试恢复旧 run 的预算与快照，不把预算重新置零；新策略须创建新 RequestId。

## 6. hold、索引与 Provider 数据模型

### 6.1 hold

V1 只支持对既存 `(sink_id,audit_id)` 单记录 Place/Release；无自动到期，释放需权限、ReasonCode、ExpectedVersion。多条 hold 可并存，存在任意 active hold 都阻止清理。存储检查 record 的真实 scope，不能凭请求 TenantId 变更归属。hold 是 Provider sidecar，不修改已哈希 AuditEnvelope，也不接受普通 Tags/ExtraProperties 作为可信保护指令。

PlaceHold、ReleaseHold、清理批次在同一 scope 锁行上串行：hold 先提交则本批保留；purge 先提交则 PlaceHold 返回 AlreadyPurged，不报告成功。保护按提交顺序生效，不承诺回溯撤销已清理内容。所有 hold 变更与各自治理收据同一事务。若没有 active body，不能创建一个看似有效的 hold。

### 6.2 additive migration

保留现有 `runtime_audit_envelopes` 为正文表；新增独立最小身份 ledger，主键沿用 `(sink_id,audit_id)`，存原 Integrity、FirstAcceptedAt、规范化 Scope、OccurredAt、正文状态与受保护分类。正文外键指向 ledger。回填逐行使用现有强类型 JSON context 解析、验证并投影，异常进入 quarantine；旧表含不合法契约时不硬转字符串。新增迁移必须重复运行可确认，升级/旧新二进制并行策略见 §10。

正文还需数据库级反复活约束：增加固定为 BodyPresent 的状态列（CHECK + 默认值），以 `(sink_id,audit_id,body_state)` 外键引用 ledger 对应唯一键。purge 先删除正文再将 ledger 转 Purged；旧 writer 的新身份因缺 ledger 被拒绝，旧 Purged 身份因状态不匹配被拒绝。不能只依赖新版进程的 readiness 检查，因为旧 sink 不会执行新检查。quarantine 行使用隔离状态/独立保留区处理，不放松正文外键以容纳损坏身份。

索引至少包括：ledger PK；可清理正文的 `(sink_id,scope_kind,tenant_id,accepted_at,audit_id)` partial index（state=BodyPresent 且非受保护）；hold `(sink_id,audit_id)` 的 active partial index；run scope + RequestId 唯一键；batch `(run_id,batch_ordinal)` 唯一键。OccurredAt 二次谓词不可省略；Provider 用 EXPLAIN/大样本证明扫描预算，不能宣称该单索引同时高效满足任意双时间分布。

新增 scope lock、retention run、batch receipt、hold/hold-receipt 表。不在原 Envelope 塞治理状态；收据与身份账本不属于正文候选集合。可清理分类必须由可信写入入口静态投影，Accountability retention/hold 镜像事实强制受保护，不能让普通 producer 的任意 tag 解除保护。

### 6.3 清理后保持 sink 契约

`PostgreSqlAuditSink.WriteAsync` 必须同步改为先对 ledger 做条件插入：新身份与正文同事务接收；已存在身份按原 hash 返回 Duplicate/Conflict，包含正文已经 Purged 的情况，保留原 FirstAcceptedAt，绝不重建正文。跨租户同 AuditId 仍遵循原 `(sink_id,audit_id)` 唯一性与完整性比较，不能改成 tenant 分区后重复接收。

清理只删除正文，ledger 标记 Purged，Integrity 不重算。查询运行/保护信息只能返回存在/已清理状态，不凭 tombstone 还原正文。最小 ledger、hold 历史与治理收据 V1 无限期保留；它们仍占空间、可能包含身份元数据。这是明确成本，V1 不是“删除所有个人数据”或法律遗忘权实现。其未来 GC 必须独立裁定重放窗口、引用与证据保留规则。

## 7. 预览、事务、取消与重试

### 7.1 Preview

默认交互是先预览；Preview 在只读事务中使用同一候选谓词和排序，最多返回一批的 Id/时间/排除原因汇总，无正文、不建 hold、不删数据、不预留候选。返回 SnapshotAt/Cutoff/PolicyHash/上限/HasMore 与有限采样；计数是有界值，不执行全表 Count。预览不等于批准、不锁住候选，执行重新授权并固定自己的快照；若提交 ExpectedPolicyHash 则不匹配拒绝并要求重新预览。界面明确两次候选可因 hold/新数据改变。

### 7.2 ExecuteBatch

1. 外层内核检查取消与时长；持久 run 预检通过后调用 Provider。
2. Provider 开本地数据库事务，锁定 scope 行与 run 行（所有操作固定 scope→run→record 顺序）；复查 run/scope/总批预算、既有 batch receipt、存储能力。
3. 有界选择 `ORDER BY accepted_at,audit_id LIMIT BatchSize FOR UPDATE`，精确 scope、双时间、非 hold/保护；删除前再次保证保护事实在同一事务序列。
4. 删除选中正文并取数据库实际 `RETURNING`；更新 ledger 为 Purged；写不可变 batch receipt（run/batch/idempotency、授权 actor、scope、policy/cutoff、实际数量、选中身份+integrity 的 canonical digest、提交时间），更新持久预算。任何写失败整体回滚。
5. 提交后才累加已确认事实并发布 post-commit observation。每批独立提交；不持有跨整场 run 的大事务，不借用业务 ambient UoW、不允许与调用方业务写入同事务。由既有持久化 Provider 的 Npgsql 本地事务承载，无新 UoW 执行内核。

同 scope 的不同 run 按批串行，允许批次交错，不承诺全局单作业或 exactly-once 调度。重复 batch key 返回已有 receipt，不能重新删下一批；同 key 不同参数必须 Conflict。每批最大删除 BatchSize；每 run 总量不超过 BatchSize×MaxBatches，多个合法 run 的累计删除不受单 run 上限约束。不同 scope 不互斥。

### 7.3 失败与结果事实

| 时点 | 行为 / 可信结果 |
| --- | --- |
| 授权/设置/能力/请求绑定拒绝 | 零正文删除；返回稳定诊断；常规失败审计通过既有 recorder |
| 取消发生在事务提交前 | 传递 CT 并回滚该批；先前已提交批保留；返回 Cancelled + 已确认计数 |
| SQL/receipt 写失败 | 当前批回滚，停止 run；不继续吞错；先前收据仍可查 |
| 提交成功、响应丢失或连接中断 | CommitKnowledge=Unknown；不报告 0、不假定回滚；以新连接按 batch key 读 receipt；存储仍不可用则保留 Unknown，重试同 key |
| 取消与 commit 同时发生 | 以 receipt 为准；确认提交保留 Committed，不把它改成 RolledBack |
| post-commit recorder/镜像失败 | 正文删除与 receipt 已提交；返回独立 MirrorOutcome，不改写提交结果，不因镜像失败重新删除 |
| 进程在两批之间退出 | 从持久 run/batch receipts 恢复；已提交批不重放，不再创建新预算 |
| run 终态记录更新失败 | 已提交 batch facts 优先；ReadRun 能派生累计事实与未确定状态，不能把 stale Running 当成零删除 |

取消响应采用有界的独立收据读取 token；恢复失败不能无限等待。具体 timeout 固定为配置硬上限（建议 5 秒）。执行 deadline 是调度预算，SQL command timeout 与 CT 另行受管；只在批间检查时长不能声称严格实时停止。

## 8. 清理动作的审计责任

Provider 的独立 receipt 是破坏性动作的提交证据；未能同事务写 receipt 就不能删除。它不是另一个通用审计 sink，不承载业务任意日志，不授予权限。Platform 操作的预检负责决定是否准入；Accountability post-fact 镜像继续保持观察职责，不能变成修改业务结果的 gate。

成功批次和 hold 变更提交后，经现有 `IAuditRecorder` 生成 typed fact，target 指向 run/batch 或 hold receipt，保留 actor/correlation/reason/policy/scope。镜像失败可从 receipt 重新导出同一 AuditId，现有 sink 去重；V1 仅提供显式重试导出，不承诺后台可靠投递。receipt 独立查询使其失败不造成唯一证据丢失。

保护包括治理 receipt 表不在候选集合、ledger 不删、镜像治理事实的正文分类受保护。即使 retention 设置缩短或同一 job 重跑，也不能消灭自己的唯一证据。普通诊断 ILogger、进程 stdout、旧 AuditLog 中手工放置的“cleanup”标签均不能满足此要求。

## 9. 验收矩阵与支持声明

当前缺口执行见 C01–C10；以下为**后续实现必须执行的门禁**，不能把设计表当测试通过：

| 组 | 正式 Provider 必测场景 |
| --- | --- |
| P01 时间 | 过期/未过期/精确 cutoff、晚接收旧事件、UTC、quarantine 不清理 |
| P02 scope/权限 | tenant-a 只能 A；host 只 host；host 指定 B 需授权；伪造空/名称/未知 Id/StoreId 拒绝；直接 DI/Job 同样拒绝未授权 |
| P03 保护 | active hold、双 hold 释放一条、release version 冲突、hold 与 purge 两种提交顺序、已 Purged 不可成功 hold、治理正文/receipt 不被清理 |
| P04 预算/预览 | 0/1/BatchSize/BatchSize+1，run 总批上限；preview 零删除、候选改变、hash 变化拒绝、无全表 Count |
| P05 事务 | 删除后 receipt 前注入失败，独立连接确认正文/ledger/receipt 全回滚；第二批失败时第一批独立保留 |
| P06 并发/恢复 | 同 batch 两连接并发、不同 run 同 scope、不同 scope、进程 commit 前/后退出、响应丢失 Unknown→receipt 恢复，重试不耗新预算 |
| P07 sink | purge 前后同 hash Duplicate、异 hash Conflict；FirstAcceptedAt 不变、正文不复活；跨租户同 AuditId 不绕过去重 |
| P08 composition | 缺 Provider/重复 StoreId/仅内存/缺能力启动拒绝；关闭不调度；复用真实 Setting/permission/tenant + generated HTTP 投影并断言拒绝时零 Provider 删除 |
| P09 升级 | 旧数据回填两次、异常隔离、原 envelope/integrity 不变、混版本禁止写入、查询计划与大样本批次预算 |

Provider 集成：扩展现有 `CrestCreates.Runtime.Persistence.PostgreSql.Tests`，真实 PostgreSQL、每测试 `itest_{guid}`，两连接与 CrashWorker；真实 Npgsql 事务，禁止 EF InMemory 假证据。Application 另测实际 Setting Management/权限/租户组合；generated endpoint 只映射该 admission，不加入 sample 特例。

NativeAOT：在现有 `CrestCreates.Runtime.Persistence.PostgreSql.AotHost` 与测试 harness 增 retention 场景，显式 `-p:CrestCreatesPublishMode=aot -r linux-x64`，完成 publish/link/run；Npgsql/schema 由 harness 建立，原生进程执行过期/边界/跨租户/hold/批次/回滚/重复恢复/purge 后 sink 去重/receipt 保护/缺能力负例，逐场景断言与总 sentinel `CRESTCREATES_AUDIT_RETENTION_NATIVE_OK`。wrong-count、移除 hold 过滤、receipt 故障三类负控必须非零且无总 sentinel；负控修改仅 fixture 或隔离源码，不增加生产公开故障开关。新 DTO/receipt 镜像经 JsonContracts 工具链闭包，官方 STJ，无 object/reflection fallback。

| 组合 | 当前状态 | 后续支持升级条件 |
| --- | --- | --- |
| 旧 AuditLog + EF SQLite | 本次 JIT 缺口复现；不等于正式 retention 支持 | 仅迁移对照，不作为 V1 Provider |
| 正式 PostgreSQL AuditEnvelope retention | 尚未实现，trim/NativeAOT 均未验证 | P01–P09 对应范围 + 真实原生 gate；逐项报告 HTTP 身份、作业身份覆盖 |
| 内存模拟 | 可做算法单测，非生产清理 | 不提升持久/原生 Provider 声明 |
| EF/FreeSql/SqlSugar/MongoDB/其他 RID | 未验证，V1 延期 | 分 Provider 单独设计、集成与发布执行证据 |

既有 PostgreSQL AotHost 的 audit sink 成功不能推导 retention 成功；只测 typed admission 的原生 fixture 也不能推导真实认证/Settings/Quartz 整链原生支持。CI 保持既有手动禁用状态，准备 gate 不等于启用或已执行。

## 10. 迁移、退出与有界后续工作

生产启用前必须按顺序完成：停止旧清理自动/人工入口与旧 writer → 应用 additive migration、ledger 回填/校验与反复活约束 → 同步部署 ledger-aware sink（禁止混合旧 writer）→ 以 preview 核对范围/hold/quarantine → 明确配置启用。新版 schema readiness 拒绝旧版本/半迁移状态；§6.2 的数据库约束阻断误启动的旧 writer，必须有旧插入 SQL 负控证明，不能只靠部署文档。回填需要维护窗口，V1 不承诺在线双写升级。

旧 `IAuditLogCleanupAppService`、DTO 的危险日期覆盖和仓储 `DeleteOlderThanAsync` 在 V1 正式切换时退出：调用点迁移到新 retention service，旧数据清理如确有运维需要单列一次性迁移工具，不装回框架默认服务；替代文件/专用旧链清理测试移到 `99_RecycleBin/issue-132-audit-retention/`，保留路径和说明。旧查询的迁移另列任务，不因本项再建正式 AuditLog retention Provider。不能让旧 API 同时裸删旧表并让新 API 删正文。

后续建议四个有界切片：S1 强类型协议/Setting/权限 admission/静态装配；S2 PostgreSQL ledger+hold+receipt+事务与升级；S3 内核+preview/run 查询+同一入口的调度与旧清理退出；S4 Provider 矩阵、集成/native 负控、证据与声明同步。S1–S3 未完成前清理保持禁用；S4 未通过不得宣称生产或 NativeAOT-verified。具体实施清单见[计划](../plans/2026-10-27-issue-132-audit-retention-governance-implementation.md)。

## 11. 原 Issue 验收映射与剩余裁定

| #132 验收 | 本次设计落点 / 交付界限 |
| --- | --- |
| 过期/未过期/跨租户/受保护 case 与当前缺口 | C01–C10 实际旧 EF 仓储复现 + 正式 sink 静态事实；hold/evidence hint 明确不是已存在的原生保护契约 |
| 唯一 owner、公共契约、索引权限边界 | §3–§8；Accountability owns lifecycle，Application admission，PG owns atomic facts |
| 可执行 Provider 集成与 NativeAOT 门禁范围 | §9，现有 test/AotHost 入口与场景、sentinel、负控；本轮不虚称已执行新门禁 |
| 实施/延期/拒绝与有界后续 | §1/§10；建议有界 V1 + 明确延期项；最终裁定待维护者确认 |

需维护者裁定的产品取舍为：V1 采用固定数据库 PostgreSQL、Global 期限且租户无 override、治理元数据与去重 ledger 暂不 GC。这些选择已给出推荐方案与代价，不阻止本次形成可审查设计。未经裁定不勾选远程验收、不关闭 Issue；Issue 关闭只代表设计裁定完成。
