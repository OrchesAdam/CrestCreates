# CrestCreates platform status — 2026-10-10

Baseline: master `5d3de8c5` (PR #145 merge: #127 RabbitMQ static bindings S1-S4 + R1/R2 review rounds;
PR #142/#139/#140 merged before). Remote verified 2026-10-14. In flight: none —
wave 2 #126/#127 both merged; #128 CRUD JSON is the next candidate.
CI: all GitHub workflows manually disabled (development-phase
local-first verification; Testcontainers suites runnable locally via podman socket).
Workflow: GitHub PR flow, never auto-merge; replaced files archived to 99_RecycleBin,
never deleted; never output credentials.

## 2026-10-23 — Issue #130 ORM matrix / soft delete / tenant / UoW (in flight)

- Branch `feat/issue-130-provider-matrix-soft-delete-20261022-104210`, PR #154, based on master
  `8c8a71de6a47d7c0eb94e8260e9f94035b22a477`; **not merged**, CI paused, awaiting unified review.
- S1–S5 delivered: provider support matrix, real soft delete, live-row filtering, per-operation
  tenant isolation and persisted ownership, SDK assembly/core consolidation, compile-time
  provider rejection and EF generated CRUD relational Q01.
- Final matrix: [docs/Feature/OrmProviderSupportMatrix.md](docs/Feature/OrmProviderSupportMatrix.md).
  Summary: EF/FreeSql/SqlSugar SQLite JIT core behaviors supported with execution evidence;
  EF PostgreSQL UoW and generated CRUD Q01 supported; FreeSql/SqlSugar non-EF generated CRUD
  rejected (CCCREPO001/002); unknown provider rejected; SqlServer/MySQL and ORM trim/NativeAOT
  remain **unverified**.
- Evidence head: `e57abee5` (S5 docs/evidence follow). Key counts: OrmProviders 257/257,
  CodeGenerator 461/461, GeneratedCrud JIT 28/28, GeneratedCrud NativeAOT 3/3, PG UoW 4/4,
  Boundary 176/178 (2 existing ControlPlane evidence-ledger guards).
- Review history: S2 R1/R2, S3 R1/R2/R3, S4 R1 and final S5 R1 all closed; probes are in-repo where accepted. Final S5 R1 fixed EF tenant batch writes and platform-base reads; matrix PG scope split accordingly.
- Migration/behavior changes: soft delete becomes logical; stamped batch/predicate delete rejects;
  SDK `GetQueryable` rejects; SDK required-tenant host reads fail closed, optional host sees shared
  only, shared rows are host-owned; SqlSugar client is scoped; FreeSql extension no longer scans;
  retired tenant/soft-delete configuration hooks reject; generated repositories/CRUD are EF-only.
- Not verified: SQLServer/MySQL and other databases; PostgreSQL SDK soft-delete/tenant/audit/
  cancellation details; ORM trim/NativeAOT; in-flight SQL cancellation; cross-process concurrency;
  EF-specific optional marker host tightening.
- S5 deliverables are complete on this branch; only PR merge and post-merge AGENTS/evidence sync
  remain, and those are the maintainer's decision.

## Delivered capabilities (master)

- Core platform mainlines: Tenant / Setting / Feature / Permission / BackgroundJobs /
  ObjectMapping / Dynamic API (compile-time generated) / Workflow + HumanTask /
  Audit (write-redact-query). Audit retention (cleanup/governance) not delivered.
- Module system: compile-time aggregation only (BuildTasks + generator), closed.
- Agent governance plane (Authoring -> deterministic review -> Control Plane):
  review artifact store and package/evidence artifact store with InMemory +
  PostgreSQL providers (additive migrations); activation request/review/gate
  present but in-memory (`InMemoryRuntimeActivationGate`,
  `InMemoryDescriptorActivationAuditor`).
- JsonContracts build-time toolchain: `src/Tooling/CrestCreates.JsonContracts.BuildTasks`
  (+ `.BuildTasks.Core`, `.Tool`) generates deterministic JSON contract roots consumed
  by the official STJ source generator; no runtime reflection/scanner fallback.
- LocalEvent typed dispatch + dead-letter replay (#126 delivered via PR #142 on 2026-10-13;
  **#146 hardening S1-S4 merged via PR #148 on 2026-10-14 (merge commit `0c6e574a`; S2/S3/S4
  review rounds R1-R3 closed with the regression probes in-repo)**):
  single typed dispatch index (`ILocalEventDispatchRegistry` / frozen `LocalEventBinding` /
  `LocalEventInvoker.Prepare`, freeze-time merged validation; undeclared events fail
  deterministically, no fallback), per-assembly generated registration (`AddLocalEvents()`,
  CCCLE001-005; explicit cross-assembly activation contract, no scanning), **per-Host codec
  declaration/contribution split (`LocalEventRegistration` + `LocalEventJsonCodecRegistration`
  + `AddCodecs`; no process-global table; frozen `JsonTypeInfo` is locked with `MakeReadOnly`)**,
  **one `Prepare` per execution resolving the whole handler batch (same instances execute;
  transient constructed once)**, replayable codecs statically bound by the JsonContracts
  toolchain (CJC015/016/017 closure diagnostics; camelCase invariant for LocalEvent),
  shared replay core (`LocalDeadLetterReplayExecutor`: fresh re-read, decode+prepare before
  `MarkRetrying`, independent Execution/StateWrite/Release facts, cancel-safe bounded Pending
  restore with preserved release facts, corrupted/admission failures never consume budget),
  EF `LocalReplayKey` additive column + **`LocalContextVersion` additive column with a
  repeatable PostgreSQL upgrade script (real PG evidence: old schema upgraded twice, fresh-scope
  round trip; other providers unverified)**, **stable replay keys + explicit same-version replay
  aliases (attribute/builder/generator; unknown non-empty keys never fall back)**, **shared
  admission core on publish/direct-dispatch/channel enqueue+consume/replay: exact Active
  descriptor association, persisted descriptor-id match, explicit governed-history adoption,
  tenant-mode matrix with authoritative tenant resolution and exact-id equality (empty/damaged
  ids refused as such, never converted into host), context snapshots/work items persisted with
  failures, LIFO context frames with independently protected unwind**, **explicit execution
  checks and opt-in `IEventValidator` activation (freeze validates DI registration; implicit
  publish-time validation removed with a migration diagnostic)**, **hosted governance
  readiness via the existing bootstrap chain (`AddCrestLocalEventGovernance` =
  `EventRegistryBootstrapper` + `BootstrapCoordinator` + `local-governance-readiness` gate,
  bounded wait, zero-provider assembly error, not-ready never cached; generated emission only
  when the compilation references the Local runtime)**. Not pledged: replay concurrency
  mutual exclusion, crash-leftover Retrying recovery, per-handler checkpoints, cross-tenant
  DLQ browsing, general payload migration.
- RabbitMQ static bindings + canonical event contracts (delivered by #127, merged via PR #145 on
  2026-10-14; R1+R2 review rounds closed — 10 findings fixed with the reviewer probes in-repo;
  baseline code facts: the reflection subscription bridge returned empty in any real
  host, publisher/consumer identity and routing disagreed, retry dropped properties and ignored the
  delay, three divergent registration paths; issues log
  docs/superpowers/plans/2026-10-13-issue-127-issues-log.md): `[CrestEvent]` declarations generate
  per-assembly IEventDescriptorProvider + `AddEventDescriptors()` activation (CCCEV001-003);
  Integration-scope events get source-generated codec roots + per-host `EventJsonCodecRegistration`
  service contributions (CJC018-020; camelCase wire payload policy; no `[LocalEvent]` requirement;
  same-origin reactivation idempotent, cross-origin conflicts diagnostic; no process-global codec
  registry); per-assembly
  `[RabbitMqSubscribe]` generation with closed typed invokers (CCCRMQ001-005); per-host frozen
  `RabbitMqBindingRegistry` (publish index by concrete type, wire index by (EventId, EventVersion),
  legacy alias table, per-queue consume plans with sequential handlers, required manifests) validated
  entirely before broker I/O; single `AddRabbitMqEventBus` entry (module/legacy context overloads
  forward; context overloads are typed-codec adapters in the same freeze); composition hosted service
  + consumer readiness gate (startup order independent); additive envelope (EventId/EventVersion,
  EventType = canonical Name); SDK-confirm publish core (homemade confirm ledger removed, exact-once
  channel lease release); business retry/DLQ settlement state machine (delay honored, MessageId/
  CorrelationId preserved, tolerant retry-header parsing, no fake redelivery success), dedicated
  consumer channel + settlement gate + bounded shutdown drain. R1+R2 review fixes (2026-10-13, all in
  PR #145 head): recovery-window connection ownership (pool waits for SDK recovery instead of
  replacing the connection; caller cancellation stays OperationCanceledException; the channel
  supervisor is recovery-phase aware — a closed channel during SDK recovery is never terminal —
  and a stable connection with a persistently closed channel fails the host after a short grace),
  controlled requeue with bounded backoff for failed redelivery confirms (no business-budget
  consumption), envelope field prechecks (payload null), delivery-time whole-batch handler
  pre-resolution plus required-manifest DI registration probe, per-registration codec validation
  (same-origin conflicts are deterministic and order-independent), and LocalEvent default-path
  coexistence (the shared IEventValidator slot is never claimed by the kernel). Not pledged: competing-consumer
  semantics beyond broker defaults, quorum DLX losslessness, per-handler checkpoints, replay
  concurrency exclusion, crash-window dedup.
- `src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql`: durable runtime state,
  Control Plane reference data, review/package artifact persistence, native AotHost
  evidence.

## Evidence boundaries

- Active CI native publish+link+run gates: PG AotHost sentinels
  (incl. `CRESTCREATES_WORKFLOW_CONDITION_AOT_OK`), Memory JSON contracts,
  ControlPlane JsonContracts, Asset/Procurement Golden App fixtures, and the
  CapabilityEndpoint native HTTP request gate (loopback requests, success/400/404
  negative cases, process reaping, evidence artifact `aot-native-evidence-<sha>`;
  merged via PR #135); MCP/Agent.Tools main fixtures run their existing
  publish-and-run tests in active CI; LocalEvent typed dispatch/replay native gate
  (five scenarios + sentinel `CRESTCREATES_LOCALEVENT_TYPED_REPLAY_NATIVE_OK`, linux-x64,
  in-memory DLQ store only, evidence `local-event-typed-replay.json`; gate step prepared
  in `ci.yml`/`full-validation.yml`, workflows remain manually disabled; merged via
  PR #142). RabbitMQ static bindings trim + native gates (true per-mode publish: trim runs a trimmed JIT
  product, native links; real broker vhost prepared by the harness; sentinel
  `CRESTCREATES_RABBITMQ_STATIC_BINDINGS_NATIVE_OK`; six scenarios incl.
  cross-assembly typed payloads, same-short-name isolation, refusals to a real DLQ, explicit
  legacy alias, retry-then-success, budget exhaustion, unroutable typed failure; linux-x64;
  evidence `rabbitmq-static-bindings-{trim,native-aot}.json`; gate step + rabbitmq service
  prepared in both workflows, keep disabled; merged via PR #145).
- #123 baseline finding (recorded, fixed): native HTTP success requests returned
  500 because `DynamicApiResponse<object>` was not declared by the fixture's
  JsonSerializerContext; fixed by declaring the envelope root (golden-host pattern),
  not by disabling AOT JSON constraints. Per-item matrix:
  `docs/review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md`.
- HTTP gate scope limits: linux-x64 only; no auth/permission/tenant/bootstrap chain;
  dependency-project IL2026/IL3050 warnings remain visible but are not this gate's signal.
- UnitOfWork unified registration (delivered by #124 via PR #136): single
  Data.Abstractions authority (`AddUnitOfWork` + provider-side typed
  `AddUnitOfWorkProvider` bindings; no reflection/assembly scanning; deterministic
  duplicate/missing/conflict diagnostics), requiresNew isolated through child DI
  scopes with ambient context following (pre-injected EF dependencies bind to the
  current UoW; other providers fail closed); legacy Infrastructure dual
  factory/manager archived; native gate `CRESTCREATES_UNITOFWORK_NATIVE_PIPELINE_OK`.
- UnitOfWork kernel/state (delivered by #137 slices 1-5, merged via PR #139 on
  2026-10-12 including three review-fix rounds): unified `UnitOfWorkOptions` (transaction toggle / Required+RequiresNew /
  isolation / timeout / provider with null-inheritance rules), caller-frame
  activation protocol (`BeginScope` sync carrier + `StartAsync`; AOP non-async
  callbacks + OnExit sync restore), four orthogonal result dimensions
  (participation state / transaction outcome / notification outcome / released),
  join participant completion with rollback-only rejection, `Committed` recorded
  immediately at commit confirmation, `Unknown` never rewritten by rollback
  attempts, post-commit notification failure preserved as committed,
  operation-layer token composition (official repositories/providers combine
  incoming CT with the managed execution token), provider flush/commit/notify
  split; managed-chain ambient routing (descendant-or-self visibility + tenant/
  declaration/connection validation before routing), abandonment terminates AND
  discards uncommitted tracking; provider capability declarations with
  pre-execution gate (SQLite={Serializable} locally measured; PostgreSQL=
  {ReadCommitted, RepeatableRead, Serializable} with CI gate wiring and actual
  transaction-level assertions, evidence = final head CI run; FreeSql/SqlSugar
  declare termination/
  discard, no isolation levels); CAP participation lease (provider-owned live
  view, read-only identity, borrow rules; handoff doc
  `docs/superpowers/specs/2026-10-10-issue-137-cap-handoff.md`); dead contracts
  archived (`99_RecycleBin/issue-137-contracts-state`); implementation issue log
  `docs/superpowers/plans/2026-10-10-issue-137-implementation-issues-log.md`.
- Review scores (8.5/10, ~70%) are subjective single-review estimates, not
  production-readiness guarantees; support claims must point to per-item evidence.

## Open work entry (#121 orchestration)

- Wave 0: #122 baseline calibration (merged via PR #134).
- Wave 1: #123 native gates / evidence matrix (merged via PR #135); #124 UnitOfWork
  (merged via PR #136); #137 UnitOfWork follow-up (design+plans merged via PR #138;
  slices 1-5 + three review-fix rounds merged via PR #139); #125 CAP entry
  consolidation (design rev.4 + S1-S4 + four implementation review-fix rounds merged
  via PR #140 on 2026-10-12; CAP unit 41/41, CAP integration 38/38 on the merged head;
  issues log docs/superpowers/plans/2026-10-12-issue-125-implementation-issues-log.md).
- Wave 2: #126 LocalEvent (design + S1-S4 + eight implementation review-fix rounds merged
  via PR #142 on 2026-10-13; JSON toolchain 126/126, LocalEvent integration 47/47 and
  NativeAOT five scenarios on the merged head; issues log
  docs/superpowers/plans/2026-10-13-issue-126-issues-log.md); #127 RabbitMQ static bindings
  (design + S1-S4 + R1/R2 review-fix rounds merged via PR #145 on 2026-10-14; unit 58/58,
  generators 365/365 full, JsonContracts 131/131, real-broker integration 37/37, real trim +
  NativeAOT gates per mode on the merged head; issues log
  docs/superpowers/plans/2026-10-13-issue-127-issues-log.md); #128 CRUD JSON.
- Wave 3: #129 Dynamic API exit / #130 ORM matrix / #131 capability map.
- Candidate: #132 audit retention design. New: #133 B12 unsupported-intent negative case.
- At most 2 implementation PRs in flight; verify at current head before merge.

## Decided boundaries (do not reopen without new evidence)

- #87/#88 closed. Durable activation aggregate/CAS/replay, submission identity and
  request+HumanTask transaction are D-class (application-specific, #88 decision).
  Review/package artifact persistence does NOT prove durable activation.
- C88-01 outbox consumer activation: delivered by #118/#119 (generated
  `[GenerateOutboxConsumerActivation]` mainline; hand-written factories removed).
- #50/#51 rejected, #76/#57 deferred; do not start without a failing acceptance case.

## Known gaps

- B12 unsupported-intent negative case missing (#133, bounded).
- CAP entry consolidation (#125): merged via PR #140 (single entry
  `AddCrestCapDistributedTransaction`; in-memory fakes/module/dead bridge/participant
  engine archived; two-layer config validation; atomic publish over the #137
  lease/borrower contract with scan-recovery delivery; PG+RabbitMQ integration evidence);
  compensation concurrency / process-crash recovery not pledged;
  CRUD DTO JSON contract (#128); legacy controller exit + route convention residue
  (#129); ORM support matrix (#130); placeholders and long-tail modules (#131).

## Preserved assets

- Retained proposal container `crest-asset-approved-inventory-87` exists (exited;
  not started or re-verified this check): schema `asset_live_retained_20260923`,
  tenant `asset-live-eval-tenant`; locator
  `docs/review/2026-09-23-asset-live-retained-result.json`. No more DeepSeek calls,
  no replacement/cleanup/schema changes without approval; no approval/activation.
- Review-store test container `crest-review-store-tests-87` exists (exited); do not
  use retained live containers for new tests.

## History

- Predecessor handoff (2026-09-30) archived verbatim:
  `docs/review/2026-10-10-memory-handoff-archive.md`.
- Older histories: `docs/review/2026-09-20/23/24/29-memory-history.md`,
  `docs/review/2026-09-30-package-evidence-handoff-history.md`.
- Calibration record (#122): `docs/review/2026-10-10-framework-architecture-review-follow-up.md`
  (revision table, #103–#108 absorption mapping, B12 verdict).
- Old quota/wake automation, worktree/agent dispatch instructions and private
  connection material are intentionally not carried forward; see the archive.

- 2026-10-15：#128 基于 cf44e947 完成①整体审查修复及②CRUD lifecycle/③跨进程两阶段 harness：完整输入/bundle/manifest hash 成功状态、设计时隔离且生成当前 CRUD/JSON、最终配置/RID 指纹、五类路径边界、实际 manifest+物化类型装配门禁、双向 provenance/框架类型身份校验、ownership 内容 comparer、外部包首消费与 TFM/RID 构建矩阵。当前 CodeGenerator 444 pass + 1 S3 object-result 红/445，JSON 单元 131/131，PackageTests 非英文 locale 29/29，DynamicApi 73 pass + 2 S3 查询红/75。S2 两个 driver roots 旧红转绿；S3/S4 尚未实施，CI 保持暂停。

- 最终补证：实现提交 2580129438a31af4ed2557595d3c9ce56e09b0e9，Release 两阶段 driver 3/3；LocalEvent 真实 NativeAOT publish/link/run 1/1（linux-x64，11 场景+sentinel，publish/run 均 0，无 IL2026/IL3050，evidence sha 精确绑定实现提交）。②③及本轮审查修复完成，S3/S4 尚未实施。报告 docs/review/2026-10-15-issue-128-s2-review-and-completion.md。

- 2026-10-16：#128 S3/S4 全部实施完成（已于 2026-10-17 随 PR #150 合并；CI 保持暂停）。S3：FilterDescriptor 有界 wire 域、typed 无 fallback 结果契约、ExplicitHost 贡献/Map 期一次权威汇总与必需 identities/metadata 校验、typed query adapter 替换反射 QueryExecutor、cancel 传播与 E2E 五动作 HTTP 证据（20/20）。S4：真实工具链 fixture（Domain/Feature/Host）+ 织入 Rougamo.Fody 的生成服务跑在唯一 UoW 内核（内存 provider，#137 公共契约；提交/回滚/响应写失败独立/陈旧并发戳四例），未知 id 404 与错误 DTO（ProblemDetails）闭包修复，原生 publish/link/run 门禁（linux-x64，15 场景 + 表达式树构造探针 + 两变异反例 + 分场景/总 sentinel，3/3）；decimal 过滤按原生探针矩阵收敛为 operator-free（Equals/CompareTo/HasValue），fixture memory query provider 是 §8 的静态执行收敛（BCL EnumerableQuery 在 AOT 不可执行）；复跑全部受影响门禁并修复既有 CapabilityEndpoint AOT fixture 的 S3-2 typed-root 回归（13/13）。最终计数：CodeGenerator 451、JsonContracts 131、PackageTests 29、BuildTasks 8、OrmProviders 124、DynamicApi 115、E2E 20、fixture JIT 11、RabbitMQ 58+37+2门禁、LocalEvent native 1。支持声明：正式 CRUD+JSON 主链 NativeAOT-verified（linux-x64）；EF 查询翻译/UoW 组合与其他 RID 未验证。设计审查已闭环（PR #150 两轮：R1 修复 `9849defa`、R2 修复 `6d54f384`、文档 head `2483d4cd` 复审通过；R1 四条 + R2 三条全部关闭，探针原样入库）。**已合并**：PR #150 于 2026-10-17 以 merge commit `196b6f66048ef32d644eeb856d00d58ae9d34965` 合入 master（base `de50b337`；47 提交）。后续状态：旧 CRUD bundle 路径与 `#warning` 已随 #129/PR #152 于 2026-10-22 归档退出（merge `2f4c5402`）；EF 翻译面证据（decimal/DateTime CompareTo 形态）仍未验证。细节见 docs/superpowers/plans/2026-10-14-issue-128-issues-log.md。

- 2026-10-20：#129 S3 完成（本地分支 `feat/issue-129-generated-http-controller-exit-20261017-114410`，未合并；CI 保持暂停，待维护者统一审查）。Ticket 11 动作正式 ExplicitHost 投影（五权限、CT、命令/查询矩阵）；UoW 单 owner 落在样例 EF 模块，平台不再预置 provider，启动期零/重复 binding fail-closed；`[UnitOfWorkMo]` 之前实际未织入的基类/手写写动作已补 Rougamo.Fody（六写各 1 次事务，非 Attribute 文本证据）；body-stamp 428/409/回读语义；transport-neutral failure facts + Web `CrestErrorResponse` 桥接（绑定失败 400、资源不存在统一 404、未知 500 脱敏、取消事实独立），CRUD typed 404 envelope 与 MCP `CAPABILITY_*` code 保持；typed validators 编译期发现/typed DI 注册/Map 期 CCHTTP007/invoker 前置执行（IValidationService additive CT overload；生成器不再覆盖用户 validator、不对可空 string 生成 NotEmpty）；租户终态解析拒绝未知/禁用租户（不再落回 host 库），双租户 per-schema 选库/读写/回滚/上下文恢复有真实 options contributor 证据；真实 Production 进程 + PostgreSQL（HTTPS、OpenIddict 登录、health/create/read/validation 400/428/409/delete 200/404、SIGTERM 2s）独立于 WebApplicationFactory。最终计数：SaaSHelpdesk 119 pass/11 fail/130（11 全为既有红：10 条 `POST /api/user` 404 + 1 条 ITenantProvider），Gap 12/12、FailureBridge 6/6、DualTenant 3/3、TenantBoundary 3/3、DynamicApi 138/138、JsonContracts 162/162、CodeGenerator 552/552、CRUD NativeAOT 23/23、Procurement 29/29、Asset 29/29；Asset E2E 缺 `ASSET_MANAGEMENT_RUNTIME_CONNECTION_STRING` 前置（19 fail 全为该环境项），Boundary 2 条 ControlPlaneReferenceData evidence-ledger 既存 WIP，Web 4 条为 worktree `.git` 文件环境项。支持声明：Ticket 正式兼容 HTTP 主链为真实 Host JIT + PostgreSQL 已验证；Ticket NativeAOT（N01/N02）、EF 其他 Provider、其他 RID 未验证。细节见 docs/superpowers/plans/2026-10-17-issue-129-issues-log.md「S3 第二增量」。

- 2026-10-20（S3 统一审查 R1 修复，head 见 PR）：1 P1 + 5 P2 已逐项闭合。P1：租户目录 resolver（Header/Query/Cookie/Route/Subdomain）改为独立 DI scope 读取，业务 DbContext 只在租户帧内构造；双租户探针按生产 resolver 图 + middleware + 生产 options 通过，并新增真实 `Program` + 两 schema 的 HTTP 验收（登录、读写隔离、428/409、FK 失败回滚）。P2：OCE 按 R/K/L 身份分类（None/第三/未取消 R → NonCaller 500 脱敏，仅 R 取消 → CallerAbort 抑制；绑定阶段同规则）；业务内部 Json/Format 不再归 400；presenter 消息改由既有 converter/资源本地化；六个手写 Ticket 动作补业务侧权限检查；共享 scalar（含 string）统一比较 converter。复跑：SaaSHelpdesk 133/11/144（11 条既有红不变）、Application 177、DynamicApi 140、JsonContracts 162、CRUD NativeAOT 23、Procurement 29、Web 110/114（4 条 worktree `.git` 环境项）。IntegrationTests 的 `OpenIddictScopes` 缺失在上一 head 同条件复现，属既存环境项。未合并，CI 保持暂停。

- 2026-10-20（S3 统一审查 R2 取消项修复，head 见 PR）：五项已关闭，取消项两处修订闭环。A：新增 `UnitOfWorkCancellationSnapshot`（Core.Abstractions）+ `IUnitOfWorkCancellationSource`；内核 scope 保留 caller token，`ExecuteAsync` catch 与 `[UnitOfWorkMo]` OnExceptionAsync 在 ambient 恢复前把 snapshot 附加到原始 OCE 的 Data（不包装异常、不改 #137 committed/unknown），pipeline 据 execution/caller 身份与 deadline 事实分类——真实 EF 内核 deadline → ManagedDeadline、内核转发的 caller 取消 → CallerAbort、第三 token → NonCaller（审查 4/4）。B：presenter 的 `Present`/`Write` 共享边界先按 `HasStarted`/`RequestAborted` 抑制 JSON，与分类无关（审查 3/3）。C：Capability 旧断言由 TimedOut 同步为 Cancelled + facts Kind Cancelled（178/178）。复跑：SaaSHelpdesk 137/11/148（11 条既有红不变）、Web 113/117（4 条 worktree `.git`）、Application 177、DynamicApi 140、OrmProviders 124、CRUD NativeAOT 23、Procurement 29、JsonContracts 162。未合并，CI 保持暂停。

- 2026-10-20（S3 统一审查 R3 取消组合路径修复，head 见 PR）：快照升级为**帧链**并接入正式组合。嵌套 Required：`CaptureCancellationSnapshot` 沿 owner/parent 链构建 inner→outer 帧，`Attach` 按 execution token 合并（外层不覆盖内层匹配记录），分类用最外层 root caller + 任一帧 deadline，避免把父帧 execution token 误当 HTTP caller。正式操作：`UnitOfWorkExecutionContext.CombineWithCurrent` 把 derived linked token 记录到产生它的帧（`RecordDerivedToken`），`ContainsToken` 匹配帧/派生 token；未匹配第三 token（含 deadline 已到期）仍 NonCaller。证据：R3 组合探针 8/8、R2 4/4；SaaSHelpdesk 145/11/156（11 条既有红不变）、Capability 178、OrmProviders 124、DynamicApi 140、Application 177、CRUD AOT 23、Procurement 29、JsonContracts 162、Web 113/117（4 条 worktree `.git`）、Data.Abstractions NativeAOT 1/1。未合并，CI 保持暂停。

- 2026-10-20（S3 统一审查 R4 三层 Required 帧序修复，head 见 PR）：R3 的帧链在第三层 Required 合并后为 `[内层, 根, 中间]`，root caller 误取中间帧。修复：内核新增 per-scope `_currentExecutionScope`，`BeginScope` 把当前最内层 scope（含 join）记录为真实 `executionParent`，捕获沿执行父链构建 inner→root；帧增加 `DepthFromRoot`，`Merge` 按深度排序（root 深度 0 在后），root caller 恒为真实请求 R。R4 三层探针 4/4（outer/inner deadline → ManagedDeadline、caller → Cancelled、third → NonCaller，frames=3、rootCallerMatchesRequest=True）；R3 8/8、R2 4/4；SaaSHelpdesk 149/11/160（11 条既有红不变）、Capability 178、OrmProviders 124、DynamicApi 140、Application 177、CRUD AOT 23、Procurement 29、JsonContracts 162、Web 113/117、Data.Abstractions NativeAOT 1/1。未合并，CI 保持暂停。

- 2026-10-20（#129 S4 legacy 隔离归档，head 见 PR）：正式链与 legacy 三包边界收口。`CrestCreates.DynamicApi.Legacy` 承接旧 registry/runtime/body reader/options/descriptors；`CrestCreates.CodeGenerator.Legacy` 承接仍被显式 Consumer 使用的 [CrestService] Minimal API 与静态 compatibility 生成器；`CrestCreates.Web.Legacy` 提供 `AddLegacyGeneratedApi`/`MapLegacyGeneratedApi`。正式 `AddCrestWeb`/`MapCrestWeb` 不再无条件映射 legacy，`CrestWebOptions.GeneratedApi` 移除。激活必须逐 service/action allowlist；Map 前 CCHTTP007 校验缺失/多余声明，CCHTTP005 拒绝 legacy 内重复或与 formal 的 method/route 冲突。SaaSHelpdesk 7 个 legacy service 显式 allowlist，Ticket 仅正式；LibraryManagement 同步迁移。归档：ControllerSourceGenerator/CrudControllerSourceGenerator/CrestCrudApiController/CrestApiController/GeneratedApiController/ApiOverride/CrudAction、DynamicApiAot controller 分支、CrudService 旧无参 stub/bundle/provider/bool ExecuteAsync 分支及专用测试（99_RecycleBin/issue-129-controller-exit，不入库）。守卫：默认 analyzer 无 legacy 生成器；正式 runtime 无 legacy 类型；正式 Web 不引用 Legacy 包；真实 Host legacy registry 不含 ITicketAppService；8 条 S4 守卫通过。证据：SaaSHelpdesk 150/11/161（11 条既有红不变）、Web 116/120（4 条 worktree `.git`）、DynamicApi 140、Application 177、Capability 178、OrmProviders 124、JsonContracts 162、Procurement 29、CRUD NativeAOT 23、Data NativeAOT 1、CompatibilityProjection E2E 20、CapabilityEndpoint AOT 13、Boundary 176/178（2 条既存 WIP）、CodeGenerator 503/503。Legacy 包未声明 trimming/NativeAOT-verified。

- 2026-10-21（#129 S4 消费者迁移与旧链退出，head 见 PR）：目标由“Legacy 隔离 + allowlist”修订为旧链彻底退出。SaaSHelpdesk 七个 AppService（Agent/Category/Customer/CustomerPortal/Dashboard/KnowledgeBase/SLAPolicy）改为正式接口投影，逐动作保留旧 route/verb/query/body/If-Match/permission，自定义写动作补 `[UnitOfWorkMo]`，body validator 显式 `RequiresValidator`，KB `GetPopular` 用可空 count 保持旧缺省语义。LibraryManagement Book/Category/Loan/Member 改为正式自契约投影适配器（`Projections/*HttpProjection.cs`）：继承 CRUD 的 `GetListAsync` 显式分页 query、`QueryAsync` 保留原 `QueryRequest<TEntity>` body、可空参数回落旧默认值；Loan/Member 的 `[UnitOfWorkMo]` 在本程序集织入（FodyWeavers）；适配器注册一次，业务服务仍由原 owner 注册。Host 改为 `AddCapabilityRuntime` + `AddCrestCapabilityEndpoints` + `AddGeneratedAppServiceHttp` + `MapCrestCapabilityEndpoints`。三个 Legacy 项目（`CodeGenerator.Legacy`/`DynamicApi.Legacy`/`Web.Legacy`）及旧测试 `git mv` 到 `99_RecycleBin/issue-129-controller-exit/`，从 `.slnx`、项目引用、`Directory.Build.Aot.props` 与 analyzer/package 分发移除；正式路由冲突预检保留在 `MapCrestCapabilityEndpoints`。测试迁移：`CompatibilityProjection.E2E` Greeting 改正式自契约（20/20）、`CapabilityEndpoint.AotFixture` 改正式投影（13/13，native publish/link/run）、新增 SaaS 正式激活基线（路由每条一次、legacy 程序集不加载）与 Library 42 动作契约基线（permission/required services/JSON roots，2/2）；旧输出形状专用测试归档。守卫：`CapabilityEndpointBoundaryTests` 断言构建图/解决方案零 Legacy 项目、正式程序集无旧类型/引用；契约 fixture `legacyProjects.retained=[]`；主解决方案构建 0 error。证据：SaaSHelpdesk 151/11/162（11 条既有红不变）、Web 99/99、DynamicApi 140、Application 177、Capability 178、OrmProviders 124、JsonContracts 162、Procurement 29、CompatibilityProjection E2E 20、CRUD AOT 23、CapabilityEndpoint AOT 13、Data AOT 1、Boundary 176/178（2 条既存 WIP）；Library IntegrationTests 仍受既有 `OpenIddictScopes` 测试环境缺失阻断（上一放行 head 同条件复现），因此本轮以正式贡献契约基线验证 42 路由，未把该环境红计为迁移通过。CodeGenerator 全量 441/441（归档旧输出形状专用测试后由 503 降至 441）；主解决方案构建 0 error。Ticket NativeAOT 仍属 S5；未合并，CI 暂停。

- 2026-10-21（#129 S4 R2/R3 复审修复，code head `013d1188`）：旧链退出仍成立；所有项直接收口正式链，不恢复 Legacy。01：Agent 创建漏校验 → 手写 `CreateAgentDtoValidator`（可被 precompile 发现）+ `IAgentAppService.CreateAsync.RequiresValidator=true`；HTTP 非法邮箱 400 且 repository 零写入回归。Library 对照修正：上一 Library Host 未注册 `IValidationService`，legacy body 校验实际未启用；R2 初版误加 Loan validators 导致正常载荷被拒，已撤回归档并加“不得要求 IValidationService”守卫。02：缺工具链/退役声明 → 默认 analyzer 每次编译收口：缺工具链 **CCHTTP002 Error**（构建失败）；显式 LegacyStatic（enum Obsolete）、隐式 legacy 静态声明、方法级 projection → **CCHTTP001 Error**；builder 同步拒绝 LegacyStatic。真实 MSBuild：无工具链 exit 1 + CCHTTP002、方法级/LegacyStatic exit 1 + CCHTTP001。03：正式 `CompatibilityHttpResultMapper` 删除旧 `IServiceProvider`/无 metadata 反射 overload 与 `MapAotJson`，仅保留 `JsonTypeInfo` 必需 overload；旧实现归档，公共表面守卫通过。最终证据：SaaSHelpdesk 152/11/163（11 既有红不变）、DynamicApi 141/141、Web 102/102、E2E 20/20、CapabilityEndpoint AOT 13/13、Library 契约基线 2/2、CodeGenerator 447/447、Application 177、Capability 178、OrmProviders 124、JsonContracts 162、Procurement 29、Boundary 176/178（2 既存 WIP）、CRUD AOT 23、Data AOT 1、主解决方案 0 error。Library IntegrationTests 仍受既有 OpenIddictScopes 环境缺失阻断；S5 未开始，未合并，CI 暂停。

- 2026-10-21（#129 S5 Ticket NativeAOT 与最终交接，code head `5e3878b2`，docs/evidence head `743fc783`）：新增 `tests/Framework/Api/CrestCreates.TicketHttpProjection.AotFixture` + `.Tests`。local class-level anchor 引用真实 `ITicketAppService`，由正式 AppService HTTP/JSON 工具链生成 11 动作 bundle；运行真实 TicketAppService/DTO/validator（fixture 仅用继承真实规则的闭包 validator 以获得 precompile 可发现性）。仅替换内存 Ticket/SLA 仓储、内存 UoW、fixture identity/permission checker 与 failure presenter/classifier；Ticket/SLA 查询使用有界静态 queryable，不使用 BCL EnumerableQuery/Expression.Compile/反射动态 LINQ；未手写同名 DTO/等价 service/JSON root table/endpoint map。原生 15 场景（create/get/all/by-customer/update stamp/assign+active SLA/resolve/close/delete If-Match/unknown 404/invalid body zero-business/permission 403/business failure/metadata rejection）+ `CRESTCREATES_TICKET_NATIVE_OK`；两变异（wrong-expectation、wrong-validation-status）exit 1 无总 sentinel。JIT 1/1、原生门禁 3/3（linux-x64 publish/link/run），证据 `tests/artifacts/aot-native-evidence/ticket-native-http*.json`。P3：`CapabilityCompatibilityProjectionAttribute` XML 注释删除方法级推荐，明确方法级退役/CCHTTP001。支持声明：**Ticket 正式 HTTP 主链 NativeAOT-verified（linux-x64，该 fixture 范围）**；不推导 EF/PostgreSQL、真实认证/租户、整个 SaaSHelpdesk、Library HTTP、其他 RID。既有受影响门禁沿用 S4 最终 head 证据（SaaSHelpdesk 152/11/163、Web 102、DynamicApi 141、Application 177、Capability 178、OrmProviders 124、JsonContracts 162、Procurement 29、E2E 20、CapabilityEndpoint AOT 13、CRUD AOT 23、Data AOT 1、Boundary 176/178（2 既存 WIP）、CodeGenerator 447、Library 契约基线 2）；主解决方案 0 error。Library 完整 HTTP 仍未验证（既有 OpenIddictScopes 环境缺失）；未合并，CI 暂停。

- 2026-10-22：补正 #128 合并状态。功能代码已通过 PR #150 合入 master（`196b6f66048ef32d644eeb856d00d58ae9d34965`），旧文档中的“待合并”已更正；原留给 #129 的旧 bundle/Controller/Legacy 归档已通过 PR #152 完成（`2f4c54024d8e2e9c5b8aecad1a399669dadb28a6`）。NativeAOT 支持仍限已验证 fixture/linux-x64，EF/其他 ORM 查询翻译与 UoW 组合、其他 RID 未验证；CI 保持暂停。

- 2026-10-23（#130 S5 最终）：ORM Provider 支持矩阵/软删除/租户/UoW 在 PR #154 完成 S1–S5（分支，未合并）。最终矩阵落 `docs/Feature/OrmProviderSupportMatrix.md`。S1 基线 124/124 → S2 删除/查询闭环 → S3 租户/装配 → S4 生成期拒绝与 EF CRUD Q01；最终 OrmProviders 267/267（含最终审查 10 条探针）、CodeGenerator 461/461（G 红例闭合，含真实 MSBuild 消费控制）、GeneratedCrud 非原生 25/25（项目总 28 = 25 + NativeAOT 3）、PG UoW 4/4、Boundary 176/178（2 条既有 #69）。支持声明按矩阵逐项；SQLServer/MySQL、ORM trim/NativeAOT、PG SDK 细节、in-flight 取消、跨进程并发未验证。所有审查轮次（S2 R1/R2、S3 R1/R2/R3、S4 R1、S5 R1）已闭合；S5 R1 修复 EF 租户批次写与平台基类普通读，矩阵已拆分 PG 查询/写入/UoW 范围；PR 未合并、CI 保持暂停。
