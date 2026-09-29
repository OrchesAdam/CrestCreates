# CrestCreates active handoff — 2026-09-24

## Goal and working rules

User goal: AI safely creates and evolves enterprise applications. Issue87 active;
88 provisional. Root designs/reviews/verifies; GPT-6 Luna high codes, medium investigates.
GitHub PR workflow, never auto-merge. Stop at actual5h limit; schedule one-time wake
after actual reset. Do not use reset cards. DEEPSEEK_API_KEY is credential env;
never print it. No additional model request is needed for the retained proposal.
AGENTS.md governs single typed/generated mainline and real native publish/link/run.
Archive files instead of deletion. Preserve root untracked docs/review/h2-mainline-closure-review.md.

History: docs/review/2026-09-24-memory-history.md preserves the previous full handoff;
docs/review/2026-09-23-memory-history.md preserves earlier project history.

## Current worktree and task

/home/orches/workspace/CrestCreates/.worktrees/review-report-inputs-87
branch codex/phase-10c-review-report-inputs-87
base PR105 final0782ac9072d0155e9e079a44d6b2e9eef58a7965.

Plan: docs/superpowers/plans/2026-09-24-review-report-input-snapshot.md.
Luna high luna6_review_hash_inputs implements finite versioned report facts +
source-generated JSON + single report-builder core and tests. Do not duplicate it.
Root owns memory/docs/testing. Luna high luna6_scope_native_fixture owns only the
native fixture and wrapper, covering captured input JSON roundtrip and full report
equality with fixed clock; do not duplicate either coding task. Bootstrap passed
142 prior warnings/0 errors; log /tmp/crest-review-report-inputs-bootstrap.log.

Snapshot captures projected report facts and review-time owner identity, never full
IDescriptor graphs, private topology indexes or draft payload. Derive projected hash
input from captured facts; do not store independently conflicting copies. Preserve
lazy template/time/contract/report-ID semantics. Existing Build(request) adapts to
same Build(snapshot) core; public interface overload requires external implementers
to update. Original activation hashes remain separate. No durable store implemented.

Next: review implementation, focused/full CP tests, real native snapshot roundtrip
and report build, publish stacked PR and exact-head full CI. No activation or merge.

## PR status (all unmerged)

- PR105 ready https://github.com/OrchesAdam/CrestCreates/pull/105
  WT review-artifact-owner-87; branch codex/phase-10c-review-artifact-owner-87.
  Implementation9d121ed9; final0782ac9072d0155e9e079a44d6b2e9eef58a7965.
  Full CI35889660867 passed at that final head; ready confirmed2026-09-24 04:59 wake.
  PR body updated. Preserve this verified parent head.
  Scope fix binds cached reviews/packages to captured policy fingerprint; mismatched
  reads/submission fail closed, lists trim, latest report does not fall back.
  Local focused5/5, fullCP561/561; real-service JIT passed; NativeAOT1/1 in1m29s.
  Native marker CONTROL_PLANE_PROJECTION_SCOPE_NATIVEAOT_OK; reflection disabled.
  Logs /tmp/crest-review-scope-full-r2.log, fixture-jit-r4.log, native.log
  (latter two use same crest-review-scope- prefix); artifacts/control-plane-json-aot-*.
- PR104 ready https://github.com/OrchesAdam/CrestCreates/pull/104
  WT review-hash-inputs-87; final1d8ac231af0152d6f179380bd2ef139708c58d50.
  Full CI35851610571 passed, ready confirmed. Typed captured review hash input;
  v2 digests unchanged; focused18/18, Draft132/132, CP556/556, native1/1.
- PR103 draft https://github.com/OrchesAdam/CrestCreates/pull/103
  WT durable-review-design-87; head0d39cd2f3ad1846f2a7d64ad3bcdb93e6b8c9429.
  Design only: docs/superpowers/plans/2026-09-23-durable-activation-review-cutover.md.
- PR102 ready https://github.com/OrchesAdam/CrestCreates/pull/102
  final5d635d8f56e3c9ae2f28ccf623c4e689dd640853; CI35804325739 success.
  Real live proposal retention succeeded; see retained data below.
- PR101 ready head6d83015137b79c88781eb592de650b04aab23a78 CI35673825720.
  PR100 ready head02459ad1b6768cee8f6ad967188e42e105cedf0f CI35613490902.
  Earlier PRs/head evidence in archived memory. No parent PR has been merged.

## Retained live proposal — preserve exactly

Container crest-asset-approved-inventory-87, port127.0.0.1:55487.
Persistent schema asset_live_retained_20260923; DO NOT drop or replace container data.
Tenant asset-live-eval-tenant.
Draft ht_asset_maintenance_initial_review-a9a00004ca5344f6832df9c25dc5e764, Created.
Sanitized locator/full hashes docs/review/2026-09-23-asset-live-retained-result.json.
One live call passed1/1, outcome HumanTaskDraftRetained; requested deepseek-v4-flash,
observed deepseek-flash. HTTP200 stop, prompt1661/completion2448/reasoning1989.
Post-test process query confirmed retained row. No human approval/activation occurred.
Earlier failed provider baselines remain failures, not rewritten as successful runs.
Private config /tmp/crest-asset-pg-private.json, never print; helper
/tmp/crest-run-asset-e2e.py injects connection internally. /tmp may disappear on reboot.

## Durable governance boundaries still unresolved

Current activation requests/private dictionaries and blind assignment transitions
are not durable/CAS-safe. Need one provider-owned request authority and atomic
decision+event markers; no durable side table beside authoritative dictionary.
Request+HumanTask can use shared PG transaction; supplied task InstanceId is stable
but CreateAsync insert-only, unknown commit requires read/verify. Explicit submission
operation key required; CorrelationId is not idempotency. Do not wrap draft SaveAsync
in ambient transaction (formal store requires top-level boundary).
Full immutable review/package/evidence storage must bind captured owner and scope,
original hashes and projected report facts. Existing sync hash resolver is not an
async artifact store. Package serializer contains manifest/snapshot/evidence, not
executable definitions. Authoring JSON context alone cannot serialize abstract draft
payload; use existing formal PG six-arm codec if full payload retention is needed.
Only in-memory activation gate exists. Approved/Activated tests do not establish
production installation. Durable gate intent/receipt/reconciliation and real Asset
runtime owner remain separate stages. Issue87 cannot close yet.

## Environment and quota

Serialize local dotnet with --disable-build-servers -m:1 -p:UseSharedCompilation=false.
Local -p:NuGetAudit=false is only an environment workaround, defaults unchanged.
Latest wake2026-09-23 23:57CST restored5h0%,weekly47%; consumed heartbeat deleted.
One-time heartbeat crestcreates now scheduled2026-09-24 04:59CST after actual5h
reset04:57:35, pointing at this memory. No reset card used. Query actual limits
before scheduling. Last measured quota68% used,weekly57%; coding still in progress.

Early review of new snapshot: API Capture(BuildRequest), Validate, ToReviewHashInput
implemented. Root requested preservation of empty/whitespace diagnostic messages
(only null malformed), topology count/unique-kind consistency, governance companion
field consistency. Primary agent may run one serialized compile only; notify root
before further builds. Actual snapshot/behavior tests and native run not yet executed.
No PR for this slice; uncommitted code is not yet reviewed/verified as complete.

Latest review checkpoint: builder now adapts Build(request) to Capture + Build(snapshot),
derives projected hash via snapshot.ToReviewHashInput(), and consumes finite facts.
Root inspected mapping but full diff review is pending (prior output truncated).
Requested indentation cleanup, stable ReportId/hash regression against old semantics,
and Capture.Validate before return if compatible. Primary agent adding tests to
DescriptorReviewReportBuilderTests.cs; native agent adding a separate helper/marker.
At quota89% used, neither agent had reported completed implementation or validation.
Check collaboration statuses/messages before reassigning. Build log may appear at
/tmp/crest-review-report-inputs-agent-build.log (one serialized compile authorized).
No snapshot tests or native publish/run have passed yet. PR105 last observed CI stage
was Asset Management Golden Sample; exact-head completion remains to verify.

Native coder now reports stable ReviewReportInputNativeAotFixture.cs, Program call,
and wrapper marker CONTROL_PLANE_REPORT_INPUT_NATIVEAOT_OK. Uses real topology DI,
rich typed facts, generated snapshot/report JSON, fixed clock, failed/null case and
unsupported-version rejection. No build/tests run. Primary coder may still be active;
check status before further builds. Last quota99% used; wake04:59 remains scheduled.

## Current verification — 2026-09-24 04:59 wake

Quota restored5h2%,weekly63%; wake consumed/deleted, no active automation/card use.
Report focused30/30 passed (/tmp/crest-review-report-focused.log). Root final review
requested render-time OrderBy(kind) for topology count lists (restored valid inputs
may be reordered), reversed-list regression, and rejecting null diagnostic Message
while preserving empty/whitespace strings. Luna primary applying this follow-up.
Then run full CP and actual native fixture; neither has run for this snapshot yet.
Native helper stable; primary agent's previous turn ended at quota before compile.

Final report fullCP566/566 passed (/tmp/crest-review-report-full.log). Native JIT
preflight passed after fixture-only fixes (/tmp/crest-review-report-fixture-jit-r2.log):
literal VersionedDescriptorRef fixture reference corrected without analyzer weakening,
hash-service namespace imported, compatibility assertion checks actual subject/level
instead of unused RuleId. No production workaround. Native publish/link/run now
running session53518, log /tmp/crest-review-report-native.log. Both coding agents idle.
Root review complete after sorting, structural validation and null-roundtrip fixes;
only native result and then PR/final-head full CI remain for this slice.

Native publish/link/run passed1/1 in1m32s, session53518 exit0, log
/tmp/crest-review-report-native.log. Report-input implementation and root review
complete; proceeding to stacked PR on105 and final-head full CI. No retained data
mutation, model call, approval or activation. Latest quota5h65%,weekly73%, allowed;
actual reset epoch1790215171, no active wake or reset card use.

PR106 https://github.com/OrchesAdam/CrestCreates/pull/106 created draft, stacked on
PR105, implementation6dfa2a74. Focused30/30, fullCP566/566, actual native1/1 passed.
Next verify full ci.yml dispatched on final handoff head before marking ready.
Do not merge. Next design step is immutable owner/scope-bound review artifact storage
and async resolver cutover; neither durable approval nor production activation exists.

## Successor worktree — 2026-09-24

PR106 final6cfd52125c04b68fdcf8b46c4b68971ecdc3918a fullCI35920938144 running;
verify success before ready. Local focused30/fullCP566/native1 passed.
Current worktree review-artifact-store-87, branch codex/phase-10c-review-artifact-store-87.
New plan docs/superpowers/plans/2026-09-24-review-artifact-store-cutover.md scopes the
next real cutover to REVIEW authority only, with formal PG and all review reads/writes
and resolver review hashes on one store. Package/evidence/request remain volatile;
no durable pending-review or activation claim. Investigation complete, coding not yet
dispatched. Preserve original PR104 hash INPUTS (not only hash scalars) separately
from PR106 projected report facts. Finite report owner sufficient; no draft codec.

GPT-6 Luna high /root/review_store_cutover now implements abstraction/envelope/JSON,
memory store, all runtime review paths/resolver and unit tests. It does not own PG
provider/migrations; assign provider only after stable interface reported. No local
builds authorized yet; root will serialize. No new PR for this slice yet.
One-time crestcreates wake scheduled2026-09-24 10:01CST after actual5h reset09:59:31,
points here; no cards used. PR106 CI currently Procurement Approval sample, ongoing.

Stable agreed API IAgentReviewArtifactStore: Task InsertAsync(envelope,ct),
Task<AgentReviewArtifactEnvelope?> GetAsync(tenant,reviewId,ct), GetLatestAsync(tenant,draft,ct),
Task<IReadOnlyList<AgentReviewArtifactEnvelope>> ListAsync(tenant,optionalDraft,ct).
Envelope Version/TenantId/ReviewResultId/CreatedAt/ScopeFingerprint/ProjectedReview/
ReportInput/OriginalHashInput; validate identities and shared projected semantics.
Original inputs may differ from projected facts; do not demand hash equality there.
Memory stores generated JSON for detachment, collisions never overwrite. Latest
timestamp plus ordinal ID tie-breaker (PG C collation). Provider agent
/root/review_store_postgres GPT-6 Luna high now owns PG store, additiveV014, explicit
kernel-validating DI and real PG tests. Runtime agent owns remaining runtime/tests.
Neither has permission to run builds yet; root serializes. Quota90% used latest;
agents asked to save stable partial work/handoffs. PR106 CI watch session71191,
/tmp/crest-pr106-ci-watch.log; verify exact final head and all steps before ready.

Early root review of in-progress envelope found original-to-projected eligibility/
validity equality checks too restrictive: visibility projection can change readiness.
Runtime coder asked to remove those (retain identity across original and projected),
add null validation for DTO.ValidationResult, and regression preserving distinct
original hash facts through resolver. Current edits are unbuilt/unverified; do not
claim implementation complete. PG provider/migration work in progress.

PG coder partial handoff at quota97%: provider file, explicit kernel-validating
AddCrestCreatesPostgreSqlReviewArtifactStore extension, Abstractions project reference,
additiveV014 + strict schema manifest/table-count entry saved. NO builds/tests;
PG test project untouched. Remaining real DB/DI/recovery/isolation/order/collision/
corruption/failure tests and compile review. Preserve partial work and resume agent.
PR106 CI still Procurement Approval sample at last read; not ready yet.

Runtime coder final partial handoff at99%: API/envelope/generated context, JSON memory
store, ToolService review creation/get/list/latest-report/submit routing, resolver
original-input hashing saved. StoreReviewHashes removed. Report cache removed and
ReportResourceSnapshot archived. Memory provider registered in explicit stubs.
Root original-vs-projected fix applied. INCOMPLETE constructor/manual test wiring,
unit coverage, list review, all compilation/testing. No commits. Resume existing
review_store_cutover agent for these; review_store_postgres for PG tests. Native
full artifact/service fixture not assigned yet. No implementation success claim.

## 2026-09-24 10:01 wake

Quota restored5h1%,weekly78%; actual5h reset1790233301. Consumed wake deleted, no
cards or active automation. PR106 exact final6cfd52125c04b68fdcf8b46c4b68971ecdc3918a
CI35920938144 SUCCESS; marked ready, unmerged. Runtime and PG coding agents resumed
same ownership. New GPT-6 Luna high review_store_native owns native fixture+wrapper
for full envelope/service recovery, not PostgreSQL-native evidence. No local builds
yet. Root review requested PG draft_id structured-column integrity check/test and
runtime list DTO reprojection fix; agents notified.

Latest: BuildTasks bootstrap passed142 existing warnings/0 errors; PG provider build
passed3 existing warnings/0 errors (/tmp/crest-review-store-provider-build.log).
No runtime/PG tests/native run yet. Provider+tests stable after draft_id corruption,
null decode, report rebuild, DI ordering fixes. Native agent complete/unbuilt:
ReviewArtifactStoreNativeAotFixture + ProjectionScopeNativeAotFixture checks rich/
failed envelopes, real service recreation on same memory store, scope/latest checks;
marker CONTROL_PLANE_REVIEW_ARTIFACT_STORE_MEMORY_NATIVEAOT_OK explicitly not PG native.
Runtime coder implementing shared AgentReviewArtifactFactory for formal ToolService
and existing CompanyCertification sequential-authoring producer; cannot re-review
against a changed runtime catalog or manually inject scalar hashes. See plan addendum.
Current changes incomplete/uncommitted, constructors/tests/sample still being wired.

## 2026-09-24 15:03 wake

Quota restored5h0%,weekly94%; reset5h1790251407, weekly1790729535. No cards used;
consumed wake deleted. Current environment lost /tmp logs and old exec sessions.
Prior CP test first build failed3 test compile issues, Luna fixed them. Earlier PG
tests all failed before execution because Testcontainers cannot reach Docker socket;
Docker CLI wrapper works but /var/run/docker.sock absent. No assertion evidence yet.
Shared factory + sample exact-inventory retention migration completed, unverified.
Native fixture stable. Previous new agents no longer active after environment reset.
Current CP full rerun session68680 log /tmp/crest-review-store-cp-r3.log.

Started INDEPENDENT test container crest-review-store-tests-87 port127.0.0.1:55488,
postgres16-alpine, database crest_runtime_tests. Random connection secret in
/tmp/crest-review-store-pg-private.json (connection field), do not print. Inject it
into CREST_RUNTIME_PG_CONNECTION internally for provider tests using existing external
server fixture support; every test gets fresh itest schema. Original retained
crest-asset-approved-inventory-87 is stopped after reboot, preserved and untouched.
No new PR until CP/PG/sample/native verification and final review pass.

Latest actual PG focused12/12 PASS in8s on independent container, log
/tmp/crest-review-store-pg-r2.log. CP rerun failed remaining missing implementation
Activation namespace; GPT-6 Luna high review_store_verification_fixes added import.
Current full CP rerun session30811 log /tmp/crest-review-store-cp-r4.log pending.
Weekly now99%,5h34%; no cards. Remaining: CP result/fixes, full PG suite (migration
changed), affected CompanyCertification sample tests, boundary/JSON ownership, actual
NativeAOT fixture publish/link/run, final review and stacked PR107/CI. No PR107 exists.
Private DB config /tmp may disappear at restart; recover internally from test container
environment without printing password if needed. Never use retained live container.

CP result564 passed/4 failed: Phase7dServiceIntegrationTests valid-report and captured
review-time owner assertions, ReviewProjectionScopeBindingTests narrowed/same-scope
Build verifications still mock old Build(DescriptorReviewReportBuildRequest), while
formal path now Build(DescriptorReviewReportInputSnapshot). Update mocks/callbacks and
retain owner/status/version and scope assertions; don't weaken behavior. Logs copied
to artifacts/review-store-verification/ for restart resilience (not committed artifacts).
One-time crestcreates wake now2026-09-30 08:54CST after weekly reset08:52:15.
No card used. Remaining test fixes not yet delegated because weekly99%.

## 2026-09-29 manual resume

Actual quota5h2%,weekly0%; user resumed successfully. Deleted old Sep30 wake; no
active automation/cards. Test DB crest-review-store-tests-87 restarted and private
connection recovered internally to /tmp/crest-review-store-pg-private.json; retained
live container remains stopped/untouched. BuildTasks bootstrap passed.
GPT-6 Luna high review_store_test_resume changed only failing tests to Build(snapshot),
preserving scope and adding explicit review-time status/proposed/baseVersion checks.
Full PG suite currently running session70363, /tmp/crest-review-store-pg-full-0929.log;
existing external PG env injected internally; no failures reported yet. Do not start
another dotnet build concurrently. Next full CP, affected CompanyCertification sample,
boundary/JSON ownership and native publish/link/run. New review note
docs/review/2026-09-29-review-artifact-store.md currently marks checks pending.

Full PG regression passed448/448 in5m24s, session70363 exit0. Current full CP rerun
session63875, log /tmp/crest-review-store-cp-0929.log. Test import and old overload
fixes now included; no further production changes since prior review.

Full CP passed568/568. Boundary153 passed then3 DB tests failed solely missing Docker
socket; rerun those3 with external PG passed3/3 (156 total excluding CI ledger gate).
Sample first compile found DescriptorDraft namespace collision in new helper; Luna
added DescriptorDraftEntity alias. Current sample rerun session70273, log
/tmp/crest-review-store-sample-r2-0929.log; native and JSON ownership pending.

Sample r2 exposed missing AgentReviewArtifactFactory namespace; Luna added CP import.
Current sample r3 session96595 log /tmp/crest-review-store-sample-r3-0929.log.
Native JIT preflight compiled and reached all new envelope/report markers but scope
fixture recovery failed: it expected3 results after two broad+one different-scope
reviews; correct exact-scope behavior returns2+trim diagnostic. Luna test_resume
fixing this count AND asserting trim diagnostic. Do not change production filtering.
Native publish/link/run still pending. Latest quota5h85%, weekly13%; one-time wake
crestcreates scheduledSep29 14:35CST after actualreset14:33:05, points here. No cards.

## Sep29 14:35 wake

Quota restored5h1%,weekly16%; consumed wake deleted. Sample r3 passed34/34 in9s.
Fixture previous partial fix count2+trim includes invalid Results.ReviewResultId
member (DTO has none); Luna resumed to remove it and use exact Get denial if needed.
Native actual publish/link/run remains pending. All PG448/CP568/boundary156 evidence
unchanged. No new production changes. No PR107 yet.
