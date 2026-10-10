# CrestCreates platform status — 2026-10-10

Baseline: master `85bdbb017a479503d250b6edd174a05daf620a7b` (PR #134 merge; remote
identical at check time 2026-10-10 10:34 +08:00). In flight: issue #123 native
execution gates (PR pending), issue #124 UnitOfWork unification (branch in progress).
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
- `src/Persistence/CrestCreates.Runtime.Persistence.PostgreSql`: durable runtime state,
  Control Plane reference data, review/package artifact persistence, native AotHost
  evidence.

## Evidence boundaries

- Active CI native publish+link+run gates: PG AotHost sentinels
  (incl. `CRESTCREATES_WORKFLOW_CONDITION_AOT_OK`), Memory JSON contracts,
  ControlPlane JsonContracts, Asset/Procurement Golden App fixtures, and (delivered
  by #123, PR pending) the CapabilityEndpoint native HTTP request gate
  (loopback requests, success/400/404 negative cases, process reaping, evidence
  artifact `aot-native-evidence-<sha>`); MCP/Agent.Tools main fixtures now run
  their existing publish-and-run tests in active CI.
- #123 baseline finding (recorded, fixed): native HTTP success requests returned
  500 because `DynamicApiResponse<object>` was not declared by the fixture's
  JsonSerializerContext; fixed by declaring the envelope root (golden-host pattern),
  not by disabling AOT JSON constraints. Per-item matrix:
  `docs/review/2026-10-10-issue-123-native-execution-gates-evidence-matrix.md`.
- HTTP gate scope limits: linux-x64 only; no auth/permission/tenant/bootstrap chain;
  dependency-project IL2026/IL3050 warnings remain visible but are not this gate's signal.
- Review scores (8.5/10, ~70%) are subjective single-review estimates, not
  production-readiness guarantees; support claims must point to per-item evidence.

## Open work entry (#121 orchestration)

- Wave 0: #122 baseline calibration (merged via PR #134).
- Wave 1: #123 native gates / evidence matrix (PR pending), #124 UnitOfWork (in progress),
  #125 CAP.
- Wave 2: #126 LocalEvent / #127 RabbitMQ / #128 CRUD JSON.
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
- UnitOfWork dual factory + runtime scans (#124, in progress);
  CAP entry split (#125); LocalEvent/RabbitMQ reflection bridges (#126/#127);
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
