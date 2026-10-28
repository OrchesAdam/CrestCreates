# CrestCreates platform status — 2026-10-23

Baseline: master `9c39e187` (2026-10-27 merge of PR #155: #131 capability map, archiving and
TenantId guard; parent base was `348c4d3c`, 2026-10-23 sync with freshly fetched origin/master;
includes PR #150/#152/#151 and #154: #128 CRUD JSON, #129 legacy exit, #130 ORM matrix/soft-delete).
#130 ORM Provider matrix/soft-delete was delivered via PR #154; S2 at `545f73d4`
passed independent R3 review. S3 at `19997ea4` passed independent R4 review:
prior S3 findings are closed. S4 at `037153db` passed its Q01 R2 review.
Unified final R2 at `2ea73e0d` approves technical merge within the corrected matrix:
both EF P1 defects and PG matrix P2 are closed. Fresh independent ORM 267/267
(including all 10 unchanged final probes) and CRUD non-native 25/25 pass.
PR #154 merged to master at `348c4d3c` on 2026-10-23 under explicit user authorization;
Issue #130 acceptance/evidence completed and closed as completed. CI unchanged.
Post-merge AGENTS/cross-Issue evidence synchronization remains a separate follow-up.
This checkout is now synchronized on master at `348c4d3c`; uncommitted local design/review records
were preserved and memory reconciled. Earlier reviews used an isolated exact-head copy;
earlier entries remain historical evidence.
CI: all GitHub workflows manually disabled (development-phase
local-first verification; Testcontainers suites runnable locally via podman socket).
Workflow: GitHub PR flow, never auto-merge; replaced files archived to 99_RecycleBin,
never deleted; never output credentials.

## 2026-10-27 — Issue #132 audit retention governance (V1 implemented; merge pending)

- Branch `feat/issue-132-audit-retention-governance-20261027-180000`, based on master `5f4cfc8f`.
  Not merged; CI manually disabled; local execution is the gate. Design and implementation plan:
  [design](docs/superpowers/specs/2026-10-27-issue-132-audit-retention-governance-design.md),
  [plan](docs/superpowers/plans/2026-10-27-issue-132-audit-retention-governance-implementation.md).
- V1 scope: PostgreSQL fixed database, Global retention settings, explicit Host or tenant scope
  (no AllTenants), dual-time cutoff (`OccurredAt` + `FirstAcceptedAt`), single-record holds with
  version/receipts, bounded body purge with atomic batch receipts, idempotent sink after purge,
  governance/quarantine protection. Deferred: other ORM providers, per-tenant policy, bulk/legal
  hold classes, archive/WORM, metadata GC, archive restore, and automatic scheduled execution
  (the scheduler adapter establishes no authenticated principal, so automatic registration stays off).
- Contracts: `CrestCreates.Accountability.Abstractions/Retention` (scope, policy + canonical hash,
  run/batch/hold/result, `IAuditRetentionStore`). Kernel `AuditRetentionExecutor`: persisted-policy
  binding, persisted batch budget, cancellation finalize, commit-Unknown → receipt reconciliation,
  mirror outcome reported separately from commit. Application admission
  (`AuditRetentionAppService`) is the single identity/permission/Setting/tenant gate shared by HTTP,
  direct DI and `AuditRetentionJob`; host tenant names resolve through `ITenantProvider` to the
  authoritative id. Generated HTTP projection is the platform anchor
  `AddCrestAuditRetentionHttp()` (`/api/audit-retention/*`). Legacy
  `AuditLogCleanupAppService`/`DeleteOlderThanAsync`/date cleanup DTO archived to
  `99_RecycleBin/issue-132-audit-retention/`.
- PostgreSQL: V016 ledger/runs/batches/holds/hold receipts/scope locks + body state, V017
  anti-resurrection FK plus repeatable typed backfill (invalid envelopes quarantined, originals
  unchanged). Sink dedup authority is the ledger and never rebuilds a purged body; batch/run/hold
  locks follow scope → run → record with receipts in the same transaction.
- Evidence (local, 2026-10-27; superseded by the R1 evidence below): Application admission 15/15;
  PostgreSQL retention 14/14; sink + migration 5/5; kernel/composition 31/31; P08 real
  identity/permission/Setting/generated-HTTP 2/2 (rejections leave the provider untouched);
  NativeAOT PG `AotHost` publish/link/run (linux-x64) emitted eight per-case retention sentinels
  plus `CRESTCREATES_AUDIT_RETENTION_NATIVE_OK`; fixture-only negative controls (wrong-count,
  hold-filter, receipt-fault) exit non-zero and never emit the aggregate sentinel.
- Support declaration: formal PostgreSQL retention V1 is **NativeAOT-verified** for the executed
  linux-x64 scenarios only. It does not cover EF/FreeSql/SqlSugar/MongoDB, other RIDs, Quartz
  identity/self-signed scheduling, or real authentication/Settings plumbing inside the native
  host (those admission paths are covered by the JIT P08 test). #132 closure is a design ruling;
  full retention acceptance follows this implementation review and explicit merge.
- Review round R1 (2026-10-28, independent review of `221fd2bc`, 2 P1 + 2 P2, all fixed on the
  branch): (1) terminal runs (`completed`/`budget-reached`/`cancelled`/`failed`) now reject new
  batches at the store with `RETENTION_RUN_NOT_EXECUTABLE` before deleting/receiving, and the
  executor converges the queued call to the persisted terminal result; (2) ambiguity recovery now
  trusts the exact committed receipt plus the lock-authoritative finalization, so a stale read can
  no longer report `failed`/0 after a committed delete, and cancellation returns a persisted
  terminal state instead of overwriting `completed`; (3) batch facts derive only from the
  immutable receipt (ordinal/deleted/committed-at), so recovery never folds several batches into
  one fact and normal-commit/replay/recovery/export bodies are identical; (4) mirror outcomes
  aggregate with first-failure precedence, and the design-required explicit receipt re-export is
  now `IAuditRetentionService.ExportBatchAsync` + `AuditLogging.Retention.Export` +
  `POST /api/audit-retention/batches/export` + `IAuditRetentionStore.ReadBatchAsync`.
  R1 also found and fixed an export-blocking precision bug: `ComputeCutoff` now truncates to
  microseconds so the in-memory run and the `timestamptz` round-trip agree, keeping one canonical
  batch-fact hash (previously the export/replay mirror conflicted).
- Evidence after R1 (local): Application retention 21/21; Accountability retention-filtered
  38/38; PostgreSQL retention 20/20 (incl. cutoff round-trip); sink/migration integration filter
  22/22; P08 2/2; AotHost JIT rehearsal green on all nine retention sentinels with the three
  fixture-only negative controls exiting 21 and never emitting the aggregate sentinel. NativeAOT
  publish/link/run re-run for R1 passed 1/1 (linux-x64): nine per-case sentinels including
  `CRESTCREATES_AUDIT_RETENTION_NATIVE_EXPORT_OK`, the aggregate
  `CRESTCREATES_AUDIT_RETENTION_NATIVE_OK`, and all three negative controls failing without it;
  the controls are fixture store-decorator mutants, not real SQL/transaction mutations.

## 2026-10-23 — Issue #130 ORM matrix / soft delete / tenant / UoW (final review passed; merge pending)

- Branch `feat/issue-130-provider-matrix-soft-delete-20261022-104210`, PR #154, based on master
  `8c8a71de6a47d7c0eb94e8260e9f94035b22a477`; **final technical review passed**, not merged,
  CI paused, merge awaits the maintainer.
- S1–S5 delivered: provider support matrix, real soft delete, live-row filtering, per-operation
  tenant isolation and persisted ownership, SDK assembly/core consolidation, compile-time
  provider rejection and EF generated CRUD relational Q01.
- Final matrix: [docs/Feature/OrmProviderSupportMatrix.md](docs/Feature/OrmProviderSupportMatrix.md).
  Summary: EF/FreeSql/SqlSugar SQLite JIT core behaviors supported with execution evidence;
  EF PostgreSQL generated query Q01 and raw-context UoW supported (PG generated writes/UoW rollback and two-repository combinations unverified); FreeSql/SqlSugar non-EF generated CRUD
  rejected (CCCREPO001/002); unknown provider rejected; SqlServer/MySQL and ORM trim/NativeAOT
  remain **unverified**.
- Final head: `2ea73e0d` (implementation `162bdbee`). Key counts: OrmProviders 267/267,
  CodeGenerator 461/461, GeneratedCrud non-native 25/25 + NativeAOT 3/3 (total 28), PG raw-context UoW 4/4,
  Boundary 176/178 (2 existing ControlPlane evidence-ledger guards).
- Review history: S2 R1/R2, S3 R1/R2/R3, S4 R1, S5 R1 and final R2 all closed; probes are in-repo where accepted. S5 R1 fixed EF tenant batch writes and platform-base reads; matrix PG scope split accordingly. Final R2 independently confirmed OrmProviders 267/267, CRUD non-native 25/25, Q01 5/5, and the narrowed PG declarations.
- Migration/behavior changes: soft delete becomes logical; stamped batch/predicate delete rejects;
  SDK `GetQueryable` rejects; SDK required-tenant host reads fail closed, optional host sees shared
  only, shared rows are host-owned; SqlSugar client is scoped; FreeSql extension no longer scans;
  retired tenant/soft-delete configuration hooks reject; generated repositories/CRUD are EF-only.
- Not verified: SQLServer/MySQL and other databases; PostgreSQL SDK soft-delete/tenant/audit/
  cancellation details; ORM trim/NativeAOT; in-flight SQL cancellation; cross-process concurrency;
  EF-specific optional marker host tightening.
- S5 deliverables are complete and final review passed; only PR merge and post-merge AGENTS/evidence
  sync remain, and those are the maintainer's decision.

## Delivered capabilities (master)

- Core platform mainlines: Tenant / Setting / Feature / Permission / BackgroundJobs /
  ObjectMapping / Dynamic API (compile-time generated) / Workflow + HumanTask /
  Audit (write-redact-query). Audit retention V1 (PostgreSQL) is implemented on the #132
  branch (merge pending); other providers/policies remain deferred.
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
  docs/superpowers/plans/2026-10-13-issue-127-issues-log.md); #128 CRUD JSON merged via
  PR #150 (`196b6f66`), merge-status closure via PR #151 (`8c8a71de`).
- Wave 3: #129 Dynamic API exit merged via PR #152 (`2f4c5402`);
  #130 ORM matrix/soft-delete merged via PR #154 (`348c4d3c`, 2026-10-23);
  #131 capability map / long-tail adjudication / TenantId guard merged via PR #155
  (`9c39e187`, 2026-10-27, ten commits, R1–R7 review rounds with 20 findings fixed;
  Issue #131 closed as completed).
- Candidate: #132 audit retention design. New: #133 B12 unsupported-intent negative case.
- At most 2 implementation PRs in flight; verify at current head before merge.

## #131 capability map, long-tail adjudication and TenantId guard (2026-10-23)

- **Merged**: PR #155 merged to master as `9c39e187` on 2026-10-27 (branch
  `feat/issue-131-capability-map-tenant-id-guard-20261023-174957`, ten commits from `348c4d3c`:
  S1 `8dfe83f5`, S2 `7b668b0d`, S3 `218ff151`, S4 `40bf53eb`, R1–R6 fixes
  `adbbf5dd`…`ae7ebe93`, wrap-up `bae2e5bc`). Issue #131 closed as completed; remote branch deleted.
  R7 technical review passed with 11 independent input forms and the entity matrix 60/60.
- S1 re-inventoried the 15 in-scope production projects at `348c4d3c` (SHA-256 per source,
  ProjectReference consumers, solution membership): `TOTAL_DIFF 0` versus the design inventory,
  so the 12 adjudications apply unchanged. Extra build inputs found beyond the design list and
  cleaned in S3: ProcurementApproval slnx member, two CI steps running `CrestCreates.Draft.Tests`.
- `docs/Feature/CapabilityMap.md` is now the single navigation surface: 12 adjudications / 15 projects
  with role, single composition entry, real call sites with file:line, functional status, execution
  evidence with boundaries, ownership and exit or upgrade conditions, plus a machine-readable index
  over all active `src/**/*.csproj` (135 before, 128 after archiving) enforced by
  `CapabilityMapCoverageArchitectureTests`.
- S3 archived 7 production projects + the generic Draft test project + the unconsumed
  `IEventStore.cs` to `99_RecycleBin/issue-131-capability-map/` (git records 28 R100 renames,
  nothing deleted) after removing 36 solution memberships, the two unused Workflow references
  and the CI steps. Anti-revival guards plus the existing "PG must not register legacy
  `IDraftStore`" test stay in place.
- S2 delivered the TenantId architecture guard: per-project analysis against the real build
  closure (shared framework > project bin > restored compile assets, deduped by assembly name),
  name sources bound by member identity, identity exits by port table + parameter-name net,
  bounded intra-method flow with same-project and first-party cross-project helper inlining,
  authoritative-source ledger, TI900 fail-closed coverage. Gate on this head: 156 active non-test
  projects, 2586 authored sources, 1024 identity exit sites, 0 violations, 0 coverage failures;
  TI-P/TI-N/TI-C/TI-M matrix plus TI-B01 directory behaviour (7/7) pass.
- It is a repository-wide bounded pattern guard, not a security analyser; generator template strings
  are not treated as bound runtime symbols and generated endpoints stay covered by their own fixtures.
- Support declarations unchanged: nothing was promoted to `NativeAOT-verified`; PluginSystem is now
  recorded as a bounded opt-in experiment (JIT-only, functional execution unverified) and
  FileManagement as a local/in-memory experiment. `docs/review/crestcreates-abp-comparison.md`
  carries a retraction banner and its PluginSystem "production ready" claim is corrected; the #130
  ORM matrix header now records the merge SHA `348c4d3c` and stays the only ORM support source.
- Local verification: root and All.slnx and all five layered solutions build with 0 errors;
  DependencyBoundaries 205/207 with the two pre-existing `EvidenceLedger` failures reproducing
  identically on pristine `348c4d3c` (baseline worktree run 176/178); Workflow 100/100,
  DescriptorDraft 132/132, Organization 223/223, Localization 12/12, FileManagement 24/24,
  Web 102/102, Application Tenants+Identity 102/102. CI remains manually disabled.
- Design-stage record retained below for provenance.
- Historical: functional design and implementation plan completed locally before implementation.
  Sources: `docs/superpowers/specs/2026-10-23-issue-131-capability-map-tenant-id-guard-design.md`
  and matching plan; static inventory in `docs/review/2026-10-23-issue-131-design-inventory.json`.
- 11 Issue scope groups become 12 decision entries / 15 production projects. Design proposes
  archiving 7 unconsumed/unfinished projects (incl. generic Draft, not DescriptorDraft), keeping
  8 with explicit boundaries; FileManagement and PluginSystem remain experimental, the latter JIT-only.
- TenantId guard design permits display/name lookup then directory-resolved Id, rejects names in
  context/claims/authorization/isolation/tenant partition keys, and requires real-source mutations.
- Source inspection used checkout `8c8a71de` plus read-only Git-object comparison to local
  `origin/master` at #130 merge `348c4d3c`: scope projects/sources/consumers/solutions unchanged;
  new host-null context/filter changes reviewed. Implementation must revalidate current master.
  No project archived, guard implemented, build/test/native
  run, GitHub Issue/PR changed, CI enabled or support level upgraded during this design turn.

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
  ORM support matrix and soft-delete semantics (#130) are delivered via PR #154 with
  per-combination evidence limits; placeholders and long-tail modules (#131) remain.
  #128 CRUD JSON and #129 legacy exit are delivered; their ORM SDK /
  real-database query composition evidence remains separately bounded.

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

## 同步保留的本地审查记录 — 2026-10-22

下列历史记录从同步前工作区保留；当前交付状态以最新合并记录为准，原始全文见 `99_RecycleBin/2026-10-22-issue-130-pre-pull/memory.md`。

## Design follow-up — 2026-10-14 (remote state verified)

- Remote master is `5d3de8c544cfc888aca23124303494929c4a1c2c`; #127 merged via
  PR #145 at 2026-10-14 00:06 Asia/Shanghai. R3 review closed all R1/R2 findings;
  independent final-head evidence: RabbitMQ unit 58/58, real-broker integration
  37/37, extra codec probes 2/2, true trim/native publish-and-run gates 2/2.
  The earlier baseline paragraph describes this local checkout, not the latest remote.
- #146 detailed design rev.1 and S1-S4 implementation handoff completed (design only,
  awaiting design review; no runtime implementation or new acceptance-test claim):
  https://github.com/OrchesAdam/CrestCreates/issues/146#issuecomment-6158450713 ;
  `docs/superpowers/specs/2026-10-14-issue-146-local-event-framework-hardening-design.md`;
  `docs/superpowers/plans/2026-10-14-issue-146-local-event-framework-hardening-implementation.md`.
  Decisions: per-Host codec contributions with frozen metadata; prepare once/use same
  handler instances; stable replay key + same-version aliases; explicit governance
  and tenant policies; tenant/correlation snapshot with legacy-null discrimination;
  separate execution/state-write/release outcomes. Existing tenant/registry/audit
  authorities remain authoritative. No replay CAS/crash-recovery/payload-migration claim.
- #146 S1/design review on PR #148 head `6f38f971` (2026-10-14): gate pending
  1 P1 + 2 P2 (bootstrap ownership, complete context fixtures, missing H03 baseline).
  Original suites independently reproduced 37+3 and 47+4 (expected red baselines,
  existing cases passed); alias-baseline deferral accepted. Review:
  https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5511969280 .
- #146 S1/design R2 on PR #148 head `d32f8b42`: R1's three items closed;
  rev.2 core design accepted, but gate still pending one P2 in the missing-audit
  negative fixture (empty tenant provider also rejects tenant A, hiding correlation
  checks). Independent suites: 37+3 / 47+8 (expected baseline failures). Tenant-only
  counterexample passed; registering A restores the intended red baseline. Review:
  https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5512052252 .
- #146 S1/design R3 on PR #148 head `20259f8d`: design and S1 gate PASSED;
  all R1/R2 findings closed. Missing-audit baseline now resolves tenant A first,
  preventing unknown-tenant false positives. Current-head integration independently
  reproduced 47 passed + 8 expected baseline failures; unit unchanged (prior 37+3).
  S2/S3 may proceed under rev.2; runtime/native/storage acceptance remains future work.
  https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5512085067 .
- Prefer #127 -> #146 -> #128; coordinate any in-flight shared tooling work before
  serial merge/reverification. CI stays manually disabled; no automatic merge/dispatch.

## #146 S2 implementation review — 2026-10-14

- PR #148 head `ccae06f2`: S2 gate pending 2 P1 + 1 P2 (mutable JsonTypeInfo
  accepted despite frozen options; Channel success requeued on scope-release error;
  cancellation discards release failure). Three independent temporary probes reproduced
  all findings. Review: https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5512427377 .
- Independent verification: unit 42/42; integration 59 passed + four agreed S3
  context baseline failures; CodeGenerator 365/365; JsonContracts 131/131; LocalEvent
  native publish/link/run gate passed. S1/design remains passed; runtime implementation
  still incomplete, not merged. No CI reenable/auto-merge/agent dispatch.

- PR #148 S2 R2 head `33ba7040`: all three S2 R1 findings verified fixed and
  their threads resolved. One new P2 remains: cooperative caller cancellation after
  handler entry is mislabelled Failed in OCE.Data instead of the specified Cancelled;
  two new regressions currently assert the wrong label. Temporary assertion-only
  contract probes reproduce Expected Cancelled / Actual Failed in both restore outcomes.
  Unit 42/42; integration 66 passed + four agreed S3 baseline failures; committed
  old probes 3/3; real LocalEvent NativeAOT publish/link/run gate 1/1 passed on this
  head. S2 gate pending this diagnostic fix. Review:
  https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5512664266 .
  Report: `docs/review/2026-10-14-pr-148-local-event-hardening-s2-review-round2.md`.

- PR #148 S2 R3 commit `f30a29bd`: S2-R2-LE146-01 verified fixed and thread
  resolved; no new actionable findings. S2 gate PASSED on this commit; S3 may proceed.
  Cancelled and AnyHandlerInvoked now remain separate facts. Independent unit 42/42;
  integration 66 passed + four agreed S3 baseline failures (state-machine + old
  probes 15/15 within suite); real LocalEvent native publish/link/run 1/1 passed.
  GitHub source branch ref verified at f30a29bd; PR metadata/commit listing still
  showed 33ba7040 at publication, so confirm PR sync before any merge. Review:
  https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5512732642 .
  Report: `docs/review/2026-10-14-pr-148-local-event-hardening-s2-review-round3.md`.
  S3/#146 overall remains incomplete; no merge/CI reenable/agent dispatch.

## #146 S3 implementation review — 2026-10-14

- PR #148 head `73bb4213`: S3 gate pending 4 P1 + 3 P2. P1: tenant mode and
  restore capability checks missing at publish/direct-dispatch/enqueue boundaries;
  frozen execution-check list still aliases mutable registration input; Required
  replay with empty TenantId executes as host; governance generator forces concrete
  Local runtime dependency into abstraction-only event contracts. P2: non-hosted
  governance composition waits on an unstarted gate; replay admission factory errors
  skip owned scope/frame cleanup; partial context activation loses acquired tenant frame.
- Independent unit 42/42; original integration 94/94; CodeGenerator 367/367;
  JsonContracts 131/131; LocalEvent boundary 3/3; real LocalEvent native
  publish/link/run gate 1/1 passed on this head. Eleven added runtime contract probes
  reproduced failures; abstraction-only compilation control passes for plain event
  and fails CS0234 with only Governance.Required added. No production source edits.
  Existing native fixture does not establish S3 governance/context matrix coverage;
  real PG upgrade/coexistence and native extension remain S4. S1/design and S2
  remain passed; S3 pending fixes, no merge/CI reenable/agent dispatch.
- Review: https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5513599241 .
  Report: `docs/review/2026-10-14-pr-148-local-event-hardening-s3-review.md`;
  probe source: `docs/review/2026-10-14-pr-148-local-event-hardening-s3-review-probes.md`.

- S3 R2 head `b6f7b973`: gate pending 2 P1 + 1 P2. R1-01 remains incomplete:
  admission checks provider presence but does not resolve tenant/exact Id or verify
  nonempty-correlation audit identity capability at acceptance boundaries. New R2-01:
  empty persisted tenant normalization lets Optional/HostOnly execute as host.
  New R2-02: activation + unwind failure wraps caller cancellation and reports
  Release.Completed despite frame release failure. Independent probes reproduced all.
- R1-02/03/06/07 fixed and verified. Per user's explicit current direction, R1-04
  is accepted with abstraction-only contracts + explicit Host governance composition,
  and R1-05 with a uniform 5-second readiness deadline. Those six threads resolved;
  original R1-01 remains open. The old 0.5-second refusal probe no longer matches the
  user's chosen deadline, and delayed active bootstrap also receives that deadline;
  neither is counted as a defect solely for differing from the old requirement.
  Spec rev.2 still describes automatic composition; synchronize design/migration
  docs to these user-directed changes during handoff.
- Independent current-head unit 42/42; original integration 106/106; CodeGenerator
  368/368; native publish/link/run gate 1/1 passed, SHA bound. Old runtime probes
  10 pass + 1 outdated expectation; new 13 failure probes reproduce actual gaps,
  plus 1 deadline-behavior control. Contracts build passes; explicitly composed Host
  starts registry/coordinator and publishes successfully. S4 evidence remains pending.
  Review: https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5514090047 .
  Report: `docs/review/2026-10-14-pr-148-local-event-hardening-s3-review-round2.md`;
  probes: `docs/review/2026-10-14-pr-148-local-event-hardening-s3-review-round2-probes.md`.
  No production edits, merge, CI reenable or agent dispatch.

- S3 R3 head `47bd9c19`: both P1 findings verified fixed and threads resolved.
  Shared recoverability validates authoritative tenant/exact Id/audit facilities;
  persisted damaged Id stays raw and is refused. Previous 13 actual-failure probes
  all pass. One P2 remains under existing S3-R2-LE146-02: non-caller OCE during
  activation plus frame unwind failure goes through generic restore catch and reports
  Release.Completed; independent source cancelled while caller token None reproduces
  Expected Failed / Actual Completed, zero handlers, Pending and budget unchanged.
  Ordinary failure and caller-cancellation combinations now pass.
- Independent current-head unit 42/42; original integration 114/114; prior probes
  13/13; new non-caller probe fails as above; real native publish/link/run 1/1 passed,
  SHA bound. S3 gate pending this P2; S4 evidence and design/migration-doc alignment
  remain pending. Review:
  https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5514425491 .
  Report: `docs/review/2026-10-14-pr-148-local-event-hardening-s3-review-round3.md`;
  probe: `docs/review/2026-10-14-pr-148-local-event-hardening-s3-review-round3-probe.md`.
  No production edits, merge, CI reenable or agent dispatch.

- S3 R4 head `ca85fa77`: remaining S3-R2-LE146-02 verified fixed and thread
  resolved; no new actionable findings. S3 gate PASSED; continue S4. Extracted
  frame cleanup errors now reach both caller-cancellation and ordinary restore-failure
  paths; non-caller OCE reports Release.Failed. Exact old probe passes; extra same-OCE
  plus scope/frame double-release-error combination passes with both facts preserved.
- Independent unit 42/42; original integration 115/115; extra probes 2/2;
  real native publish/link/run 1/1 passed, SHA bound. S4 still owes governance/context
  native scenario extension, real PG upgrade/coexistence evidence and design/migration
  docs aligned to explicit Host composition + uniform five-second readiness deadline.
  Review: https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5514690320 .
  Report: `docs/review/2026-10-14-pr-148-local-event-hardening-s3-review-round4.md`;
  probes: `docs/review/2026-10-14-pr-148-local-event-hardening-s3-review-round4-probes.md`.
  S3 pass is not S4/#146 completion. No production edits, merge, CI reenable or dispatch.

- 2026-10-14：PR #148 / #146 S4 head b34bd035 审查发现 3 P2，暂不放行：原生上下文场景未区分消费/重放/caller correlation；治理版本场景无竞争 Active 版本，移除精确版本过滤仍绿；Spec/迁移说明尚未同步已接受的 contracts-only Host 显式治理组合与统一五秒截止。精确源码托管 fixture 11/11 PASS；独立临时副本关闭 Channel/audit 激活并移除版本谓词后仍 11/11 PASS，证明证据缺口。自动审批两次超时，沙箱 VSTest socket/ILLink task-host 与 podman 限制阻止 native/PG 独立复跑，作者计数不算独立验证。报告：docs/review/2026-10-14-pr-148-local-event-hardening-s4-review.md。此前 S3 通过结论保持；未合并、CI 暂停。
- S4 审查发布：https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5515187584（3 个新增 P2 inline threads，head b34bd035）。

- 2026-10-14：PR #148 / #146 S4 R2 head c2cddf3b 三项 P2 全部修复、未发现新增问题，S4 门禁通过；结合此前 S1–S3，可进入合并。独立验证：正式 NativeAOT publish/link/run 1/1（linux-x64，11 场景+sentinel，完整 SHA 绑定）；集成 116/116 含真实 PG 旧表双次升级/fresh-context 往返；三个临时变异分别在目标断言处失败（Channel tenant、audit correlation、精确版本拒绝码），原生产源码逐字保持。独立临时 PG 容器与测试 schema 已清理，保留资产未触碰。报告 docs/review/2026-10-14-pr-148-local-event-hardening-s4-review-round2.md。未自动合并、CI 保持暂停。
- S4 R2 通过结论发布：https://github.com/OrchesAdam/CrestCreates/pull/148#pullrequestreview-5515511343；三个 S4 R1 inline threads 已补回复并 resolve。

- 2026-10-14：已核实 PR #148 合并（2026-10-14 17:09 Asia/Shanghai，merge 0c6e574a2872e1a6412e861b97e543910ae1b876，审查 head c2cddf3b）；#146 S1–S4 主链补全正式交付。下一主线按 #121/#146 编排进入 #128 CRUD JSON 编译期契约（GitHub 当前 open、评论为空，尚需详细设计），之后 #129 Dynamic API 旧入口退出；#130/#131 保持可穿插的后续任务。当前开放 PR 查询为 0；此轮仅核实与建议，未派发开发、未创建/修改 Issue，CI 未变更。

- 2026-10-14：#128 详细设计 rev.1 与 S1–S4 实施交接完成（设计待审查，未启动实现/新验收）。基线远端 master de50b33765fbdf8992c77ff15a126147c4cfeb7a。决策：BuildTasks 前置物化 CRUD ordinary Compile → 既有 JsonContracts → 官方 STJ；正式 GenerateCrudService(typeof(Entity)) partial service 声明明确程序集 owner；复用 Capability compatibility projection，typed body/input/result envelopes，禁止 object envelope/fallback；补有限 filter wire 与静态 query adapter，避免默认反射排序。发现基线测试 helper 串行 driver、test-only CRUD Attribute 与正式入口缺失，纳入 S1 真实 MSBuild/STJ 基线。Spec：docs/superpowers/specs/2026-10-14-issue-128-crud-json-compilation-contract-design.md；计划：docs/superpowers/plans/2026-10-14-issue-128-crud-json-compilation-contract-implementation.md。完整设计+清单已发布 https://github.com/OrchesAdam/CrestCreates/issues/128#issuecomment-6165502876 。无 runtime 修改、无开发派发/自动合并/CI 恢复。

- 2026-10-14：#128 S1 增量 1 head c15556dd 复核可接受，未发现新增阻塞 P1/P2；正式 Attribute 与真实单 driver 命名红例成立。独立原 CodeGenerator 套件 369 passed + 1 约定红（370，0 skipped）；另加临时声明探针 1/1 passed，author-only 全 Emit 成功、真实 Domain.Shared Attribute、driver 无错误；生成输出仍有 15 编译错误，不能当成完整生成链通过。S1 整体未放行，继续补真实 MSBuild/STJ、mapper/input/closed envelope、request/response/query 基线及诊断/target/manifest 冻结后再审；S2/S3 保持门禁。报告 docs/review/2026-10-14-issue-128-s1-increment1-review.md。无 PR 创建/生产修改/开发派发/合并/CI 变更。
- #128 S1 增量 1 复核评论已发布：https://github.com/OrchesAdam/CrestCreates/issues/128#issuecomment-6165803301；仅确认本增量，完整 S1/S2/S3 门禁未放行。

- 2026-10-15：#128 S1 整体 head 5ee8258b 审查发现 5 P2，S1/S2/S3 门禁未放行：查询 fixture 命名策略使红例未触达 Value setter；MSBuild 把 DTO 命名错误当同轮不可见且遮蔽 mapping/Id 错误；CCCRUD004 误拒排除字段/受保护 Id，并漏拒 protected-only ctor；信封基线只比对字符串且绑定将退出的 SG 输出。独立原套件 CodeGenerator 371 pass + 6 红/377、DynamicApi 73 pass + 2 红/75（均零 skipped）；mapper 探针 3/3 红，查询 naming 对照才触达 JsonElement 拒绝，五段注释即可使两个信封红例假绿。报告 docs/review/2026-10-15-issue-128-s1-overall-review.md，探针见同名 -probes.md。未修改实现分支、未创建 PR/派发开发/合并/变更 CI。
- #128 S1 整体 R1 审查已发布：https://github.com/OrchesAdam/CrestCreates/issues/128#issuecomment-6182405716；5 P2，待修订后复审。

- 2026-10-15：#128 S1 R2 head 5c49e9ef 复审：R1-01 查询命名及 R1-04 构造器关闭；R1-03 的 ExcludeProperties/Id 已修，但 TenantId/CreationTime/IsDeleted 等非输入平台字段仍误拒；R1-02 同轮 STJ 仍混入原四条命名错误；R1-05 正则仍可被注释满足且绑定旧 SG 输出。剩余 3 P2，S1/S2/S3 门禁保留。独立原套件 CodeGenerator 374 pass + 7 红/381，DynamicApi 73 pass + 2 红/75，均零 skipped；旧 mapper 探针 3/3 pass，新平台字段探针 3/3 fail，信封注释变异 2/2 假绿。报告 docs/review/2026-10-15-issue-128-s1-overall-review-round2.md；未修改实现分支/创建 PR/派发开发/合并/变更 CI。
- #128 S1 R2 复审已发布：https://github.com/OrchesAdam/CrestCreates/issues/128#issuecomment-6182606168；剩余 3 P2，待修订后复审。

- 2026-10-15：#128 S1 R3 head 13479ad4 复审，平台字段误拒绝关闭（六个 mapper 探针 6/6 pass），尚有 3 P2：MSBuild 非零退出/DTO 子串守卫可被无关错误满足；输入信封 symbols 使用丢弃生成输出的原 compilation；响应字段集合未断言、仍由正则决定成败。独立原 CodeGenerator 376 pass + 6 红/382、DynamicApi 73 pass + 2 红/75，均零 skipped。反向控制已确认官方 STJ 三种 DTO metadata 存在且无 SYSLIB1030 时原 MSBuild 仍绿，响应纯注释仍绿，helper probe 确认真实生成类型不在返回值中。报告 docs/review/2026-10-15-issue-128-s1-overall-review-round3.md；S1/S2/S3 门禁保留，未修改实现分支/创建 PR/派发开发/合并/更改 CI。
- #128 S1 R3 已发布：https://github.com/OrchesAdam/CrestCreates/issues/128#issuecomment-6182826636；剩余 3 项 P2，待测试证据修订后复审。

- 2026-10-15：#128 S1 R4 head 670a0555 复审，updated compilation 修订关闭，六个 mapper 探针保持通过；剩余 3 P2：MSBuild 在 build 前读取不存在的 obj，原套件新增 DirectoryNotFoundException 不是契约红；空的错误 owner 输入类仍满足输入基线；无关同名 JsonTypeInfo/响应/DTO 加空字段仍满足响应基线。独立原 CodeGenerator 375 pass + 7 fail/382（含该 fixture 错误）、DynamicApi 73 pass + 2 红/75，零 skipped。7 正向探针通过、两个假契约变异假绿；仅调整 build 顺序后 metadata 守卫能正确拒绝 STJ 已生成元数据的上一轮反例。报告 docs/review/2026-10-15-issue-128-s1-overall-review-round4.md。S1/S2/S3 门禁保留；未修改实现分支/创建 PR/派发开发/合并/改变 CI。
- #128 S1 R4 已发布：https://github.com/OrchesAdam/CrestCreates/issues/128#issuecomment-6183030530；剩余 3 P2，S1 暂不放行。

- 2026-10-15：#128 S1 R5 head 94c24031 复审，R4-01 MSBuild 时序/归因和 R4-02 输入 owner/成员/roots 关闭；R4-03 类型身份及假契约拒绝有效，剩余 1 P2：ResponseRootViolations 漏检 DynamicApiResponse<PagedResultDto<ProductDto>>，只移除该外层 root 仍返回零违规。独立原 CodeGenerator 386 pass + 8 红/394、DynamicApi 73 pass + 2 红/75，零 skipped；输入完整模型 1/1 pass，响应完整模型 pass、缺分页外层 root 反例 fail（漏检），原 9 条结构反例全部 pass。报告 docs/review/2026-10-15-issue-128-s1-overall-review-round5.md。已关闭事项结论保持，S1/S2/S3 门禁暂保留；未修改实现分支/生产源码/创建 PR/派发开发/合并/变更 CI。
- #128 S1 R5 已发布：https://github.com/OrchesAdam/CrestCreates/issues/128#issuecomment-6183719222；仅剩分页响应外层 root 漏检 1 P2。

- 2026-10-15：#128 S1 R6 head 2ca5e9b3 复审通过，R5-CRUD128-01 分页响应外层 root 漏检关闭，未发现新增阻塞 P1/P2，S1 设计/基线门禁通过，可进入 S2 BuildTasks 前置物化 + JsonContracts/官方 STJ 单次 CoreCompile。独立精确 head 全套 CodeGenerator 400 pass + 8 约定红/408（含已入库 3 个 R5 探针，作者 397+8/405 为未加探针口径）、DynamicApi 73 pass + 2 红/75，零 skipped；8 个响应 root 删除、2 个输入 root 删除、完整模型及原 9 结构反例全过，八条红例名称与上轮一致。报告 docs/review/2026-10-15-issue-128-s1-overall-review-round6.md。S2/S3/S4 尚未验收；未修改实现分支/生产源码/创建 PR/派发开发/合并/更改 CI。
- #128 S1 R6 放行结论已发布：https://github.com/OrchesAdam/CrestCreates/issues/128#issuecomment-6183919489；下一阶段 S2，既有未实现契约红需按计划转绿。

- 2026-10-15：#128 基于 cf44e947 完成①整体审查修复及②CRUD lifecycle/③跨进程两阶段 harness，源码提交 25801294，文档交付 head dc48aad9，已普通快进推送原分支且原开发工作树同步干净。独立验证：定向39/39、CodeGenerator444 pass +1 S3红/445、JSON单元131/131、PackageTests非英文29/29、DynamicApi73+2 S3红/75、Release driver3/3、LocalEvent NativeAOT1/1（linux-x64，11场景+sentinel，evidence sha精确25801294，无IL2026/IL3050）。完整报告 docs/review/2026-10-15-issue-128-s2-review-and-completion.md；交付评论 https://github.com/OrchesAdam/CrestCreates/issues/128#issuecomment-6188709959 。S3 typed result/查询及S4 GeneratedCrud原生验收保持后续范围，未合并/恢复CI/派发agent。

- 2026-10-16：PR #150 / #128 实现 R1 审查（head 672462d3）完成，暂不放行：3 P1（多 Feature 公共 JSON roots 精确 context 冲突、默认 JIT Host resolver 顺序导致启动拒绝、已 Built CapabilityRegistry 仅按 Id 存在而接受不同权限契约）+ 1 P2（非 Int32 enum 数值过滤）。均有独立探针；CodeGenerator 451/451、JsonContracts 131/131、既有 DynamicApi 115/115、生成 CRUD HTTP/UoW 11/11、精确 SHA 原生门禁 3/3。报告 docs/review/2026-10-16-issue-128-pr150-review-round1.md；可应用测试补丁 docs/review/probes/2026-10-16-issue-128-pr150-r1.patch；GitHub review https://github.com/OrchesAdam/CrestCreates/pull/150#pullrequestreview-5545853237（四条行内意见）。未改开发分支生产代码、未合并，CI 保持暂停；EF 翻译/其他 RID 未验证。

- 2026-10-16：PR #150 / #128 实现 R2 复审（head 23e920e7，实现 9849defa）完成。R1 四条原探针转绿；仍暂缓合并，新增 3 P2：显式带 Modifiers 的 DefaultJsonTypeInfoResolver 被生成装配绕过、GetById/latest 导致正确精确 v1 与 v2 共存被误拒、冻结契约等价校验遗漏 RiskLevel。三条独立探针均红；原 DynamicApi125、Application177、CRUD JIT18、正式声明生成器定向33均过，精确 head 原生门禁3/3。报告 docs/review/2026-10-16-issue-128-pr150-review-round2.md；测试补丁 docs/review/probes/2026-10-16-issue-128-pr150-r2.patch；已发布审查 https://github.com/OrchesAdam/CrestCreates/pull/150#pullrequestreview-5550193846。未改开发分支生产代码、未合并，CI 保持暂停。

- 2026-10-17：PR #150 / #128 实现 R3 复审通过（head 2483d4cd，实现 6d54f384；两者间仅文档）。R2 三项 P2 已闭合，原探针方法体与上轮补丁一致；独立复跑 DynamicApi129/129、CRUD JIT20/20、正式声明生成器定向33/33、精确 head NativeAOT门禁3/3全部通过。未发现新增可复现阻塞，建议维护者合并；R1/R2七条审查线程均已 resolved。报告 docs/review/2026-10-17-issue-128-pr150-review-round3.md；native摘要 docs/review/2026-10-17-issue-128-pr150-r3-native-summary.json；放行审查 https://github.com/OrchesAdam/CrestCreates/pull/150#pullrequestreview-5555627048。未自动合并，CI继续暂停；EF/其他 ORM 查询翻译及 UoW 组合、其他 RID 未验证，legacy归档沿既有#129交接。

- 2026-10-17：已核对 PR #150 / #128 合并，当前远端 master 196b6f66048ef32d644eeb856d00d58ae9d34965。#129 详细功能设计 rev.1 已发布并回读核对全文一致：https://github.com/OrchesAdam/CrestCreates/issues/129#issuecomment-6228947951 。设计保留 Ticket 私有 setter/领域方法与手写 AppService，将11动作迁往通用 ordinary-Compile HTTP projection + JsonContracts + ExplicitHost；明确 GET 写动作兼容、Update权限与stamp收紧、失败转换桥接、真实租户选库/验证/UoW、legacy隔离及Controller/旧stub/反射辅助归档；含完整验收矩阵与S1–S5编排。Spec docs/superpowers/specs/2026-10-17-issue-129-generated-http-controller-exit-design.md；计划 docs/superpowers/plans/2026-10-17-issue-129-generated-http-controller-exit-implementation.md。当前为设计交接，未实施功能或派发任务；CI保持暂停，不自动合并。

- 2026-10-17：#129 S1 / PR #152 head 602d6e40 事实与设计R1复核完成，暂不放行S2：3 P2（未定义权限不等于仅SuperAdmin可用，GetAll端点GetAll/真实服务Search二层门禁未对齐；原S1同轮JSON/全局激活/契约冻结探针未交付；R06缺未删除检查/R07缺当前stamp精确比对及边界零副作用证据）。独立6绿+8约定红/14、额外真实PG权限manager/checker/AppService控制2/2通过。设计rev.2已发布并回读核对全文一致：https://github.com/OrchesAdam/CrestCreates/issues/129#issuecomment-6230158783 ；冻结正式五Ticket权限/GetAll统一Search、PlatformDefault/ApplicationOwned持久化owner及启动校验、Web唯一404与绑定局部400、legacy三项目依赖、framework缺失HTTP面范围和S1补件/S3启动与租户责任。审查：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5556429329 ；报告docs/review/2026-10-17-issue-129-s1-design-review-round1.md及事实控制补丁/evidence.json。原全量62既有红分类仅61需FQN补账；本轮未复跑全量/生产进程/原生。保留用户本地改动，未改开发分支生产代码、未合并、CI继续暂停。

- 2026-10-17：#129 S1 / PR #152 R2复审 head 09f73642，R1权限事实/五定义/真实grant-revoke控制关闭并resolved；S1仍不放行S2实现，3 P2：同轮STJ探针错误namespace造成CS0234假归因（修正FQN后真实build0errors+SYSLIB1030，原断言反而红）；U02用全局MemberRef计数替代Host owner，运行时单binding仍误拒；P09当前stamp正例却发送any-value-ignored-today，另R07缺修改后HTTP回读、R04/R08零事务/持久化守卫未全补。独立全量sample39过72失败/111=10目标红+62既有，51项归因FQN对齐；编译/fixture定向7/7过，FQN对照原负例1红，owner对照R01绿/U02红。复审https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5556968773；报告docs/review/2026-10-17-issue-129-s1-design-review-round2.md及两个对照patch/evidence.json。未改开发分支，未全跑458生成器/原生/生产进程/双租户，CI暂停、不合并。

- 2026-10-17：#129 S1 / PR #152 R3复审 head 05fa50cb，R2同轮FQN/STJ诊断、U02来源与owner区分、P09真实stamp三主项关闭并resolved；复核以真实Probe程序集+两个独立DI activation补证未激活Host的全局binding可见性，R1机制/冻结线程关闭（非双HTTPHost端点验收）。仍暂不放行S2实现：原R1-03剩余2 P2，R07/P09缺修改后HTTP stamp回读（故意缓存旧值的变异使原两条仍绿，新控制红）；Execute计数漏BeginScope+Start，真实PG事务已开而计数0，R04事务/持久化及R08事务断言仍缺。独立样例41过71失败/112=9目标红+62既有，62 FQN与R2完全相同；编译/fixture定向8/8过；两DI binding sentinel通过；故意错误stamp变异原2绿+新增2红。报告docs/review/2026-10-17-issue-129-s1-design-review-round3.md，evidence.json、反例patch及host运行说明已保存；复审https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5557430712 。未改开发分支；未独立全跑459生成器/原生/生产进程/双租户，CI暂停、不合并；引用程序集小范围设计增补仍需先于对应S2实现。

- 2026-10-17：#129 S1 / PR #152 R4 head 9c12ffb2 复审通过，R3两项P2与原R1-03剩余要求全部关闭并resolved，无新增阻塞P1/P2。R15修改后HTTP回读能拒绝永久缓存首次stamp变异（原R07/P09/P11绿，R15红）；仅补当前stamp映射的临时正向对照R07/R15/P09/P10/P11 5/5过。scope.StartAsync观察及真实PG CurrentTransaction非空控制P11过，R03/R04/R08零逻辑执行/观察start/未观察帧/业务持久化断言齐。独立完整sample42过72失败/114=10目标红+62既有，62 FQN与R3完全一致；CodeGenerator本轮未触及/未重跑，无新增支持声明。放行复审https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5557525933；报告docs/review/2026-10-17-issue-129-s1-design-review-round4.md及evidence.json、映射正向对照patch。下一步先准备并复审引用程序集声明范围的小设计增补，再S2实现；S3仍负责生产映射/唯一持久化装配/权限与验证/双租户/生产启动；S1放行不等于#129功能或PR最终合并验收。未改开发分支/启动S2/合并/恢复CI。

- 2026-10-17：#129 引用程序集声明范围小设计增补 / PR #152 head e09963ef 复审，方向认可但剩2 P2：DeclaredTypeFullNames错误承载外部DTO/契约身份，与#128产物声明/ownership精确校验冲突；取消未冻结caller abort/deadline/非调用方OCE的对外矩阵，现有pipeline统一Timeout→504而外层converter可落500，不能据原则文字实现R-06。独立新增探针+fixture 8/8；真实CLI清单正反对照2/2（原清单成功，只追加真实引用DynamicApiResponse即CCCRUD008）。S1基线放行保持，增补修订后再放行S2实现。审查已发布回读：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5557595844；旧8线程仍resolved，新2线程未resolved。报告docs/review/2026-10-17-issue-129-referenced-scope-review-round1.md、evidence.json及ownership审查补丁已存。未改开发分支/启动S2/合并/恢复CI；本轮不重跑完整样例/CodeGenerator/原生，不新增支持声明。

- 2026-10-17：#129 引用程序集增补 rev.2 / PR #152 head 1df99f4a 复审：所有权R1-REF129-01关闭并resolved（Declared只实际声明，Referenced独立且唯一symbol解析）；取消矩阵已补但剩1 P2 R2-REF129-01（原R1-02延续）：kernel-only deadline也使linked取消且OCE.token=linked，文档caller-abort与deadline条件同时成立；绑定阶段无条件caller-abort又覆盖foreign OCE→500。独立fixture+引用探针10/10过；真实net10 token组合两个反例sentinel通过，非HTTP链验收。原取消线程已回复保留unresolved，未重复开线程。S1放行不变；修订来源判定/同时取消优先级及H02/R-06后再放行S2。审查已发布回读：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5557634415；报告docs/review/2026-10-17-issue-129-referenced-scope-review-round2.md，evidence.json及取消反例源码/输出已存。未改开发分支生产实现/启动S2/S3/合并/恢复CI，本轮未跑完整样例/全套生成器/原生。

- 2026-10-17：#129 引用程序集增补 rev.3 / PR #152 head 26edb788 复审：上轮deadline-only、linked双取消优先级、foreign归因与响应抑制分开、绑定阶段同规则已修正；所有权保持关闭。剩1 P2 R3-REF129-01（原取消线程延续）：算法先匹配R/K再判None且不查来源已取消，DefaultHttpContext.R=None时默认OCE误归CallerAbort并抑制存活请求500；K=None误记deadline；未取消的R/L OCE亦误归因。独立10/10过，真实net10 token/DefaultHttpContext规范对照3个既有控制PASS+4个反例确认；非生产HTTP链验收。原取消线程已补最小有序条件并保留unresolved，无重复线程；S1放行不变，增补修订后再S2。审查发布回读：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5557672719；报告docs/review/2026-10-17-issue-129-referenced-scope-review-round3.md，evidence.json及对照代码输出已存。未改开发分支生产代码/启动S2/S3/合并/恢复CI，未跑完整样例/生成器/原生，不新增支持声明。

- 2026-10-17：#129 引用程序集增补 rev.4 / PR #152 head 87294148 复审通过，剩余取消P2关闭并resolved；所有权保持关闭，PR历史10条线程均resolved，无新增阻塞P1/P2。None优先、身份须查原始源已取消、L来源均未取消归NonCaller，覆盖上轮4反例。独立引用探针+fixture10/10过；真实net10 token/DefaultHttpContext规范对照12/12 CONTROL_PASS（L==R为显式别名判定条件，非底层链接API行为声明），不是生产HTTP取消验收。S1和增补设计门禁均通过，可按shared model/emitter→普通Compile前阶段产物→ExplicitHost/JSON进入S2；R-06a–f完整口径保留，执行与原生按S3/S5。复审发布回读：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5557692309；报告docs/review/2026-10-17-issue-129-referenced-scope-review-round4.md及evidence.json/控制台源码输出已存。未改开发分支生产实现/启动S2/S3/合并/恢复CI；未跑完整样例/全套生成器/原生，不新增支持声明。

- 2026-10-17：#129 PR #152 S2.1 head fe014a5e 实现审查R1：4 P2（显式ExplicitHost无ContractType仍编译并生成静态ModuleInitializer注册；同短名契约默认capability ID与信封名碰撞，显式不同prefix仍同信封；TypeParameters.Length误拒IService<Dto>闭合泛型；接口private helper被纳入动作集）。独立新增模型/声明10/10过、真实MSBuild/STJ同轮/激活3/3过；审查5控制全部在编译前置守卫通过后目标断言红（身份2例+其余3例），补丁可应用。先修当前声明/模型再落S2.2 emitter，边界草案可继续准备；S1及增补rev.4设计放行不撤回，不把未交付S2.2/2.3当回归。review发布回读：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5557889371；报告docs/review/2026-10-17-issue-129-s21-review-round1.md、evidence.json与probes/2026-10-17-issue-129-s21-r1-contract-probes.patch保存；新增4线程unresolved，旧10保持resolved。报告含emitter共享HTTP后端/CRUD业务前端分界及#128回归对应，不复制Task/CLI/stamp/解析器链。未改开发分支生产实现/启动S2.2/合并/恢复CI；未跑全套474/样例/原生，不新增支持声明。

- 2026-10-18：#129 PR #152 S2.1 R2 head f69b9586 复审：原4项中静态fallback/闭合泛型/private辅助方法3项关闭resolved；身份仍剩1 P2 S21-R2-HTTP129-01（原02线程延续）：SanitizeToken输出含连字符的信封FQN不能声明C#类型；A.B.IOrder/A_B.IOrder被同slug折叠，默认capability ID碰撞，显式不同prefix仍同信封。原探针逐字入库SHA a2f9b0f6d7775381298f99781618270fadf04fd405463f687eed9dfd11868a95，独立15/15过；新3探针均经authored零编译错误/模型无错守卫后在目标断言红，稳定no-build再现3红。review发布回读：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5560605019；14线程13resolved/身份1未resolved。报告docs/review/2026-10-18-issue-129-s21-review-round2.md及evidence.json/identity-probes.patch保存。S1/增补设计放行不变，先修合法且可区分的canonical identity编码再物化S2.2；不要求重开设计。未改开发分支生产实现/启动S2.2/合并/恢复CI，未独立跑479全套/样例/原生，不新增支持声明。

- 2026-10-18：#129 PR #152 S2.1 R3 head 023ac9af 复审：R2非法C#名、dot/underscore/大小写问题已修正，原R1五条/R2三条探针逐字未变，独立18/18过；仍剩身份编码1 P2 S21-R3-HTTP129-01（原02线程延续）：首位数字补无标识下划线，使005FHost与_Host都编码为_005FHost。两真实owner源码零编译错、PE emit均成功、模型无错误，默认capability ID与信封完全相同；新2探针在目标断言红。需首位数字也参与可逆编码或统一合法字母前缀，不能额外补无标识_。review发布回读：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5561244284；14线程13resolved/身份1未resolved，无新线程。报告docs/review/2026-10-18-issue-129-s21-review-round3.md、evidence.json、canonical-probes.patch保存。S1/增补设计放行及其余三实现关闭结论不变，先修这一编码边界再物化S2.2。未改开发分支生产源码/启动S2.2/合并/恢复CI，未独立跑482全套/样例/原生，不新增支持声明。

- 2026-10-18：#129 PR #152 S2.1 R4 head 7d98ea43 复审通过，身份剩余P2关闭并resolved，历史14线程全部resolved，无新增阻塞P1/P2。首位数字编码自身，005FHost→_003005FHost、_Host→_005FHost，消除条件前缀歧义；原R1/R2/R3探针逐字保持，独立20/20过。额外2检查过：9000多个ASCII/Unicode/特殊输入往返解码、去重/字段分隔/合法标识符片段；13组owner/contract/action信封FQN互异且同一ordinary Compile编译无错（有界语料，不宣称穷举证明或HTTP验收）。S2.1模型/声明首增量通过，可进入S2.2归一wire model+共享HTTP后端/普通Compile物化，B01–B04/JSON hook/生命周期/外部包验收沿既定边界；S2整体/S2.3仍待实现验收。放行review发布回读：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5561329896；报告docs/review/2026-10-18-issue-129-s21-review-round4.md、evidence.json及canonical-checks.patch保存。未改开发分支生产源码/启动S2.2/合并/恢复CI；未独立跑484全套/样例/原生，不新增支持声明。

- 2026-10-18：#129 PR #152 S2.2-A head ba753959 审查R1暂不通过，1 P1+5 P2（S22A-R1-HTTP129-01..06）：纯query冻结动作参数丢失编译错、route/query同Guid静默重排、If-Match仅元数据不保留；int/string reader类型别名不匹配、Task<int> as错误、sync string/void错误await；双契约同owner AddGeneratedAppServiceHttp调用歧义；manifest单hint混多artifact且漏configurator/ContentHash；Guid实际capability input未列RequiredJsonRootTypes；CRUD emitter未变，实际是两套HTTP发射链，未达两前端共用后端。独立原3/3过；新增最终14控制1过13红（route-first正向过、query-first实际invoker执行值交换；Header实际binder输出丢stamp），源码/模型前置守卫成立。JSON context stub仅编译隔离，不是STJ/HTTP/AOT验收；架构项来自代码事实。review发布回读：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5561887273；报告docs/review/2026-10-18-issue-129-s22a-review-round1.md、evidence.json及emitter-probes.patch保存，新6线程unresolved/旧14resolved。先收口共同wire model/后端和A输出再接B，B接线设计可准备；不将B/S2.3/S3未交付作为回归，S1/设计/S2.1放行不撤回。未改开发分支生产实现/启动B/合并/恢复CI；未跑487全套/样例/原生/完整Host/B01-B04，不新增支持声明。


- 2026-10-18：#129 PR #152 S2.2-A R2 head 0ada6120 复审，原输入映射P1、manifest与scalar roots两P2关闭并resolved；余3 P2延续原02/03/06：nullable enum合法query在生成binder抛ArgumentException，ref/开放泛型方法被接受后CS1620/CS0246（可模型期CCHTTP001拒绝，不要求新能力）；双真实owner程序集公开activation FQN相同，Host完整静态调用仍CS0433；共同wire/backend已实际接入两前端，但CRUD输入信封仍走独立EmitCapabilityInputEnvelope，未全部共用。独立手写/原探针24/24、CRUD mainline+typed adapter+real declaration59/59、GeneratedCrud fixture23/23含native publish/link/run绿跑15场景+总sentinel及两变异exit1无总sentinel，三证据精确绑定本head。新增探针1过4失败/5=非空enum控制绿，nullable/ref/generic/cross-owner四目标红。未全跑501/样例/#129真实Host/B01-B04，不从#128 native推导#129支持。复审发布：https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5562744044；报告docs/review/2026-10-18-issue-129-s22a-review-round2.md、evidence.json、emitter-probes.patch保存。20线程17resolved/3未resolved，无重复新线程；先闭合A剩余项再B实现，B接线设计可准备，S1/增补/S2.1放行不撤回。未改开发分支生产实现/合并/恢复CI。


- 2026-10-18：#129 PR #152 S2.2-A R3 head 516eabdc 复审，R2类型支持边界与跨owner激活两项关闭并resolved，原R2五探针逐字一致转绿；CRUD/手写信封确实共用EmitEnvelopeDeclaration，架构提取成立。但余1 P2 S22A-R3-HTTP129-01（原06线程继续）：旧CRUD string? IfMatch接共享core后变global::System.String，丢可空注解；缺header仍null。真实Attribute/CRUD model/emitter+ordinary envelope+官方STJ单driver，在RespectNullableAnnotations=true且保留null输出时原产物序列化抛JsonException(Path $.IfMatch)，仅恢复?同控制绿；不是只用CS8618报问题，也未冒充完整HTTP状态验收。独立29/29、CRUD59/59、补充15过1红/16（14 nullable scalar缺失/合法/非法/required缺失路径绿+注解修复控制绿），Native fixture23/23含publish/link/run及两变异，三证据精确本head。默认native通过与strict-nullable反例分账。先保留wire属性声明NRT/CLR root identity区别，再进B；不要求关闭Host选项/default!压warning/反射fallback。review https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5562994771；报告docs/review/2026-10-18-issue-129-s22a-review-round3.md、evidence.json、emitter-probes.patch保存，20线程19resolved/1未resolved无重复新线程。未全跑506/样例/#129 B01-B04/真实Host/Native，不扩大支持声明，未改开发分支生产实现/合并/恢复CI。


- 2026-10-19：#129 PR #152 S2.2-A R4 head 82fa399c 复审，原CRUD IfMatch可空性R3-P2关闭并resolved，共同信封架构已完成；旧R2/R3 probe逐字一致，独立45/45、CRUD59/59、Native fixture23/23含linux-x64 publish/link/run及两变异，三证据精确本head。新增2 P2分别新建线程：R4-01 TypeOfDisplay以Contains(Nullable<)判BCL值类型，合法引用DTO Review.Nullable<Body>?保留外层?而typeof两处CS8639，Box<Body>?控制绿，需语义CLR identity/NRT display分离；R4-02手写RequiresConcurrencyToken且无stamp形参的合成IfMatch仍globalString/Required=false，实际binder赋null，官方STJ SerializeToElement在RespectNullableAnnotations=true/Never下JsonException(Path $.IfMatch)，header有值/仅恢复?控制绿。新增5控制3过2目标红。先修当前两边界再B实现，S1/增补/S2.1及原CRUD关闭不撤回。复审 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5566117702；报告docs/review/2026-10-19-issue-129-s22a-review-round4.md、evidence.json、r4-emitter-probes.patch保存。旧20线程全部resolved、新2未resolved；未独立全跑522/样例/#129 B01-B04/真实Host/Native，不扩大支持声明，未改开发分支生产实现/合并/恢复CI。


- 2026-10-19：#129 PR #152 S2.2-A R5 head dcae5d60 复审通过，无新增阻塞P1/P2；原R4两项已关闭并resolved，22线程全部resolved。CLR/NRT display由Roslyn类型事实分离，parameter/root携带ClrTypeFullName，移除TypeOfDisplay名称启发式；合成IfMatch声明string?/CLR string，Required=false与input-only事实不变，官方STJ严格nullable缺失/有值均过。独立原50/50（R4 probe逐字未变）、额外CLR身份8/8（引用Nullable<T>/嵌套NRT/数组/Nullable<自定义struct>，single body及route+body carrier实际BodyTypes/roots/Input Type一致和值类型未擦除）、CRUD59/59、Native fixture23/23含linux-x64 publish/link/run、15必需场景+总sentinel及两变异exit1无sentinel，三证据精确本head。S2.2-A增量放行，可按既定设计进入S2.2-B：复用CLI/Task/transport/fingerprint/stamp、ordinary Compile/manifest、显式JSON DependsOn、官方STJ同轮、生命周期/TFM/RID/外部包B01-B04；S2.3真实Host/Map校验及S2整体/PR最终验收未提前放行。复审 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5566308510；报告docs/review/2026-10-19-issue-129-s22a-review-round5.md、evidence.json、r5-clr-checks.patch保存。未独立全跑527/样例PG/#129 B/真实Host/Native，不扩大支持声明，未改开发分支生产实现/合并/恢复CI。


- 2026-10-19：#129 PR #152 S2.2-B head 3a1ebb26 统一审查 R1 暂不放行，4 P2 S22B-R1-HTTP129-01..04：JSON display 重解析使 Box<int>? 丢 Nullable、Outer<int>.Inner<string> 误报 CCHTTP004；manifest hash 只核对重新生成的预期源码而未核对实际 Compile bundle（真实 Include 后改 api/widgets→api/forged 仍构建成功）；显式 ExplicitHost 缺 ContractType 被当作 legacy 跳过（真实构建 exit0、bundle空、仅CCHTTP002警告）；双锚点在 producer ToDictionary 抛重复键，CLI Fatal/exit3，MSBuild仅CCHTTP002，未到达后续声称的CCHTTP003。独立原 JsonContracts144/144、原真实MSBuild7/7、审查探针2正向过+5目标红/7，后三项均真实build确认；CRUD Nativefixture23/23，linux-x64 publish/link/run绿跑16PASS含15必需场景+总sentinel、两变异exit1无总sentinel，三证据精确绑定本head。四项闭合后复审B再S2.3；S1/增补/S2.1/S2.2-A放行不撤回。B03最后正式声明移除、B04真实引用程序集契约/DTO同轮控制仍需补齐，七例不能整体背书完整B01-B04。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5567406845；报告docs/review/2026-10-19-issue-129-s22b-review-round1.md、evidence.json、r1-probes.patch及real-build.py保存。未独立重跑535全套/样例PG/#129 Host或S5 Native，不扩大支持声明，未改开发分支生产实现/合并/恢复CI。


- 2026-10-19：#129 PR #152 S2.2-B R2 head 240e9754 复审：原R1-01符号绑定/03显式Host缺契约/04双锚点三项关闭并resolved；原7条探针逐字一致、独立7/7，JsonContracts151/151、真实MSBuild13/13（引用程序集/最后声明移除/Nullable与嵌套泛型官方STJ控制已补齐）。仍2 P2：S22B-R2-HTTP129-01延续原02线程，实际源检查用全文Contains，原始bundle仅放注释/#if false/未使用raw string时，活动改路由产物三类均真实build exit0/无CCHTTP006；普通直接篡改已正确exit1。新增S22B-R2-HTTP129-02：跨契约method/route全局检查漏RoutePrefix，不同prefix同relative误拒CCHTTP003，最终路径相同但拆分不同漏检exit0；两项均真实MSBuild确认且有正反控制。新增探针3正向过+5目标红/8；CRUD Nativefixture23/23，linux-x64 publish/link/run绿跑16PASS含15必需场景+总sentinel、两变异exit1无总sentinel，三证据精确本head。27线程25resolved/2未resolved（原02+新路由），无重复线程。先闭合两项再放行S2.3；S1/增补/S2.1/S2.2-A放行保持。复审 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5568118858；报告docs/review/2026-10-19-issue-129-s22b-review-round2.md、evidence.json、r2-probes.patch及两个real-build脚本已保存。未独立全跑541/样例PG/#129 Host/Map或S5 Native，不扩大支持声明；未改开发分支生产实现/合并/恢复CI。


- 2026-10-19：#129 PR #152 S2.2-B R3 head 9399b056 复审：原R2-02路由项关闭并resolved，共享FullRoute被emitter/producer共同消费，原四组合通过；非活动文本三变异均已拒绝。R2八条探针逐字一致、独立8/8，JsonContracts159/159含原R1七条，完整真实MSBuild20/20，CRUD Nativefixture23/23（linux-x64 publish/link/run绿跑16PASS含15必需场景+总sentinel，两变异exit1无总sentinel，三证据精确本head）。仍1 P2 S22B-R3-HTTP129-01延续原R1-02/R2-01：任意活动token连续匹配未绑定manifest指定FQN；原始bundle置namespace Decoy中、真实FQN保留改路由产物，两个symbol不同，当前核验仍零CCHTTP006。新增ownership探针2正向过+1目标红/3，真实build baseline0/普通篡改1+CCHTTP006/Decoy活动副本0零编译错及官方STJ完成；需绑定指定FQN自身DeclaringSyntaxReferences/范围或已识别完整bundle真实内容hash，不能只查FQN存在再搜整树。27线程26resolved/原产物线程1未resolved，无新重复线程；先闭合该项再S2.3，前序放行不撤回。复审 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5568801904；报告docs/review/2026-10-19-issue-129-s22b-review-round3.md、evidence.json、r3-ownership-probes.patch与real-build.py保存。未独立全跑548/样例PG/#129 Host/Map或S5 Native，不扩大支持声明；未改开发分支生产实现/合并/恢复CI。


- 2026-10-19：#129 PR #152 S2.2-B R4 head 7f60dbc3 复审通过，唯一 S22B-R3-HTTP129-01（延续 R1-02/R2-01）已关闭并 resolved，27/27 审查线程全部关闭。本轮实际对账按生产者 DeclaredTypeFullNames/metadata name 锁定当前 owner 的真实 symbol、唯一 source 声明、同一 bundle tree，再逐 FQN 比较自身完整活动 token；Decoy 活动副本不能替真实类型背书。原 R3 三条探针与补丁逐字一致且独立3/3；JsonContracts162/162（原R1七+R2八+R3三）、完整真实MSBuild21/21（错误命名空间变异拒绝CCHTTP006与恢复、引用程序集/官方STJ同轮、声明移除、生命周期、TFM/RID、外部包首消费均过）、CRUD Nativefixture23/23（linux-x64 publish/link/run绿跑16PASS含15必需场景+总sentinel，两变异exit1无总sentinel，三证据精确本head）。S2.2-B放行，可按既定设计进入S2.3真实Host激活/跨Host隔离/Map期fail-closed及Asset/Procurement迁移；S2整体与Ticket/S3、legacy/S4、#129原生/S5门禁保持。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5569492268 已逐字回读；报告docs/review/2026-10-19-issue-129-s22b-review-round4.md及r4-evidence.json保存。未独立全跑549/样例PG或HTTP/#129 Host/Map/S5 Native，共享CRUD原生证据不扩大支持声明；未改开发分支生产实现/合并/恢复CI。


- 2026-10-19：#129 PR #152 S2.3 R1 head 84cf10f1 复审暂不放行，2 P2：S23-R1-HTTP129-01正式生成贡献未携带/验证必需业务契约DI，真实Procurement Host仅RemoveAll<ProcurementAppService>仍启动并映射兼容路由，submit HTTP正常而首次兼容GET 500 No service，未实现冻结增补R-04的启动CCHTTP007/零路由；S23-R1-HTTP129-02 Asset生产Program只注册AssetApplicationService未注册AssetCompatibilityProjection，自契约invoker的GetRequiredService无法解析，真实Program注册图探针在provider构建前主动停止，确认应用服务/贡献已注册但自契约不存在，无PG连接。原S2.2-B及27已关闭线程不撤回，新建2线程（PRRT_kwDOOxUy6c6t9SCD/PRRT_kwDOOxUy6c6t9SCH），29总/27resolved/2open。独立DynamicApi130/130、共享model/emitter60/60、JSON162/162、Procurement原26/26+审查2正向过1目标红（重复激活、双Host激活/拒绝隔离均过）、Asset原29/29+生产图1守卫过1目标红；Asset历史git diff用例在archive首次因无.git失败，提供现有只读Git对象库后整套通过，不记产品回归。CRUD Nativefixture23/23，linux-x64 publish/link/run绿跑16PASS含15必需场景+总sentinel，两变异exit1无总sentinel，三证据精确head。自契约显式动作收口可保留，需同步仍写interface-only的Spec/fixture/XML。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5572906995 已逐字回读；报告docs/review/2026-10-19-issue-129-s23-review-round1.md、r1-evidence.json及s23-r1-host-probes.patch已保存。未独立全跑551/Asset PG兼容HTTP/Ticket或S5原生，共享CRUD原生不扩展#129支持声明；未改开发分支生产实现/合并/恢复CI。


- 2026-10-20：#129 PR #152 S2.3 R2 head abc7d4b3 复审通过，原两P2已闭合并resolved，29/29审查线程全部关闭。正式AppService贡献携带typeof契约的RequiredServiceTypes，Host聚合后bootstrap在映射循环前用IServiceProviderIsService查询注册存在性，缺失CCHTTP007含owner/contract，业务工厂不执行；Asset由业务owner显式AddScoped<AssetCompatibilityProjection>，HTTP激活不注册业务。原两份探针逐字与上轮patch一致，Procurement29/29含3探针、Asset生产图2/2、Asset原29/29、DynamicApi原130/130、额外零端点/不调用故意抛错工厂2/2、生成model/emitter+契约fixture定向61/61（含fixture9）、JSON162/162，均零skipped。CRUD Nativefixture23/23，linux-x64 publish/link/run绿跑16PASS含15必需场景+总sentinel，两变异exit1无总sentinel，三证据精确本head。S2.3放行，可按既定S3推进；Ticket/legacy/S5分别验收，Asset PG兼容HTTP仍未验证，不从注册图/Procurement或共享CRUD推导支持。接口/自契约Spec、fixture、XML同步成立；Addendum两行R-05重复编号为非阻塞小项。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5579559237 已逐字回读；报告docs/review/2026-10-20-issue-129-s23-review-round2.md、r2-evidence.json和required-service-probes.patch保存。未独立全跑552/Asset PGHTTP/#129原生，未改开发分支生产实现/合并/恢复CI。


- 2026-10-20：#129 PR #152 S3第一增量 head 8b00977f 审查完成，已核实Ticket 11正式端点/capability/permission矩阵、GetAll归属与五权限目录；新增2 P2需纳入下一增量：S3-I1-R1-HTTP129-01共享root规则由已激活贡献context放宽为任意JsonSerializerContext，标量及既有void/error均可被反射facade或改wire metadata满足，独立8控制4正向过4目标拒绝红（metadata.OriginatingResolver为Default仍Map，Guid改host:N、void字段host_code/host_message仍Map）；S3-I1-R1-HTTP129-02 wire授权元数据探针仍StartsWith(api/ticket)，正式RawText带/，实际筛0而HaveCount11失败，归一完整家族独立1/1通过，实际11条全部compat.appservice.ticket.*且无IAuthorizeData，无旧拼法路由混入。原29线程不撤回，新2线程PRRT_kwDOOxUy6c6uKtOs/PRRT_kwDOOxUy6c6uKtO1，31总29resolved2open。独立DynamicApi130/130、JSON162/162；原PG fixture切到现有Podman API后wire6+F02共4过3红，两条已声明桥接待办为空鉴权body及Resolve500/目标400，另一条为新测试假红；首次默认Docker socket不可用不算产品回归。Domain/SLA仍沿用既有测试侧UoW normalization，不推导生产owner已收口。CRUD Nativefixture23/23，linux-x64 publish/link/run绿跑16PASS含15必需场景+总sentinel，两变异exit1无总sentinel，三证据精确本head。S3继续进行，失败桥接/UoW/stamp/typed validation/双租户/H01-H08待办不作为新发现，S2.3放行保持。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5579769256 已逐字回读；报告docs/review/2026-10-20-issue-129-s3-increment1-review-round1.md、r1-evidence.json与increment1-probes.patch保存且apply-check通过。未独立全跑CodeGenerator/Helpdesk，不推导Ticket原生，未改开发分支生产实现/合并/恢复CI。

- 2026-10-20：#129 PR #152 S3 统一审查 R1（精确 head `993297dbbe990690e3c19b2e53b824624b8ccfaf`）未放行：1 P1 + 5 P2。P1=真实 Header resolver 提前在请求 scope 固定 Host HelpdeskDbContext，进入 tenant-B 帧后业务仓储仍读取 Host schema；独立双 PG schema 请求顺序反例已证实，现有3控制绕开解析顺序。P2=OCE 来源丢弃导致 None/第三token/未取消请求token错误返回空504；业务 JsonException/FormatException被全局归400；固定MessageFor绕过既有converter本地化；手写领域服务直接调用缺Update/Get/Search检查（未授权Resolve/Close实际落库）；共享string root漏比converter导致改wire仍Map通过（延续旧JSON线程）。旧路由元数据假红已关闭。独立复跑：原始Helpdesk119/11/130（既有10×User创建404+1×ITenantProvider）；reviewer新增16例5过11目标红；DynamicApi原138全过（加新string控制139/1）；双库原3过+新反例1红；Web定向原15过+本地化1红；CodeGenerator定向65/65、Application177/177、JsonContracts162/162、CRUD NativeAOT23/23（三份证据sha绑定本head，绿16PASS+sentinel、两变异exit1无sentinel）。未独立重跑作者CodeGenerator全552、Procurement/Asset及Production子进程；Ticket NativeAOT仍属S5。报告 `docs/review/2026-10-20-issue-129-s3-unified-review-round1.md`，evidence JSON及便携probe patch同目录/子目录。GitHub review 5581229277；未改生产源码、未合并、CI继续暂停。

- 2026-10-20：#129 PR #152 S3 R2复审（head `710b2a485f69b9c63d6c5cde349eb55b3087b9cb`）暂不放行S3。五项已独立闭合并关闭原线程：P1租户目录独立scope（生产resolver图4/4+真实Program双schema HTTP1/1，B登录/隔离/428/409/FK回滚/stamp不变）、业务JSON/Format安全500、本地化converter权威、六个领域动作直接权限、shared string converter（DynamicApi140/140）。保留原取消项S3-R1-HTTP129-02/P2两个修订点：K/L init属性正式Pipeline未赋值，真实EF UoW deadline与kernel转发caller取消均误归NonCaller（新4控制2过2目标红，真实事务/一次调用/OCEtoken/清理前置均过）；R已取消+NonCaller仍选择JsonHttpResult，未独立传输抑制（新3控制2过1目标红，结果选择探针不声称TCP发体）。独立原始Helpdesk133/11/144，11失败FQN与993完全一致；Web定向18/1/19（原16过）；Application177/177；Capability177/178唯一取消E2E仍断TimedOut，993同条件复现同红，需随取消契约同步；CRUD NativeAOT23/23，三份sha绑定710b2a48，绿16PASS+sentinel、两变异exit1无总sentinel。未重跑全CodeGenerator/JSON单元、Procurement/Asset、独立Production进程；Ticket Native仍S5。报告docs/review/2026-10-20-issue-129-s3-unified-review-round2.md，r2-evidence及r2-probes.patch（apply-check通过）；GitHub review5581910466逐字回读；36线程35resolved1取消open。未改生产源码/合并/恢复CI。

- 2026-10-20：#129 PR #152 S3 R3复审（head `32a175bad7a232e0a6326c17b05490a1c4a8b74a`）暂不放行；保留原取消项S3-R1-HTTP129-02/P2，未新增重复线程。已验证typed snapshot桥接平层真实EF四模式4/4、共用响应抑制3/3、旧取消断言同步Capability178/178；旧五项关闭保持。剩余是组合token来源关联：嵌套Required外层取消捕获无条件覆盖内层匹配OCE的Data snapshot；正式CombineWithCurrent产出的effective与scope execution不相等，真实PG pg_sleep查询deadline/caller取消仍归NonCaller。新增8控制4过4目标红（单层deadline、内层独立deadline、第三token含scope过期均过；嵌套外层deadline/caller、操作effective deadline/caller均仅最终Kind失败）；真实事务/Required同owner/一次业务/原OCE/ambient清理等前置均过。独立原始Helpdesk137/11既有红/148，11失败FQN与上一head完全一致；Web定向19/19、Capability178/178、OrmProviders124/124、DynamicApi140/140、Application177/177、CRUD NativeAOT23/23，三证据sha精确本head，绿16PASS+总sentinel、两变异exit1无总sentinel。报告docs/review/2026-10-20-issue-129-s3-unified-review-round3.md，r3-evidence及r3-probes.patch（apply-check通过），GitHub review5582302460逐字回读，reply4322743530；36线程35resolved1取消open。未改生产实现/合并/恢复CI；未重跑全CodeGenerator/JSON单元、Procurement/Asset、独立Production进程，Ticket Native仍S5。


### #129 S3 统一复审 R4（2026-10-20，ae5eef87）

PR #152 head `ae5eef87321a15fb63d6a702498e784a03f9ffd9`：S3 暂不放行，原取消项 S3-R1-HTTP129-02/P2 保留。原 R3 8/8、R2 4/4、双租户 HTTP 1/1 均独立通过；新三层 Required 探针 3 pass/1 fail（有界来源检查复跑相同）：Capture 沿事务 owner 得 [C,A]，Merge B 后成 [C,A,B]，列表末项使根 caller 误读为 A.ExecutionToken，R 存活的外层 deadline 被误报 Cancelled。应修复共享执行父链/合并顺序。独立矩阵：SaaSHelpdesk 145/11/156（原 11 条 FQN 不变）、Web 定向 19/19、Capability 178/178、OrmProviders 124/124、UoW NativeAOT 1/1、CRUD NativeAOT 23/23（linux-x64、SHA 精确绑定；不推导 Ticket S5）。报告 `docs/review/2026-10-20-issue-129-s3-unified-review-round4.md`，证据与补丁同目录；GitHub review `5582676815`、原线程回复 `4323043792`，线程保持 unresolved。未合并、CI 保持暂停。


### #129 S3 统一复审 R5（2026-10-20，1cca837c）

PR #152 head `1cca837c600a6cf993aec76bc6f40e8985f09bd0`：原 S3-R1-HTTP129-02/P2 已闭合，本轮无新增阻塞项，S3 放行进入 S4。真实 executionParent 纳入 Required join，Capture 沿执行父链，DepthFromRoot 排序/最小深度选根；原 R4 4/4、R3 8/8、R2 4/4、双租户 HTTP 1/1 独立通过。新增四层精确 root/depth/caller 及手动 Required/RequiresNew→兄弟 scope→新根恢复控制共 5/5（正式 UoW/EF 注册、独立 PG 连接/事务；首次 HTTP fixture 共用连接限制单独记账，未计产品发现）。独立矩阵：Helpdesk 149/11/160（原11失败FQN不变）、Web定向19、Capability178、Orm124、UoW Native1、CRUD Native23全过；linux-x64原生证据sha精确本head。报告 `docs/review/2026-10-20-issue-129-s3-unified-review-round5.md`，r5-evidence与r5-probes.patch已保存。GitHub review5583015185、reply4323310454逐字回读，36线程全resolved。S4 legacy隔离归档、S5 Ticket Native/回归交接仍按计划推进；本轮未重跑全CodeGenerator/JSON/Application/DynamicApi、Procurement/Asset、Production子进程。未合并，CI保持暂停。


### #129 S4 复审 R1（2026-10-21，8f2f77ab）

PR #152 head `8f2f77ab1be721858b3e70cb3df33659c9e04c83`：S4暂不放行，4项P2+1项P3，S3放行保持。P2：旧公开AddCrestDynamicApi/MapCrestDynamicApi无allowlist映射3路由；Type.FullName清单丢程序集身份（只Allow A仍映射同名B）；raw route比较遗漏大小写/参数名等价且legacy-first→formal不拒绝（真实ASP.NET路由歧义及真实formal Map落证）；旧legacy源码守卫仍读正式包旧路径，.git根定位阻断掩盖新回归（仅补目录标记满足前置后本head Web119/1，旧1cca同条件117/117）。P3：README/pre-controller-removal快照未在版本化树，归档说明需可复核工件，不判断本地软删除。审查探针15=10过5目标红，补丁apply-check过。独立矩阵Helpdesk150/11/161且旧11FQN不变；生成器相关定向117、DynamicApi140、Json162、兼容E2E20、Package29、正式CRUD Native23全过；Boundary正确Podman端点176/178，旧head同条件同两条evidence-ledger缺项（首轮3条容器错误单列）。原生linux-x64三证据SHA绑定本head，绿16PASS+sentinel、负例exit1无sentinel；Ticket native仍S5。报告docs/review/2026-10-21-issue-129-s4-review-round1.md、r1-evidence、r1-probes已存；GitHub review5593880536及五评论逐字回读，41线程36resolved5open。先修四P2再复审S4，未合并/恢复CI。


### #129 S4 复审 R2：旧链退出（2026-10-21，154a7f90）

PR #152 head `154a7f90ec319e6972b614493a5746c372a0b57d`：三个Legacy项目/旧Add-Map/allowlist/引用确已退出，方向符合唯一主链；原S4-R1四P2+一P3按路径退休/当前守卫验证/归档工件入库关闭。但S4仍暂不放行，新增3P2：Agent.Create未声明RequiresValidator，真实已注册validator拒绝invalid-email但HTTP200且fresh-scope多1用户（同探针S3 1cca两例全过）；正式缺工具链CCHTTP002诊断随旧生成器一起归档，真实默认analyzer消费构建exit0/无贡献metadata（S3相同控制exit1+CCHTTP002），显式LegacyStatic带工具链亦静默空成功；CompatibilityHttpResultMapper仍公开三个无消费者IServiceProvider重载与反射Results.Json fallback/IL抑制，正式包旧响应支路未归档（不声称生成invoker正在调用）。修复均在正式链，不恢复Legacy，不进入S5。独立Web原102/102、generator定向120、DynamicApi140、Json162、正式E2E20、CapabilityEndpoint Native13、CRUD Native23全过；两组linux-x64原生evidence SHA精确本head，CRUD绿16PASS+sentinel、两负例各15PASS/exit1/无sentinel。Helpdesk首跑147/15/162（原11+4条Dashboard PG57P01，Dashboard独立4/4恢复），Agent新增1pass/1目标红、S3同探针2/2；响应退出2目标红；真实声明build有正向STJ和analyzer witness及S3对照。Library描述基线2/2，真实HTTP仍OpenIddictScopes缺表，不算业务验收通过；Boundary176/178同两ledger WIP。完整报告docs/review/2026-10-21-issue-129-s4-review-round2.md、r2-evidence.json、r2-probes.patch/real-build.py保存，补丁精确head cached apply-check通过，2874产品文件逐blob无差异。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5595541906 已逐字回读；44线程41resolved/3open（4333524393/4400/4407）。S3放行保持；未独立全跑441/其他未受影响全套/全解决方案或Ticket S5原生，未改开发分支生产源码、未合并、CI暂停。


### #129 S4 复审 R3（2026-10-21，2986b018）

PR #152 head `2986b018a81793129ef1135d84f2be1df208b069`（较代码4f25026b仅memory变化）：S4仍暂不放行。原R2-01 Agent与R2-03旧响应重载已关闭：Agent原样探针+API10/10、Web含原样响应探针104/104、DynamicApi141/141。原R2-02部分修复，类级LegacyStatic/隐式旧声明拒绝成立，但CCHTTP002被降Warning，真实缺transport仍exit0/无贡献metadata；旧方法级投影Attribute仍可编译，而新analyzer只查NamedType，带transport真实方法声明exit0/零warning/error/无001/002/空贡献，witness存在。需恢复Error并完整拒绝退役方法声明，不改验收标准。新增S4-R3-HTTP129-01/P2：CreateLoanDtoValidator要求领域生成的LoanDate/DueDate非空且DueDate<=Now；正常最小载荷/正常未来到期日均被真实生成invoker+validator+adapter拒绝（只有末端业务为严格Moq，不声称生产HTTP/PG），同两例154a7f90对照2/2；过去日期可通、空BookId拒绝两控制通过，metadata基线2/2。独立generator定向125、JSON162、正式E2E20全过；CapabilityEndpoint Native13/13、CRUD Native23/23，linux-x64 publish/link/run、SHA精确本head，CRUD绿16PASS+sentinel、两负例各15PASS/exit1/无sentinel。真实构建最终四例：analyzer witness、正式STJ正例、显式退役拒绝通过，缺工具链/旧方法声明拒绝仍红；原R2脚本对MSBuild“CCHTTP001 error”及precompile提前拒绝的识别局限已据完整日志校准，保留原记录，未当产品问题。报告docs/review/2026-10-21-issue-129-s4-review-round3.md、r3-evidence.json、r3-probes.patch/real-build.py保存，probe cached apply-check过，2879产品文件逐blob无差异。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5596713958 已逐字回读，45线程43resolved/2open（原诊断4333524400续回复4334461629，新Loan4334454402）。未独立全跑446/Helpdesk163/其他未受影响全套/Boundary/全解决方案/完整S2.2-B；Library生产HTTP及Ticket S5原生未验证。旧链退出、S3放行保持；未改生产源码、未合并，CI暂停，不恢复Legacy。


### #129 S4 复审 R4：放行（2026-10-21，51fc3b67）

PR #152 head `51fc3b673da18bfd717d33041c4a12cbc3fd4cee`（代码013d1188，之后仅memory变化）：S4通过，可进入S5；原诊断P2与Loan回归P2均闭合，不恢复Legacy。上轮真实消费构建脚本原样四例全满足：缺transport exit1+error CCHTTP002、方法级旧声明/LegacyStatic exit1+CCHTTP001、带transport正例exit0+真实贡献/官方STJ metadata，witness有效。Loan两条原始正常载荷探针未改、省略日期及未来到期日均经正式invoker+adapter通过；日期对照和metadata共5/5。与154a7f90相关代码一致，撤回R2误加Loan validators属于恢复迁移前无新body校验；此前要求本次迁移必须保留新增前置ID校验不成立，本轮更正，旧新增validator专属空BookId负控不作为门禁，不声称前置ID校验或生产HTTP通过。Agent原探针+API10/10，非法邮箱400/零落库；Web104、DynamicApi141、generator定向126、JSON162、正式E2E20、CapabilityEndpoint Native13、CRUD Native23全过；linux-x64原生SHA绑定本head，CRUD绿16PASS+总sentinel、两负例15PASS/exit1无总sentinel。2876产品文件逐blob无差异。新增仅非阻塞P3：Attribute顶部XML仍推荐方法级/类全部方法，需同步正式class/interface/self范围，不阻挡S5。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5597642167 正文/两闭合回复/P3逐字回读，46线程45resolved/1open(P3)。报告docs/review/2026-10-21-issue-129-s4-review-round4.md和r4-evidence.json已存。Library全HTTP仍受既有OpenIddictScopes前置、Ticket NativeAOT仍S5；未独立全跑作者163/447或其他未受影响全套/Boundary/全解决方案。下一步按S5真实Ticket服务+明确内存持久化叶子完成native/loopback/sentinel/变异/回收及最终交接；未改生产源码、未合并，CI保持暂停。


### #129 S5 复审 R1（2026-10-22，9a0c7196）

PR #152 head `9a0c71961f4937b49223b5d5c4451c276c9eff1c`（代码eb8e4807，后续docs/evidence）：S5与最终交接暂不放行，新增两项P2门禁判别力缺口，S4放行保持；未发现本切片修改生产runtime。真实Ticket契约/服务/DTO/规则和正式工具链成立；独立JIT1/1、Native3/3，linux-x64 ELF原生产物，三份SHA精确当前head：绿publish0/gate0/15PASS+sentinel，两期望变异publish0/gate1/14PASS无sentinel，fixture自身IL2026/IL3050为0。反向控制使用同一TicketNativeGate的JIT逻辑，不冒充额外native证据：01校验返回Success，真实业务进MemoryTicketRepository后才抛验证异常，仍invalid-body-zero-business PASS、15PASS+sentinel/exit0；TicketCount不变不能证明零业务/UoW，需独立计数及正向控制。02仅metadata负例Host移除真实Ticket贡献、放零端点ProblemDetails decoy，仍metadata-rejection PASS、15PASS+sentinel/exit0；需锁定真实owner/完整identities/11动作/root及精确目标错误，补N02故障控制。两patch apply-check过，临时变更均恢复，2893源/样例/fixture逐blob无差异。上轮XML P3已修正并关闭。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5607984213 正文/两inline/旧P3回复逐字回读，48线程46resolved/2open（4343475151/5156）。报告docs/review/2026-10-22-issue-129-s5-review-round1.md、s5-r1-evidence.json及probes/gate-mutations.py、late-validation.patch、decoy-metadata.patch已保存。修复仅增强fixture证据，不新增生产测试扩展点、不恢复Legacy；Library全HTTP/EF真实认证租户/其他RID仍不推导。其他runtime/Boundary/package/全解决方案未本轮重跑，S4计数仅历史证据。未合并，CI保持暂停。


### #129 S5 复审 R2：实现审查放行（2026-10-22，dbb1e2cb）

PR #152 head `dbb1e2cb4d7293254a91845171b525a5c4a0dc0d`（代码5e3878b2，后续docs/evidence）：S5及#129实现审查/本次最终交接放行，原两P2均关闭，S1–S4放行保持；未恢复Legacy。业务装饰器逐动作委托真实TicketAppService，真实UoW Begin/仓储写入口独立计数，create三项各+1、非法body400/零增量/零落库；metadata检查真实owner/完整11 endpoint-capability集合/identity数量/context/Create body+typed result roots，拒绝精确owner-action-FQN且零新增路由。独立JIT1/1、Native5/5，linux-x64一次Release publish/link、五次ELF原生产物执行，五证据SHA精确本head：正例15PASS+总sentinel/exit0；四失败控制14PASS/exit1无总sentinel，晚验证及decoy原因精确匹配，fixture自身IL2026/IL3050为0。另重放R1外部故障，不使用内置mutation开关；两旧假绿均变为目标FAIL、14PASS/exit1无sentinel（JIT共享gate证据，不并入native计数）。源码已恢复，2895文件逐blob无差异，patch apply-check过。仅非阻塞P3：实施计划顶部S4进行中/S5未开始和第60行代码SHAeb8旧文字需同步5e/current状态，不阻挡放行或要求重跑runtime。审查 https://github.com/OrchesAdam/CrestCreates/pull/152#pullrequestreview-5608279047 正文/两闭合回复/P3逐字回读，49线程48resolved/1open(P3)。报告docs/review/2026-10-22-issue-129-s5-review-round2.md、s5-r2-evidence及replay脚本/patch已存。支持仅Ticket fixture组合/所列场景/linux-x64，不推导EF/真实认证租户/整个SaaS/Library/其他RID；Library全HTTP仍OpenIddictScopes环境前置未验证，既有11红/Boundary两WIP不改口。其他全套/解决方案/Package/LocalEvent/RabbitMQ未本轮重跑，S4结果仅历史证据。本次未合并，CI保持暂停，可进入维护者人工合并流程。


### #129 已合并（2026-10-22，PR #152）

按用户明确合并指令，PR #152 已以merge commit方式合入master：`2f4c54024d8e2e9c5b8aecad1a399669dadb28a6`（2026-10-22 09:58:50 Asia/Shanghai）。最终head `13ad93ef9d59ff6e43f6e494495e123d1cb280d9` 在已审查 `dbb1e2cb` 上仅修正实施计划两行：S1–S5放行状态和证据代码SHA5e3878b2；文件全文回读一致、parent及diff核对通过，无runtime变更，沿用独立JIT1/1、Native5/5与R1外部故障重放证据。最后P3回复4343824205已闭合，49/49审查线程resolved。GitHub回读PR=MERGED、master=合并SHA；merge message Closes #129 已自动关闭Issue129（09:58:54）。三个工作流aot-validation/CI/Full Validation均disabled_manually，未恢复CI、未绕过保护。保留源分支与提交历史。#129 Controller/Legacy退出与Ticket正式AppService HTTP主链S1–S5完成；NativeAOT仅fixture组合/所列场景/linux-x64，EF/真实认证租户/Library完整HTTP/其他RID等限制仍保持。用户本地工作区原有改动未覆盖。


### #128 合并状态闭环（2026-10-22，PR #151）

已核对#128实现PR #150早于2026-10-17合入master（196b6f66），但状态文档PR #151未合并、Issue128仍open。PR151原head9b848503与master存在AGENTS/memory冲突，并仍写旧bundle待归档；在隔离副本合入master2f4c5402后保留#129全部成果，仅更正AGENTS/memory/issues log三文档（相对master8增4删，git diff --check通过），把旧链状态同步为已随#129/PR152归档，EF/其他ORM/其他RID未验证范围保持。无runtime变更，未重复运行代码测试。正常快进推送冲突解决提交62eea59e（无force），expected-head核对后PR151以merge commit `8c8a71de6a47d7c0eb94e8260e9f94035b22a477` 合入master（父2f4c5402+62eea59e，2026-10-22 10:25:17 Asia/Shanghai）；Closes #128 自动关闭Issue128（10:25:19）。GitHub回读PR151=MERGED、Issue128=CLOSED、master=8c8a71de，三个工作流仍disabled_manually。未重复合并#128实现、未回退#129、不绕过保护、不删源分支，原工作区未覆盖。

## #130 设计启动 — 2026-10-22

已从本地 master 6dd608a3 快进到 origin/master `8c8a71de6a47d7c0eb94e8260e9f94035b22a477`。同步前 memory 与九个远端同路径文档完整保留在 `99_RecycleBin/2026-10-22-issue-130-pre-pull/`（manifest 含 SHA256），正式路径使用远端版本，memory 保留本地独立审查历史。

#130 rev.1 设计与 S1–S5 计划已写入 `docs/superpowers/specs/2026-10-22-issue-130-provider-matrix-soft-delete-design.md` 和 `docs/superpowers/plans/2026-10-22-issue-130-provider-matrix-soft-delete-implementation.md`。方向：保留三个 Provider，按 Provider/数据库/能力/执行模式建立证据矩阵，不预抽共享基类；收口五删除重载、默认过滤、audit/stamp、租户及实际 UoW 参与；SDK 伪 IQueryable/未实现生成入口明确拒绝；复用 #137 内核，不扩张未实测能力。host/共享行兼容清单在 S1 冻结；FreeSql/SqlSugar 不由测试数量推导等价支持。

本轮独立基线：.NET SDK 10.0.112，OrmProviders.Tests 124/124、0 skipped；两个临时 SQLite 探针调用生产实体删除后原表计数均从1变0，确认现有 SoftDelete 测试的 null 断言允许物理删除。EF 两类仓储的 stamped Delete 也存在 ExecuteDelete 旁路（源码事实，本轮未独立执行该路径）。证据与124条FQN：`docs/review/2026-10-22-issue-130-baseline-evidence.json`，探针源码/项目保存在 `docs/review/probes/2026-10-22-issue-130-soft-delete-observation.*.txt`。当前只是设计/现状盘点，未完成目标契约验收，未更改生产 runtime；未运行新的 PG/trim/NativeAOT，不新增 ORM verified 声明。未发布 Issue 评论/创建实现 PR/合并/恢复 CI。

## #130 / PR #154 S2 独立审查 R1 — 2026-10-22

精确 head `b7f1d349d570133dccb5bb64add30fafe70b7d34`，S2暂不放行，新增三项P2：生成 tenant OR 条件未整体加括号，host/null 重新读到已软删除行；FreeSql 删除及 SqlSugar 查询/删除 raw `IsDeleted = 0` 绕过 SDK 映射列（合法 deleted_flag 映射在真实 SQLite 报 no such column）；跨命名空间同名软实体生成重复 ConfigureRowSoftDeleteFilter，CS0111/CS0121。均是S2新路径，不作为原有S3红例。

独立原始OrmProviders174过/10失败/184，0 skipped，10失败FQN与作者S3清单完全相同，历史124与作者35条D用例均通过；原始过滤器8/8、诊断集1过3约定红/4。新增真实SQLite探针1过4目标红/5（tenant正例通过，host及三个映射列入口失败）；生成器probe1过1目标红/2（不同名正例过，同名编译失败）。生成器合并定向集10过4失败/14。2881个src/samples tracked文件逐blob保持head一致，未改生产代码；本轮未重跑453生成器全量/Boundary/S1物理版本控制/PG/trim/native，不新增ORM verified。报告 `docs/review/2026-10-22-issue-130-s2-review-round1.md`，`-s2-r1-evidence.json` 与 `probes/2026-10-22-issue-130-s2-r1-probes.patch` 已保存，补丁精确head cached apply-check通过。PR已附加本chat；仅本地审查，未发布GitHub评论或review、未改实现分支/合并/恢复CI。先闭合三P2再复审S2，S3保持未启动。

## #130 / PR #154 S2 独立复审 R2 — 2026-10-22

精确 head `b466457204ad43612f10de907ff08886f55b9b5f`（实现d593d312+666816c2），S2暂不放行；R1-1 host软删除闭合，R1-2/R1-3仍各有一个P2完整性缺口。映射物理列名裸拼WHERE不带SDK标识符引用，ISoftDelete.IsDeleted合法映射为order时，两Provider CodeFirst/插入/仓储Count均成功，DeleteAsync(id)均在near order syntax error失败；deleted_flag同构控制两条通过，新映射探针2过2目标红/4。FullName将点/加号统一替换下划线仍使A.B.Row与A_B.Row碰撞ConfigureA_B_RowSoftDeleteFilter，真实生成/编译CS0111/CS0121；A_C同构正例通过，新名称探针1过1目标红/2。

独立原始OrmProviders185过10原S3失败/195，零skip，失败FQN与R1完全相同；原5条ORM探针及新6条映射回归均过；原生成器8+2共10/10，诊断集1过3约定G红/4（合并定向11过3失败/14）。原ORM探针仅新增Collection属性、方法逐字未改，生成器probe逐字相同；registry快照/恢复及非并行隔离接受，不计产品发现。2882个src/samples文件逐blob保持head一致。未独立全跑455生成器/Boundary/PG/trim/native/in-flight取消，不新增ORM verified。报告 `docs/review/2026-10-22-issue-130-s2-review-round2.md`，r2-evidence.json及r2-probes.patch已保存，补丁cached apply-check通过。仅本地复审，未发GitHub评论或review/改实现分支/合并/恢复CI；先闭合两剩余P2再复审S2，S3未放行。

## #130 / PR #154 S2 独立复审 R3：放行 — 2026-10-22

精确 head `545f73d47cb2cd8bae80c8938908eb752d67d03f`（实现9071e99a，之后三提交仅五份文档）：R2两项剩余P2闭合，本轮未发现新增阻塞P1/P2，S2放行，可进入S3；R1-1 host软删除修复保持。FreeSql接口表达式交SDK翻译live/stamp、SqlSugar SQL builder引用物理Id/stamp/live；R2原保留字映射4/4、普通+保留字映射回归10/10。完整类型身份按ASCII字母数字保留/其余逐字符_XXXX编码；R2原A.B.Row/A_B.Row探针2/2、R1同名2/2及新显式名称测试通过。

独立OrmProviders193过/10原S3失败/203，零skip；R1 ORM5/5、R2 ORM4/4、历史124均过，10失败FQN与R2完全相同。生成器原过滤器9/9+R1/R2 probe2/2+2/2=13/13，诊断集1过3约定G红/4，合并定向14过3失败/17，失败FQN与R2相同。原R1 ORM仅Collection差异、原R1生成器/R2映射/R2生成器文件逐字相同；本轮无新增探针或生产/测试源码修改，2882个src/samples tracked文件逐blob一致、隔离副本干净。报告 `docs/review/2026-10-22-issue-130-s2-review-round3.md` 及r3-evidence.json已保存。作者458全量455/3仅作者证据，本轮未独立重跑全生成器/Boundary/PG/其他DB/ORM trim/native/in-flight取消，不新增verified。S2放行不等于整个PR/#130完成；S3十T/U与S4三G保持后续门禁。本轮仅本地复审，未发GitHub评论/review、未改实现分支/合并/恢复CI，未启动或派发S3。

## #130 / PR #154 S3 独立审查 R1：暂不放行 — 2026-10-23

精确head `8c8b24fe6869116b99aafe15ae015725a2f6de49`（实现64dad166，后续仅三份文档），S3暂不放行，2P1+2P2，S2放行保持，S4未放行。P1：两EF仓储用tracked查询读取持久归属，同上下文A→B并篡改tracked TenantId后真实写入迁移原行；同租户合法detached更新又因预读重复跟踪失败（2个tracked正例过，4目标红）。P1：SDK optional读条件复用到写，普通tenant可更新/按id删除/按predicate批删host null共享行（6host正例过，6tenant目标红）。P2：正式AddFreeSqlWithUow/AddSqlSugarWithUow DI成功解析ICurrentUserProvider身份但未接Audit hook，CreatorId落库null（2红；既有A测试手工ConfigureAudit）。P2：UoW.StartAsync后caller取消且ExecutionToken确已取消，SDK入口默认CT仍在非事务受管scope中写入1行（2红；是入库前取消，不冒充in-flight验证）。

独立原始OrmProviders216/216、零skip，原十T/U FQN全绿；过滤器/R1R2生成器探针13/13，含诊断集的定向17条14过3约定G红，失败FQN与S2相同。新增ReviewS3Probes共22条：8正例过、14目标失败；使用真实SQLite/EF拦截器/生产仓储/正式DI-UoW，独立连接回读；2886个src/samples tracked文件逐blob与head一致，未改生产源码。报告 `docs/review/2026-10-23-issue-130-s3-review-round1.md`、s3-r1-evidence.json和probes补丁已保存，精确head cached apply-check过。未独立重跑458生成器全量/Boundary/PG/其他DB/ORM trim/native/in-flight/跨进程，不新增verified。仅本地审查，未发送GitHub评论或review、未改实现分支/合并/恢复CI、未启动S4。

## #130 / PR #154 S3 独立复审 R2：暂不放行 — 2026-10-23

精确head `7b06a5fb8cd0fb77205b6353f962cd1cc4c75b70`（实现1025368e，后续仅四份文档）：R1两P1闭合（EF无跟踪持久快照6/6、SDK共享行写12/12），S2放行保持，S3仍有两P2完整性缺口，S4未放行。R1-3延续：两Provider新ApplyCreationAudit的CreatorId??=保留伪造创建人，Update全实体又把被篡改的CreatorId/CreationTime写回，违反当前身份/原始创建字段权威；正式DI真实SQLite两正常创建控制过、两伪造创建+两改创建字段目标红。R1-4延续：SqlSugar入口组合token但ExecuteCommandAsync/多处查询仍无CT；ICurrentUserProvider创建快照时同步取消、SQL尚未调用，实际写入1行，FreeSql同条件0行控制过；不是in-flight SQL取消验证。

独立串行原始OrmProviders240/240、零skip，原ReviewS3Probes逐字未改22/22、新正式跨scope审计2/2、历史ORM探针9/9及映射10/10均过；新ReviewS3R2Probes共8：3控制过5目标红。2886个src/samples文件逐blob与head一致，仅隔离副本新增测试，主工作区保留。报告 `docs/review/2026-10-23-issue-130-s3-review-round2.md`、s3-r2-evidence.json和probe patch已保存，精确head cached apply-check过。本轮Tooling/测试未变，未重复CodeGenerator；作者458/455/3、Boundary176/178仅历史，不算独立复验；PG/其他DB/ORM trim/native/in-flight/跨进程未验证范围保持。仅本地审查，未发GitHub评论/review、未改实现分支/合并/恢复CI、未启动S4。先闭合两P2再复审S3。


## 2026-10-23 — Issue #130 S3 independent review R3

- PR #154 exact head `f6adf610f44a78c593fd7f87e402b12f0532d375`, implementation `733bea45`; original ORM 249/249, original R1/R2 probes byte-identical and all green.
- Audit finding closed; two additional detached EF controls confirm creation-field protection in both repository entries.
- One P2 remains from cancellation finding: FindPersistedForTenantCheckAsync has no token argument and calls FirstAsync() without CT. Deterministic cancellation after entry check but before SQL causes a SELECT, then later OCE with zero writes. GetAsync(id) control checks cancellation before launching SQL. New controls 5/6; S3 not released, S4 not started.
- Both test calls serial; 2886 tracked src/samples blobs unchanged in isolated review copy. No CodeGenerator/Boundary/PG/ORM native/in-flight/cross-process revalidation.
- Report: `docs/review/2026-10-23-issue-130-s3-review-round3.md`; evidence: `docs/review/2026-10-23-issue-130-s3-r3-evidence.json`; reproducible patch: `docs/review/probes/2026-10-23-issue-130-s3-r3-probes.patch`. No GitHub review/comment, merge, implementation-branch edits or CI reenable.


## 2026-10-23 — Issue #130 S3 independent review R4 approved

- PR #154 exact head `19997ea48fc87f0a36ddf161f3a5687512ef7362`, implementation `40940559`; final P2 closed: SqlSugar ownership pre-read receives/checks effective token and passes it to FirstAsync at all four callers. No new blocking findings.
- Independent serial ORM run 255/255, 0 skipped; original S3 R1/R2/R3 probes byte-identical and 36/36. Historical S2 ORM probes 9/9 and mapped-column regressions 10/10. Exact-head isolated copy clean; 2886 tracked src/samples blobs match.
- S3 approved within verified SQLite scope, S2 approval maintained. S4 may proceed per plan but remains unstarted here; whole-PR merge approval awaits remaining S4/S5. No CodeGenerator/Boundary/PG/ORM trim/native/in-flight/cross-process revalidation or expanded support claim.
- Report: `docs/review/2026-10-23-issue-130-s3-review-round4.md`; evidence: `docs/review/2026-10-23-issue-130-s3-r4-evidence.json`. No production changes, GitHub review/comment, merge or CI reenable.


## 2026-10-23 — Issue #130 S4 independent review R1

- PR #154 exact head `824da858012b706f9ca6f8c18325d63e491800cc`, implementation `84f25b8e` + PG `9c62a626`; G diagnostics and enum-name decoding confirmed, affected generator subset 25/25 including real MSBuild 3/3.
- Serial independent positives: CRUD non-native 23/23, native publish/link/run gate 3/3 (1m50s, green + two failing native controls), PG UoW 4/4, ORM 255/255. Native persistence is memory fixture, not EF/ORM. Full CodeGenerator 461 and Boundary counts remain author evidence.
- Two P2 acceptance gaps in new Q01: (1) removing Price, CreationTime or explicit Sorts individually still passes original query assertions; (2) removing both UoW registrations still passes original Create/fresh-context assertions through repository auto-save. No new runtime data bug reproduced. Independent copied-bed control 1 normal pass / 4 target sensitivity failures.
- S4 not released, S5 unstarted. S2/S3 approval maintained. Fix actual Q01 data/assertions and EF transaction/failure controls, then repeat fault sensitivity against updated gates. Frozen review patch demonstrates old gate gaps and is not a required unchanged runtime regression.
- Report: `docs/review/2026-10-23-issue-130-s4-review-round1.md`; JSON: `docs/review/2026-10-23-issue-130-s4-r1-evidence.json`; patch: `docs/review/probes/2026-10-23-issue-130-s4-r1-probes.patch`. 2887 tracked src/samples blobs match exact head; no production edits or external GitHub review/comment, merge or CI reenable.


## 2026-10-23 — Issue #130 S4 independent review R2 approved

- PR #154 exact head `037153db9d07d6c06f1e8b97ef6b3510daea85c1`, implementation `1f8e592e`; both R1 P2 Q01 acceptance gaps closed. No new blocking findings; S4 approved, S2/S3 maintained. S5 may proceed under existing plan but remains unstarted here; no whole-PR merge approval.
- Fresh serial CRUD non-native 25/25, including Q01 5/5. Independent copied updated main gate: normal bed passes; removing Price/CreationTime/Sorts or UoW causes expected xUnit assertion failure, 5/5 controls. Current 28 total = 25 non-native + 3 native, avoid double-counting.
- Only Q01 test/docs changed. Product and native/G/PG/ORM inputs/tree identities unchanged; prior independent generator 25, native 3, PG UoW 4, ORM 255 counts are carried, not freshly executed. Native evidence JSONs/ELF hashes revalidated. Full generator 461 and Boundary 176/178 remain author/previous evidence. Support limits unchanged.
- Report: `docs/review/2026-10-23-issue-130-s4-review-round2.md`; JSON: `docs/review/2026-10-23-issue-130-s4-r2-evidence.json`; patch: `docs/review/probes/2026-10-23-issue-130-s4-r2-probes.patch`. 2887 tracked src/samples blobs match exact head; no production edits or GitHub review/comment, merge, CI reenable or S5 execution.


## 2026-10-23 — Issue #130 S5 / unified final review R1 not approved

- PR #154 exact head `73d4775e57f584db2b256aa8b13ed666aff3a8a4`, S5 implementation `e57abee5`; original ORM 257/257 (retired hooks 2/2). S5 hook rejection works.
- Two new P1 defects in previously uncovered EF paths: both EF repository entries allow unstamped optional-tenant batch update to move a hidden tenant-a row to tenant-b and entity-list batch delete to remove it; platform EfCoreRepositoryBase ordinary GetList clears generated filters, leaking tenant-b and soft-deleted rows.
- Canonical independent probes 10 total / 4 pass / 6 target failures with generated-model and generic-EF read controls plus legal single-update controls. First harness read prechecks exposed the base read leak; final read/write probes isolate failures. No product changes.
- P2 final matrix overclaims PostgreSQL two-repository abandon/Complete and full generated write/UoW combination; PG evidence is raw-context requiresNew/isolation plus generated query only. Also align 28 total CRUD = 25 non-native + 3 native and stale PR summary counts.
- Whole PR not approved for merge; historical S2/S3/S4 covered-scenario evidence remains. 2887 tracked src/samples blobs match. Existing unaffected G/CRUD/native/PG evidence carried, not freshly run. No CI reenable, GitHub review/comment, merge or post-merge sync.
- Report: `docs/review/2026-10-23-issue-130-final-review-round1.md`; JSON: `docs/review/2026-10-23-issue-130-s5-r1-evidence.json`; reproducible patch: `docs/review/probes/2026-10-23-issue-130-s5-r1-probes.patch`.


## 2026-10-23 — Issue #130 unified final R2 approved

- PR #154 exact head `2ea73e0da27c268720198b8225124f157b0c90c2`, implementation `162bdbee`; final technical review approved within corrected support matrix, no new blocking finding. No merge executed and CI unchanged.
- Both P1s closed: tenant UpdateRange and both DeleteRange forms reject before enumeration/tracking/query/write in both EF entries; platform ordinary query starts from generated-filtered native query. Original ReviewS5Probes byte-identical and 10/10. Matrix P2 closed: PG reads/raw-context UoW are separated from unverified generated writes and two-repository abandon/Complete.
- Fresh serial ORM 267/267 and CRUD non-native 25/25 (Q01 5/5), 0 skipped. Checkout clean, 2887 product src/samples blobs match. Tooling/native inputs unchanged; prior generator/native/raw-PG evidence carried, not fresh. Native JSON/ELF hashes rechecked. Full generator 461/461 and Boundary 176/178 remain author/historical evidence.
- Report: `docs/review/2026-10-23-issue-130-final-review-round2.md`; JSON: `docs/review/2026-10-23-issue-130-final-r2-evidence.json`. Minor SHA/command/title editorial alignment remains non-blocking. No production edits, GitHub review/comment, merge, CI reenable or post-merge synchronization.


## 2026-10-23 — Issue #130 merged and closed

- Explicit user instruction authorized merging PR #154 and completing/closing Issue #130. Before merge, verified final docs head `5a5c6ff75675e0b7f515ef0a07f600a2c7a2f862` differs from approved `2ea73e0d` only in three documentation files.
- GitHub merge method merge, expected head locked; confirmed merge commit `348c4d3c35b8682e6b47b6c2dd6058b046c1113a`, target master. PR state closed/merged.
- Issue #130 body preserves the original scope, checks all acceptance items, and adds delivery summary, exact merge/review SHAs, independent versus carried test evidence, supported/unverified boundaries and immutable evidence links. Closed with state_reason completed; final readback confirmed body/state.
- CI not changed; no new test execution during merge/closure. Local checkout was not reset or pulled over existing review files. Post-merge AGENTS/cross-Issue matrix synchronization remains separately deferred as previously agreed.

## 同步保留的远端合并前记录 — 2026-10-23

以下为远端提交中的实施阶段记录；PR #154 的最新已合并状态以上方交付记录为准。

- 2026-10-23（#130 S5 最终）：ORM Provider 支持矩阵/软删除/租户/UoW 在 PR #154 完成 S1–S5（分支，未合并）。最终矩阵落 `docs/Feature/OrmProviderSupportMatrix.md`。S1 基线 124/124 → S2 删除/查询闭环 → S3 租户/装配 → S4 生成期拒绝与 EF CRUD Q01；最终 OrmProviders 267/267（含最终审查 10 条探针）、CodeGenerator 461/461（G 红例闭合，含真实 MSBuild 消费控制）、GeneratedCrud 非原生 25/25（项目总 28 = 25 + NativeAOT 3）、PG UoW 4/4、Boundary 176/178（2 条既有 #69）。支持声明按矩阵逐项；SQLServer/MySQL、ORM trim/NativeAOT、PG SDK 细节、in-flight 取消、跨进程并发未验证。所有审查轮次（S2 R1/R2、S3 R1/R2/R3、S4 R1、S5 R1）已闭合；S5 R1 修复 EF 租户批次写与平台基类普通读，矩阵已拆分 PG 查询/写入/UoW 范围；PR 未合并、CI 保持暂停。

## Remote sync — 2026-10-23

- Fresh `git fetch origin` followed by fast-forward from `8c8a71de` to `348c4d3c` (44 commits).
- All local uncommitted files were snapshotted before synchronization; three colliding #130 local
  documents were archived under `99_RecycleBin/2026-10-23-remote-sync-20261023T094409Z/collisions/`.
- Preserved #131 design/plan/inventory byte-for-byte and retained both sides of memory history.
  Backup archive and SHA-256 manifest are in the same backup directory. No source code edits,
  test/build execution, new commit, push or CI change in this synchronization turn.
