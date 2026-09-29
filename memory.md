# CrestCreates active handoff — 2026-09-29

## Goal and working rules

User goal: AI safely creates and evolves enterprise applications. Issue87 active;
88 provisional. Root designs/reviews/verifies; GPT-6 Luna high codes, medium investigates.
GitHub PR workflow, never auto-merge. Stop at actual5h limit; schedule one-time wake
after actual reset. Do not use reset cards. DEEPSEEK_API_KEY is credential env;
never print it. No additional model request is needed for the retained proposal.
AGENTS.md governs single typed/generated mainline and real native publish/link/run.
Archive files instead of deletion. Preserve root untracked docs/review/h2-mainline-closure-review.md.
History preserved in docs/review/2026-09-29-memory-history.md and earlier Sep24/Sep23 archives.

## Current slice

Worktree .worktrees/review-artifact-store-87, branch codex/phase-10c-review-artifact-store-87.
Base PR106 final6cfd52125c04b68fdcf8b46c4b68971ecdc3918a.
Implementation complete and root reviewed; local verification passed. No new PR yet.
Next commit/push stacked draft PR on106, attach, final handoff commit and dispatch ci.yml
on final head. Verify exact head/full success before ready. Never merge.

Plan docs/superpowers/plans/2026-09-24-review-artifact-store-cutover.md.
Evidence docs/review/2026-09-29-review-artifact-store.md.
PR body /tmp/crest-review-store-pr-body.md (update native result before create).
Full durable activation plan docs/superpowers/plans/2026-09-23-durable-activation-review-cutover.md.

IAgentReviewArtifactStore is sole review authority. ToolService create/get/list/latest
report/submission review checks and resolver original-review hashes use it. Removed
_reviewResults, separate review hash dictionaries/StoreReviewHashes, unused report cache.
Two old snapshot records moved to99_RecycleBin/2026-09-24-review-artifact-store;
gitignore hides archives, force-add EXACT two destinations at commit.

Envelope captures version/tenant/review/time/scope, projected DTO, PR106 report facts,
PR104 ORIGINAL canonical input. Original vs projected hashes/readiness may differ.
Finite review-time owner, no full abstract draft payload. Generated JSON, immutable
insert/no overwrite, detached memory provider, scope-bound reads, latest overall before
scope check (no fallback older review). Typed governance from captured report.
PG additiveV014 checks structured keys/draft/timestamps/version against payload.
Explicit AddCrestCreatesPostgreSqlReviewArtifactStore requires base provider kernel;
memory development stubs use same interface, never a provider-failure fallback.

Shared AgentReviewArtifactFactory centralizes projection/capture without granting
authorization or persisting. ToolService uses frozen scope/universe. CompanyCertification
sequential authoring captures exact pre-review inventories and preserves raw original
review rather than re-reviewing changed runtime catalog; generated IDs registered
at actual artifact persistence. Required constructor/writer API changes documented.

## Verified local results

- BuildTasks bootstrap and provider build passed (existing warnings).
- Full PostgreSQL448/448 in5m24s, log /tmp/crest-review-store-pg-full-0929.log.
- Full ControlPlane568/568, log /tmp/crest-review-store-cp-0929.log.
- Boundaries153 plus3 DB-backed cases passed (156, excludes CI evidence-ledger gate).
  Logs /tmp/crest-review-store-boundary-0929.log and boundary-pg-0929.log.
- CompanyCertification34/34, /tmp/crest-review-store-sample-r3-0929.log.
- Actual NativeAOT publish/link/run1/1 in1m37s, /tmp/crest-review-store-native-0929.log.
  Full envelope rich/failed roundtrip, real ToolService recreation on same memory store,
  complete fixed-clock report equality, scope denial/latest wrong-scope behavior.
  Marker CONTROL_PLANE_REVIEW_ARTIFACT_STORE_MEMORY_NATIVEAOT_OK.
  This DOES NOT establish PostgreSQL NativeAOT persistence.
- git diff --check clean. Final full CI (including JSON ownership/ledger) pending.

Earlier failures retained in historical logs: CP old Build(request) mock overloads
fixed to Build(snapshot), sample missing namespace/type aliases corrected. Native
fixture erroneously expected all3 records instead of2 broad+trimmed narrow record;
count2 plus ResultsSecurityTrimmed verified. No production filtering weakened.

## Test environment

Serialize dotnet --disable-build-servers -m:1 -p:UseSharedCompilation=false.
Local -p:NuGetAudit=false only environment workaround; defaults unchanged.
No /var/run/docker.sock, Docker CLI wrapper works. Use existing external fixture
CREST_RUNTIME_PG_CONNECTION internally, no credentials in output.
Independent container crest-review-store-tests-87,127.0.0.1:55488,
database crest_runtime_tests. Private config /tmp/crest-review-store-pg-private.json
contains connection. If /tmp lost, recover internally from THIS container's environment.
Tests create independent itest schemas. Do not use retained live proposal container.
No local process currently running. Agents idle.

## Prior PRs

All unmerged:
PR106 ready, CI35920938144 success at6cfd52125c04b68fdcf8b46c4b68971ecdc3918a.
PR105 ready, CI35889660867 success at0782ac9072d0155e9e079a44d6b2e9eef58a7965.
PR104 ready, CI35851610571 success at1d8ac231af0152d6f179380bd2ef139708c58d50.
PR103 draft design only, head0d39cd2f3ad1846f2a7d64ad3bcdb93e6b8c9429.
PR102 ready, CI35804325739 success at5d635d8f56e3c9ae2f28ccf623c4e689dd640853.
Earlier PR history in archived memory. Do not change reviewed parent heads.

## Retained live proposal — preserve

Container crest-asset-approved-inventory-87 (stopped after reboot, preserved),
port55487, schema asset_live_retained_20260923, tenant asset-live-eval-tenant.
Draft ht_asset_maintenance_initial_review-a9a00004ca5344f6832df9c25dc5e764, Created.
Locator docs/review/2026-09-23-asset-live-retained-result.json.
One real DeepSeek call passed HumanTaskDraftRetained; independent process confirmed
persistence. No approval/activation. Do not drop/replace container/schema or call model
again. Earlier failed model baselines remain failures, not rewritten as success.

## Remaining platform work

Review records only are durable here. Package/evidence previews, activation requests,
HumanTask linkage and activation intent/receipt remain separate volatile boundaries.
Post-restart submission can still fail on missing package/evidence; don't call this
durable pending approval or production activation. Only in-memory gate exists.
Issue87 cannot close yet.

Next domain: package/evidence artifact authority with original content/owner/scope,
then activation aggregate CAS+event replay identity, stable submission operation key,
request+HumanTask shared transaction/outbox, durable gate intent/receipt reconciliation.
No parallel authoritative mirror. Official package serializer retains manifest/snapshot/
evidence/hash envelopes, NOT executable descriptor definitions.
Request transaction may join PG coordinator; draft SaveAsync stays top-level.
Unknown commit requires read/verify semantic identities, not blind task reinsertion.
Do not expand to runtime installation until an actual Asset runtime owner is designed.

## Quota / wake

Sep29 14:35 wake restored5h1%,weekly16%, actual5h reset1790681738.
Consumed wake deleted; no active automation, no cards. Query actual limits before
scheduling next wake. Current local tests done; continue PR publication and CI.
