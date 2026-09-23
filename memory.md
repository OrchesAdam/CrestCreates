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
