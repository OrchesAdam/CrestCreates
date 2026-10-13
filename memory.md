# CrestCreates platform status — 2026-10-10

Baseline: master `5d3de8c5` (PR #145 merge: #127 RabbitMQ static bindings S1-S4 + R1/R2 review rounds;
PR #142/#139/#140 merged before). Remote verified 2026-10-14. In flight: none —
wave 2 #126/#127 both merged; #128 CRUD JSON is the next candidate.
CI: all GitHub workflows manually disabled (development-phase
local-first verification; Testcontainers suites runnable locally via podman socket).
Workflow: GitHub PR flow, never auto-merge; replaced files archived to 99_RecycleBin,
never deleted; never output credentials.

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
- LocalEvent typed dispatch + dead-letter replay (delivered by #126 via PR #142 on
  2026-10-13, eight review rounds closed): single typed dispatch index
  (`ILocalEventDispatchRegistry` / `LocalEventBinding` / `LocalEventInvoker`, freeze-time
  merged validation; undeclared events fail deterministically, no fallback), per-assembly
  generated registration (`AddLocalEvents()`, CCCLE001-005; explicit cross-assembly
  activation contract, no scanning), replayable codecs statically bound by the JsonContracts
  toolchain (`[LocalEvent(Replayable=true)]` discovery; CJC015/016/017 closure diagnostics
  incl. polymorphic discriminator uniqueness and STJ-recoverable collection checks), shared
  replay core (`LocalDeadLetterReplayExecutor`: re-read current state, precheck before state
  transition, persisted retry budget, cancel-safe Pending restore), EF `LocalReplayKey`
  additive column, `InMemoryDeadLetterStore` same-key update fix. Not pledged: replay
  concurrency mutual exclusion, crash-leftover Retrying recovery, per-handler checkpoints.
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
